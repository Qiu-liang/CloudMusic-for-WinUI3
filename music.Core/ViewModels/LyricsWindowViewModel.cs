using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using music.Core.Abstractions;
using music.Core.Models;
using music.Core.Mvvm;
using music.Core.Services;

namespace music.Core.ViewModels
{
    public sealed class LyricsWindowViewModel : ObservableObject, IDisposable
    {
        private readonly ILyricsService _lyricsService;
        private readonly ICoverArtService _coverArtService;
        private readonly IPlaybackCoordinator _playback;
        private readonly IUiDispatcher _dispatcher;
        private readonly IApiLog _log;
        private readonly LatestLyricRequest _latestLyrics = new();

        private CancellationTokenSource? _lyricsCts;
        private CancellationTokenSource? _coverCts;
        private Song? _currentSong;
        private List<LyricLine> _lyrics = new();
        private int _currentLyricIndex = -1;
        private double _lastRawMs = -1;
        private string? _loadedCoverUrl;
        private bool _disposed;

        private string _songTitle = string.Empty;
        private string _artistAlbumText = string.Empty;
        private string _coverUrl = string.Empty;
        private bool _isPlaying;
        private bool _isShuffleEnabled;
        private RepeatMode _repeatMode;
        private double _volumePercent = 100;
        private TimeSpan _position;
        private TimeSpan _duration;

        public LyricsWindowViewModel(
            ILyricsService lyricsService,
            ICoverArtService coverArtService,
            IPlaybackCoordinator playback,
            IUiDispatcher dispatcher,
            IApiLog log)
        {
            _lyricsService = lyricsService;
            _coverArtService = coverArtService;
            _playback = playback;
            _dispatcher = dispatcher;
            _log = log;
        }

        public event EventHandler<Song?>? SongReset;
        public event EventHandler? LyricsLoaded;
        public event EventHandler<int>? ActiveLineChanged;
        public event EventHandler<CoverLoadedEventArgs>? CoverBytesLoaded;

        public Song? CurrentSong => _currentSong;
        public IReadOnlyList<LyricLine> Lyrics => _lyrics;
        public int CurrentLyricIndex => _currentLyricIndex;

        public string SongTitle { get => _songTitle; private set => SetProperty(ref _songTitle, value); }
        public string ArtistAlbumText { get => _artistAlbumText; private set => SetProperty(ref _artistAlbumText, value); }
        public string CoverUrl { get => _coverUrl; private set => SetProperty(ref _coverUrl, value); }
        public bool IsPlaying { get => _isPlaying; private set => SetProperty(ref _isPlaying, value); }
        public bool IsShuffleEnabled { get => _isShuffleEnabled; private set => SetProperty(ref _isShuffleEnabled, value); }
        public RepeatMode RepeatMode { get => _repeatMode; private set => SetProperty(ref _repeatMode, value); }
        public double VolumePercent { get => _volumePercent; private set => SetProperty(ref _volumePercent, value); }
        public TimeSpan Position { get => _position; private set => SetProperty(ref _position, value); }
        public TimeSpan Duration { get => _duration; private set => SetProperty(ref _duration, value); }

        public void Initialize()
        {
            _playback.CurrentSongChanged -= OnCurrentSongChanged;
            _playback.PositionChanged -= OnPositionChanged;
            _playback.DurationChanged -= OnDurationChanged;
            _playback.PlaybackStateChanged -= OnPlaybackStateChanged;
            _playback.ShuffleChanged -= OnShuffleChanged;
            _playback.RepeatModeChanged -= OnRepeatModeChanged;
            _playback.VolumeChanged -= OnVolumeChanged;

            _playback.CurrentSongChanged += OnCurrentSongChanged;
            _playback.PositionChanged += OnPositionChanged;
            _playback.DurationChanged += OnDurationChanged;
            _playback.PlaybackStateChanged += OnPlaybackStateChanged;
            _playback.ShuffleChanged += OnShuffleChanged;
            _playback.RepeatModeChanged += OnRepeatModeChanged;
            _playback.VolumeChanged += OnVolumeChanged;

            ApplySongSnapshot(_playback.CurrentSong);
            LoadLyricsAsync().Forget(_log, "LyricsWindow.InitialLyrics");
        }

        public void TogglePlayPause() => _playback.TogglePlayPause();
        public Task PreviousAsync() => _playback.PreviousAsync();
        public Task NextAsync() => _playback.NextAsync();
        public void ToggleShuffle() => _playback.ToggleShuffle();
        public void ToggleRepeat() => _playback.ToggleRepeat();
        public void Seek(TimeSpan position) => _playback.Seek(position);

        public void SeekToLine(int index)
        {
            if (index >= 0 && index < _lyrics.Count)
            {
                _playback.Seek(_lyrics[index].Time);
            }
        }

        public void SetVolumePercent(double percent) => _playback.Volume = percent / 100.0;

        public async Task LoadLyricsAsync()
        {
            var song = _currentSong;
            if (song == null || _disposed) return;

            if (!string.IsNullOrEmpty(song.LocalFilePath))
            {
                _lyrics = new List<LyricLine>();
                _currentLyricIndex = -1;
                _dispatcher.TryEnqueue(() => { if (!_disposed) LyricsLoaded?.Invoke(this, EventArgs.Empty); });
                return;
            }

            _lyricsCts?.Cancel();
            _lyricsCts?.Dispose();
            _lyricsCts = new CancellationTokenSource();
            var ct = _lyricsCts.Token;
            var songId = song.Id;
            var request = _latestLyrics.Next(songId);

            try
            {
                var lyricInfo = await _lyricsService.GetLyricsAsync(songId, ct);
                if (_disposed || ct.IsCancellationRequested ||
                    !_latestLyrics.IsCurrent(request, songId, _currentSong?.Id ?? string.Empty))
                    return;

                var parsed = lyricInfo == null || string.IsNullOrEmpty(lyricInfo.LrcLyric)
                    ? new List<LyricLine>()
                    : LrcParser.ParseWithTranslations(lyricInfo.LrcLyric, lyricInfo.TranslatedLyric);

                _dispatcher.TryEnqueue(() =>
                {
                    if (_disposed || !_latestLyrics.IsCurrent(request, songId, _currentSong?.Id ?? string.Empty))
                        return;

                    _lyrics = parsed;
                    _currentLyricIndex = -1;
                    LyricsLoaded?.Invoke(this, EventArgs.Empty);
                });
            }
            catch (OperationCanceledException) { }
        }

        private async Task LoadCoverBytesAsync(Song song)
        {
            var coverUrl = song.CoverImgUrl;
            var songId = song.Id;
            if (string.IsNullOrEmpty(coverUrl) || string.Equals(coverUrl, _loadedCoverUrl, StringComparison.Ordinal))
                return;

            _coverCts?.Cancel();
            _coverCts?.Dispose();
            _coverCts = new CancellationTokenSource();
            var ct = _coverCts.Token;

            try
            {
                var bytes = await _coverArtService.DownloadImageBytesAsync(coverUrl, ct);
                if (_disposed || ct.IsCancellationRequested || _currentSong?.Id != songId || bytes == null || bytes.Length == 0)
                    return;

                _loadedCoverUrl = coverUrl;
                _dispatcher.TryEnqueue(() =>
                {
                    if (_disposed || _currentSong?.Id != songId) return;
                    CoverBytesLoaded?.Invoke(this, new CoverLoadedEventArgs(songId, coverUrl, bytes));
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _log.Write($"[LyricsWindow] Load cover error: {ex.Message}");
            }
        }

        private void ApplySongSnapshot(Song? song)
        {
            _currentSong = song;
            _lyrics = new List<LyricLine>();
            _currentLyricIndex = -1;
            _lastRawMs = -1;

            IsPlaying = _playback.IsPlaying;
            IsShuffleEnabled = _playback.IsShuffleEnabled;
            RepeatMode = _playback.GetRepeatMode();
            VolumePercent = _playback.Volume * 100.0;
            Duration = _playback.GetDuration();
            Position = _playback.GetCurrentPosition();

            if (song != null)
            {
                SongTitle = song.Name;
                ArtistAlbumText = string.IsNullOrEmpty(song.Album.Name)
                    ? song.ArtistNames
                    : $"{song.ArtistNames} — {song.Album.Name}";
                CoverUrl = song.CoverImgUrl;
                SongReset?.Invoke(this, song);
                if (!string.IsNullOrEmpty(song.CoverImgUrl))
                {
                    LoadCoverBytesAsync(song).Forget(_log, "LyricsWindow.LoadCover");
                }
            }
            else
            {
                SongTitle = string.Empty;
                ArtistAlbumText = string.Empty;
                CoverUrl = string.Empty;
                SongReset?.Invoke(this, null);
            }
        }

        private void OnCurrentSongChanged(object? sender, Song song)
        {
            _dispatcher.TryEnqueue(() =>
            {
                if (_disposed) return;
                ApplySongSnapshot(song);
                LoadLyricsAsync().Forget(_log, "LyricsWindow.LoadLyrics");
            });
        }

        private void OnPositionChanged(object? sender, TimeSpan position)
        {
            _dispatcher.TryEnqueue(() =>
            {
                if (_disposed) return;
                Position = position;

                double raw = position.TotalMilliseconds;
                double ms;
                if (_lastRawMs >= 0 && raw < _lastRawMs - 800)
                {
                    ms = raw;
                    _lastRawMs = raw;
                }
                else
                {
                    ms = Math.Max(raw, _lastRawMs);
                    _lastRawMs = ms;
                }

                int newIndex = LyricWindowHelpers.FindActiveLyricIndex(_lyrics, ms, _currentLyricIndex);
                if (newIndex != _currentLyricIndex)
                {
                    _currentLyricIndex = newIndex;
                    ActiveLineChanged?.Invoke(this, newIndex);
                }
            });
        }

        private void OnDurationChanged(object? sender, TimeSpan duration) =>
            _dispatcher.TryEnqueue(() => { if (!_disposed) Duration = duration; });

        private void OnPlaybackStateChanged(object? sender, bool isPlaying) =>
            _dispatcher.TryEnqueue(() => { if (!_disposed) IsPlaying = isPlaying; });

        private void OnShuffleChanged(object? sender, bool enabled) =>
            _dispatcher.TryEnqueue(() => { if (!_disposed) IsShuffleEnabled = enabled; });

        private void OnRepeatModeChanged(object? sender, RepeatMode mode) =>
            _dispatcher.TryEnqueue(() => { if (!_disposed) RepeatMode = mode; });

        private void OnVolumeChanged(object? sender, double volume) =>
            _dispatcher.TryEnqueue(() => { if (!_disposed) VolumePercent = volume * 100.0; });

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _playback.CurrentSongChanged -= OnCurrentSongChanged;
            _playback.PositionChanged -= OnPositionChanged;
            _playback.DurationChanged -= OnDurationChanged;
            _playback.PlaybackStateChanged -= OnPlaybackStateChanged;
            _playback.ShuffleChanged -= OnShuffleChanged;
            _playback.RepeatModeChanged -= OnRepeatModeChanged;
            _playback.VolumeChanged -= OnVolumeChanged;

            _lyricsCts?.Cancel();
            _lyricsCts?.Dispose();
            _lyricsCts = null;

            _coverCts?.Cancel();
            _coverCts?.Dispose();
            _coverCts = null;
        }
    }

    public sealed class CoverLoadedEventArgs : EventArgs
    {
        public CoverLoadedEventArgs(string songId, string coverUrl, byte[] bytes)
        {
            SongId = songId;
            CoverUrl = coverUrl;
            Bytes = bytes;
        }

        public string SongId { get; }
        public string CoverUrl { get; }
        public byte[] Bytes { get; }
    }
}
