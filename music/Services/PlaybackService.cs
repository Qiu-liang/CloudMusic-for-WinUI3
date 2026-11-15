using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using music.Models;

namespace music.Services
{
    public enum RepeatMode { None, All, One }

    public class PlaybackService
    {
        private readonly MediaPlayer _mediaPlayer;
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly PlaybackQueue<Song> _queue = new();
        private RepeatMode _repeatMode = RepeatMode.None;
        private bool _isPlaying;
        private CancellationTokenSource? _sourceRequestCancellation;
        private readonly object _sourceRequestLock = new();

        public event EventHandler<bool>? PlaybackStateChanged;
        public event EventHandler<Song>? CurrentSongChanged;
        public event EventHandler<TimeSpan>? PositionChanged;
        public event EventHandler<TimeSpan>? DurationChanged;
        public event EventHandler<double>? VolumeChanged;
        public event EventHandler<string>? PlaybackFailed;

        public bool IsPlaying => _isPlaying;
        public Song? CurrentSong { get; private set; }
        public List<Song> GetPlaylist() => new List<Song>(_queue.Snapshot());
        public double Volume
        {
            get => _mediaPlayer?.Volume ?? 0.8;
            set { if (_mediaPlayer != null) { _mediaPlayer.Volume = Math.Clamp(value, 0, 1); VolumeChanged?.Invoke(this, _mediaPlayer.Volume); } }
        }

        public PlaybackService()
        {
            _mediaPlayer = new MediaPlayer();
            _mediaPlayer.Volume = 0.8;
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _mediaPlayer.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;
            _mediaPlayer.PlaybackSession.PositionChanged += OnPositionChanged;
            _mediaPlayer.MediaEnded += OnMediaEnded;
            _mediaPlayer.MediaFailed += OnMediaFailed;
        }

        public void SetPlaylist(List<Song> songs, int startIndex = 0)
        {
            _queue.SetPlaylist(songs, startIndex);
        }

        public Task PlayAsync(Song song)
        {
            if (song == null) return Task.CompletedTask;
            return PlayCurrentAsync(_queue.SetSingle(song));
        }

        public Task PlayAsync(List<Song> songs, int index = 0)
        {
            if (songs == null || songs.Count == 0 || index < 0 || index >= songs.Count)
                return Task.CompletedTask;
            return PlayCurrentAsync(_queue.SetPlaylist(songs, index));
        }

        public Task PlayLocalAsync(string filePath, Song song)
        {
            if (song == null || string.IsNullOrWhiteSpace(filePath)) return Task.CompletedTask;
            song.LocalFilePath = filePath;
            return PlaySongSourceAsync(_queue.SetSingle(song));
        }

        public Task PlayLocalFilesAsync(IList<string> filePaths, IList<Song> songs, int index = 0)
        {
            if (filePaths == null || songs == null || filePaths.Count != songs.Count ||
                songs.Count == 0 || index < 0 || index >= songs.Count)
                return Task.CompletedTask;

            var replacement = _queue.SetPlaylist(songs, index);
            for (var i = 0; i < songs.Count; i++) songs[i].LocalFilePath = filePaths[i];
            return PlaySongSourceAsync(replacement);
        }

        private Task PlayCurrentAsync(Song song) => PlaySongSourceAsync(song);

        private async Task PlaySongSourceAsync(Song song)
        {
            if (!string.IsNullOrWhiteSpace(song.LocalFilePath))
            {
                await PlayLocalSourceAsync(song.LocalFilePath, song);
                return;
            }

            var cancellation = BeginSourceRequest();
            var token = cancellation.Token;
            try
            {
                var (url, result) = await App.ApiService.GetSongUrlWithResultAsync(song.Id, token);
                if (token.IsCancellationRequested) return;
                if (string.IsNullOrEmpty(url))
                {
                    var reason = string.IsNullOrWhiteSpace(result.DisplayMessage)
                        ? "无法获取播放地址，请稍后重试。"
                        : result.DisplayMessage;
                    NotifyPlaybackFailed($"无法播放《{song.Name}》：{reason}");
                    return;
                }

                _ = _dispatcherQueue.TryEnqueue(() => PlayOnlineSource(url, song, token));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Playback] Error: {ex.Message}");
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

        private async Task PlayLocalSourceAsync(string filePath, Song song)
        {
            var cancellation = BeginSourceRequest();
            var token = cancellation.Token;
            try
            {
                if (string.IsNullOrWhiteSpace(filePath)) return;
                var file = await StorageFile.GetFileFromPathAsync(filePath).AsTask(token);
                _ = _dispatcherQueue.TryEnqueue(() =>
                {
                    if (token.IsCancellationRequested) return;
                    _mediaPlayer.Source = MediaSource.CreateFromStorageFile(file);
                    StartPlayback(song);
                });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Playback] Local file API error: {ex.Message}");
                _ = _dispatcherQueue.TryEnqueue(() =>
                {
                    if (token.IsCancellationRequested || !File.Exists(filePath)) return;
                    _mediaPlayer.Source = MediaSource.CreateFromUri(new Uri(Path.GetFullPath(filePath)));
                    StartPlayback(song);
                });
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

        private void PlayOnlineSource(string url, Song song, CancellationToken token)
        {
            if (token.IsCancellationRequested) return;
            try
            {
                _mediaPlayer.Source = MediaSource.CreateFromUri(new Uri(url));
                StartPlayback(song);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Playback] Play error: {ex.Message}");
                NotifyPlaybackFailed($"播放《{song.Name}》失败：{ex.Message}");
            }
        }

        private void NotifyPlaybackFailed(string message)
        {
            _ = _dispatcherQueue.TryEnqueue(() => PlaybackFailed?.Invoke(this, message));
        }

        private void StartPlayback(Song song)
        {
            _mediaPlayer.Play();
            CurrentSong = song;
            CurrentSongChanged?.Invoke(this, song);
        }

        public void Play()
        {
            if (_mediaPlayer?.Source != null) _mediaPlayer.Play();
        }

        public void Pause() => _mediaPlayer?.Pause();

        public void TogglePlayPause()
        {
            if (_mediaPlayer?.Source == null && _queue.Current != null)
            {
                _ = PlaySongSourceAsync(_queue.Current);
                return;
            }
            if (_isPlaying) Pause(); else Play();
        }

        public Task NextAsync()
        {
            var song = _queue.MoveNext();
            return song == null ? Task.CompletedTask : PlayCurrentAsync(song);
        }

        public Task PreviousAsync()
        {
            var song = _queue.MovePrevious();
            return song == null ? Task.CompletedTask : PlayCurrentAsync(song);
        }

        public void ToggleShuffle() => _queue.IsShuffleEnabled = !_queue.IsShuffleEnabled;

        public void ToggleRepeat() => _repeatMode = _repeatMode switch
        {
            RepeatMode.None => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            RepeatMode.One => RepeatMode.None,
            _ => RepeatMode.None
        };

        public RepeatMode GetRepeatMode() => _repeatMode;
        public bool IsShuffleEnabled => _queue.IsShuffleEnabled;

        public void Seek(TimeSpan position)
        {
            if (_mediaPlayer?.PlaybackSession != null)
                _mediaPlayer.PlaybackSession.Position = position;
        }

        public TimeSpan GetCurrentPosition() => _mediaPlayer?.PlaybackSession?.Position ?? TimeSpan.Zero;
        public TimeSpan GetDuration() => _mediaPlayer?.PlaybackSession?.NaturalDuration ?? TimeSpan.Zero;

        private void OnPlaybackStateChanged(MediaPlaybackSession sender, object args)
        {
            _ = _dispatcherQueue.TryEnqueue(() =>
            {
                _isPlaying = sender.PlaybackState == MediaPlaybackState.Playing;
                PlaybackStateChanged?.Invoke(this, _isPlaying);
            });
        }

        private void OnPositionChanged(MediaPlaybackSession sender, object args)
        {
            _ = _dispatcherQueue.TryEnqueue(() =>
            {
                PositionChanged?.Invoke(this, sender.Position);
                DurationChanged?.Invoke(this, sender.NaturalDuration);
            });
        }

        private async void OnMediaEnded(MediaPlayer sender, object args)
        {
            switch (_repeatMode)
            {
                case RepeatMode.One:
                    if (CurrentSong != null) await PlayCurrentAsync(CurrentSong);
                    break;
                case RepeatMode.All:
                    await NextAsync();
                    break;
                case RepeatMode.None:
                    if (_queue.HasNext) await NextAsync();
                    break;
            }
        }

        private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
        {
            var detail = string.IsNullOrWhiteSpace(args.ErrorMessage)
                ? $"媒体加载失败（{args.Error}）。"
                : $"媒体加载失败：{args.ErrorMessage}";
            var songName = CurrentSong?.Name;
            var fullMessage = string.IsNullOrWhiteSpace(songName)
                ? detail
                : $"无法播放《{songName}》：{detail}";

            _ = _dispatcherQueue.TryEnqueue(() =>
            {
                _isPlaying = false;
                PlaybackStateChanged?.Invoke(this, false);
                PlaybackFailed?.Invoke(this, fullMessage);
            });
        }

        public void Dispose()
        {
            CancellationTokenSource? current;
            lock (_sourceRequestLock)
            {
                current = _sourceRequestCancellation;
                _sourceRequestCancellation = null;
            }
            try { current?.Cancel(); } catch (ObjectDisposedException) { }
            _mediaPlayer?.Dispose();
        }
    }
}
