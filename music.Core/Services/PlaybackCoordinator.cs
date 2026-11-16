using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using music.Core.Abstractions;
using music.Core.Models;

namespace music.Core.Services
{
    public interface IPlaybackCoordinator
    {
        event EventHandler<bool>? PlaybackStateChanged;
        event EventHandler<Song>? CurrentSongChanged;
        event EventHandler<TimeSpan>? PositionChanged;
        event EventHandler<TimeSpan>? DurationChanged;
        event EventHandler<double>? VolumeChanged;
        event EventHandler<string>? PlaybackFailed;
        event EventHandler<bool>? ShuffleChanged;
        event EventHandler<RepeatMode>? RepeatModeChanged;
        event EventHandler? PlaylistChanged;

        bool IsPlaying { get; }
        Song? CurrentSong { get; }
        bool IsShuffleEnabled { get; }
        double Volume { get; set; }

        List<Song> GetPlaylist();
        void SetPlaylist(List<Song> songs, int startIndex = 0);
        Task PlayAsync(Song song);
        Task PlayAsync(List<Song> songs, int index = 0);
        Task PlayLocalAsync(string filePath, Song song);
        Task PlayLocalFilesAsync(IList<string> filePaths, IList<Song> songs, int index = 0);
        void Play();
        void Pause();
        void TogglePlayPause();
        Task NextAsync();
        Task PreviousAsync();
        void ToggleShuffle();
        void ToggleRepeat();
        RepeatMode GetRepeatMode();
        void Seek(TimeSpan position);
        TimeSpan GetCurrentPosition();
        TimeSpan GetDuration();
        void NotifySongLikeChanged(string songId, bool isLiked);
    }

    public sealed class PlaybackCoordinator : IPlaybackCoordinator, IDisposable
    {
        private readonly IMediaPlayer _mediaPlayer;
        private readonly ISongSourceResolver _sourceResolver;
        private readonly IUiDispatcher _dispatcher;
        private readonly IApiLog _log;
        private readonly PlaybackQueue<Song> _queue = new();
        private readonly object _sourceRequestLock = new();
        private CancellationTokenSource? _sourceRequestCancellation;
        private RepeatMode _repeatMode = RepeatMode.None;
        private bool _isPlaying;

        public event EventHandler<bool>? PlaybackStateChanged;
        public event EventHandler<Song>? CurrentSongChanged;
        public event EventHandler<TimeSpan>? PositionChanged;
        public event EventHandler<TimeSpan>? DurationChanged;
        public event EventHandler<double>? VolumeChanged;
        public event EventHandler<string>? PlaybackFailed;
        public event EventHandler<bool>? ShuffleChanged;
        public event EventHandler<RepeatMode>? RepeatModeChanged;
        public event EventHandler? PlaylistChanged;

        public bool IsPlaying => _isPlaying;
        public Song? CurrentSong { get; private set; }
        public bool IsShuffleEnabled => _queue.IsShuffleEnabled;

        public double Volume
        {
            get => _mediaPlayer.Volume;
            set
            {
                _mediaPlayer.Volume = Math.Clamp(value, 0, 1);
                VolumeChanged?.Invoke(this, _mediaPlayer.Volume);
            }
        }

        public PlaybackCoordinator(
            IMediaPlayer mediaPlayer,
            ISongSourceResolver sourceResolver,
            IUiDispatcher dispatcher,
            IApiLog log)
        {
            _mediaPlayer = mediaPlayer;
            _sourceResolver = sourceResolver;
            _dispatcher = dispatcher;
            _log = log;
            _mediaPlayer.Volume = 0.8;

            _mediaPlayer.PlaybackStateChanged += OnPlaybackStateChanged;
            _mediaPlayer.PositionChanged += OnPositionChanged;
            _mediaPlayer.MediaEnded += OnMediaEnded;
            _mediaPlayer.MediaFailed += OnMediaFailed;
        }

        public List<Song> GetPlaylist() => new List<Song>(_queue.Snapshot());

        public void SetPlaylist(List<Song> songs, int startIndex = 0)
        {
            _queue.SetPlaylist(songs, startIndex);
            PlaylistChanged?.Invoke(this, EventArgs.Empty);
        }

        public Task PlayAsync(Song song)
        {
            if (song == null) return Task.CompletedTask;
            var selected = _queue.SetSingle(song);
            PlaylistChanged?.Invoke(this, EventArgs.Empty);
            return PlaySongSourceAsync(selected);
        }

        public Task PlayAsync(List<Song> songs, int index = 0)
        {
            if (songs == null || songs.Count == 0 || index < 0 || index >= songs.Count)
                return Task.CompletedTask;
            var selected = _queue.SetPlaylist(songs, index);
            PlaylistChanged?.Invoke(this, EventArgs.Empty);
            return PlaySongSourceAsync(selected);
        }

        public Task PlayLocalAsync(string filePath, Song song)
        {
            if (song == null || string.IsNullOrWhiteSpace(filePath)) return Task.CompletedTask;
            song.LocalFilePath = filePath;
            var selected = _queue.SetSingle(song);
            PlaylistChanged?.Invoke(this, EventArgs.Empty);
            return PlaySongSourceAsync(selected);
        }

        public Task PlayLocalFilesAsync(IList<string> filePaths, IList<Song> songs, int index = 0)
        {
            if (filePaths == null || songs == null || filePaths.Count != songs.Count ||
                songs.Count == 0 || index < 0 || index >= songs.Count)
                return Task.CompletedTask;

            var replacement = _queue.SetPlaylist(songs, index);
            for (var i = 0; i < songs.Count; i++) songs[i].LocalFilePath = filePaths[i];
            PlaylistChanged?.Invoke(this, EventArgs.Empty);
            return PlaySongSourceAsync(replacement);
        }

        private async Task PlaySongSourceAsync(Song song)
        {
            if (!string.IsNullOrWhiteSpace(song.LocalFilePath))
            {
                await PlayLocalSourceAsync(song.LocalFilePath, song).ConfigureAwait(false);
                return;
            }

            var cancellation = BeginSourceRequest();
            var token = cancellation.Token;
            try
            {
                var (url, result) = await _sourceResolver.ResolvePlaybackUrlAsync(song.Id, token).ConfigureAwait(false);
                if (token.IsCancellationRequested) return;
                if (string.IsNullOrEmpty(url))
                {
                    var reason = string.IsNullOrWhiteSpace(result.DisplayMessage)
                        ? "无法获取播放地址，请稍后重试。"
                        : result.DisplayMessage;
                    NotifyPlaybackFailed($"无法播放《{song.Name}》：{reason}");
                    return;
                }

                _ = _dispatcher.TryEnqueue(() =>
                {
                    if (token.IsCancellationRequested) return;
                    _ = LoadOnlineAndStartAsync(url, song, token);
                });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _log.Write($"Playback Error: {ex}");
                if (!token.IsCancellationRequested)
                {
                    NotifyPlaybackFailed($"播放《{song.Name}》失败：{ex.Message}");
                }
            }
            finally
            {
                ReleaseSourceRequest(cancellation);
            }
        }

        private async Task LoadOnlineAndStartAsync(string url, Song song, CancellationToken token)
        {
            if (token.IsCancellationRequested) return;
            try
            {
                await _mediaPlayer.LoadUriAsync(url, token).ConfigureAwait(false);
                if (token.IsCancellationRequested) return;
                StartPlayback(song);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _log.Write($"Playback Play error: {ex}");
                NotifyPlaybackFailed($"播放《{song.Name}》失败：{ex.Message}");
            }
        }

        private async Task PlayLocalSourceAsync(string filePath, Song song)
        {
            var cancellation = BeginSourceRequest();
            var token = cancellation.Token;
            try
            {
                if (string.IsNullOrWhiteSpace(filePath)) return;
                await _mediaPlayer.LoadLocalFileAsync(filePath, token).ConfigureAwait(false);
                _ = _dispatcher.TryEnqueue(() =>
                {
                    if (token.IsCancellationRequested) return;
                    StartPlayback(song);
                });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _log.Write($"Playback Local play error: {ex}");
            }
            finally
            {
                ReleaseSourceRequest(cancellation);
            }
        }

        private CancellationTokenSource BeginSourceRequest()
        {
            var next = new CancellationTokenSource();
            CancellationTokenSource? previous;
            lock (_sourceRequestLock)
            {
                previous = _sourceRequestCancellation;
                _sourceRequestCancellation = next;
            }

            // 仅取消旧请求，不在此处提前 Dispose；由各自异步流程的 finally 安全释放
            try { previous?.Cancel(); } catch (ObjectDisposedException) { }
            return next;
        }

        private void ReleaseSourceRequest(CancellationTokenSource cancellation)
        {
            lock (_sourceRequestLock)
            {
                if (ReferenceEquals(_sourceRequestCancellation, cancellation))
                {
                    _sourceRequestCancellation = null;
                }
            }

            cancellation.Dispose();
        }

        private void NotifyPlaybackFailed(string message)
        {
            _ = _dispatcher.TryEnqueue(() => PlaybackFailed?.Invoke(this, message));
        }

        private void StartPlayback(Song song)
        {
            _mediaPlayer.Play();
            CurrentSong = song;
            CurrentSongChanged?.Invoke(this, song);
        }

        public void Play()
        {
            if (_mediaPlayer.HasSource) _mediaPlayer.Play();
        }

        public void Pause() => _mediaPlayer.Pause();

        public void TogglePlayPause()
        {
            if (!_mediaPlayer.HasSource && _queue.Current != null)
            {
                _ = PlaySongSourceAsync(_queue.Current);
                return;
            }
            if (_isPlaying) Pause(); else Play();
        }

        public Task NextAsync()
        {
            var song = _queue.MoveNext();
            return song == null ? Task.CompletedTask : PlaySongSourceAsync(song);
        }

        public Task PreviousAsync()
        {
            var song = _queue.MovePrevious();
            return song == null ? Task.CompletedTask : PlaySongSourceAsync(song);
        }

        public void ToggleShuffle()
        {
            _queue.IsShuffleEnabled = !_queue.IsShuffleEnabled;
            ShuffleChanged?.Invoke(this, _queue.IsShuffleEnabled);
        }

        public void ToggleRepeat()
        {
            _repeatMode = _repeatMode switch
            {
                RepeatMode.None => RepeatMode.All,
                RepeatMode.All => RepeatMode.One,
                RepeatMode.One => RepeatMode.None,
                _ => RepeatMode.None
            };
            RepeatModeChanged?.Invoke(this, _repeatMode);
        }

        public RepeatMode GetRepeatMode() => _repeatMode;

        public void Seek(TimeSpan position)
        {
            _mediaPlayer.Position = position;
        }

        public TimeSpan GetCurrentPosition() => _mediaPlayer.Position;
        public TimeSpan GetDuration() => _mediaPlayer.NaturalDuration;

        public void NotifySongLikeChanged(string songId, bool isLiked)
        {
            if (CurrentSong != null && string.Equals(CurrentSong.Id, songId, StringComparison.Ordinal))
            {
                CurrentSong.Liked = isLiked;
            }
            foreach (var song in _queue.Snapshot())
            {
                if (string.Equals(song.Id, songId, StringComparison.Ordinal))
                {
                    song.Liked = isLiked;
                }
            }
        }

        private void OnPlaybackStateChanged(object? sender, bool isPlaying)
        {
            _ = _dispatcher.TryEnqueue(() =>
            {
                _isPlaying = isPlaying;
                PlaybackStateChanged?.Invoke(this, _isPlaying);
            });
        }

        private void OnPositionChanged(object? sender, TimeSpan position)
        {
            _ = _dispatcher.TryEnqueue(() =>
            {
                PositionChanged?.Invoke(this, position);
                DurationChanged?.Invoke(this, _mediaPlayer.NaturalDuration);
            });
        }

        private void OnMediaEnded(object? sender, EventArgs args)
        {
            _ = HandleMediaEndedAsync();
        }

        private async Task HandleMediaEndedAsync()
        {
            try
            {
                switch (_repeatMode)
                {
                    case RepeatMode.One:
                        if (CurrentSong != null) await PlaySongSourceAsync(CurrentSong).ConfigureAwait(false);
                        break;
                    case RepeatMode.All:
                        await NextAsync().ConfigureAwait(false);
                        break;
                    case RepeatMode.None:
                        if (_queue.HasNext) await NextAsync().ConfigureAwait(false);
                        break;
                }
            }
            catch (Exception ex)
            {
                _log.Write($"OnMediaEnded error: {ex.Message}");
            }
        }

        private void OnMediaFailed(object? sender, string detail)
        {
            var songName = CurrentSong?.Name;
            var fullMessage = string.IsNullOrWhiteSpace(songName)
                ? detail
                : $"无法播放《{songName}》：{detail}";

            _ = _dispatcher.TryEnqueue(() =>
            {
                _isPlaying = false;
                PlaybackStateChanged?.Invoke(this, false);
                PlaybackFailed?.Invoke(this, fullMessage);
            });
        }

        public void Dispose()
        {
            _mediaPlayer.PlaybackStateChanged -= OnPlaybackStateChanged;
            _mediaPlayer.PositionChanged -= OnPositionChanged;
            _mediaPlayer.MediaEnded -= OnMediaEnded;
            _mediaPlayer.MediaFailed -= OnMediaFailed;

            CancellationTokenSource? current;
            lock (_sourceRequestLock)
            {
                current = _sourceRequestCancellation;
                _sourceRequestCancellation = null;
            }
            try { current?.Cancel(); } catch (ObjectDisposedException) { }
            _mediaPlayer.Dispose();
        }
    }
}
