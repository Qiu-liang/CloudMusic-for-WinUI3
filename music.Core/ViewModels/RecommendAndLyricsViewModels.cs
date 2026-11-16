using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using music.Core.Abstractions;
using music.Core.Mvvm;
using music.Core.Services;
using music.Core.ViewModels.Items;
using music.Models;
using music.Services;

namespace music.Core.ViewModels
{
    public sealed class RecommendViewModel : PageViewModelBase
    {
        private readonly IRecommendationService _recommendationService;
        private readonly ISessionService _sessionService;
        private readonly IPlaybackCoordinator _playback;
        private readonly INavigationService _navigation;

        private List<Song> _dailyModels = new();
        private List<Song> _personalizedModels = new();
        private List<Song> _intelligenceModels = new();
        private List<Song> _guessModels = new();
        private bool _showLoggedInSections;

        public RecommendViewModel(
            IRecommendationService recommendationService,
            ISessionService sessionService,
            IPlaybackCoordinator playback,
            INavigationService navigation,
            IApiLog log)
        {
            _recommendationService = recommendationService;
            _sessionService = sessionService;
            _playback = playback;
            _navigation = navigation;

            Playlists = new ObservableCollection<PlaylistItem>();
            PersonalizedSongs = new ObservableCollection<SongItem>();
            RadarPlaylists = new ObservableCollection<PlaylistItem>();
            DailySongs = new ObservableCollection<SongItem>();
            IntelligenceSongs = new ObservableCollection<SongItem>();
            GuessSongs = new ObservableCollection<SongItem>();

            RefreshCommand = new AsyncRelayCommand(LoadAsync, log: log);
            PlayAllPersonalizedCommand = new AsyncRelayCommand(
                () => _personalizedModels.Count > 0 ? _playback.PlayAsync(_personalizedModels, 0) : Task.CompletedTask,
                log: log);
        }

        public ObservableCollection<PlaylistItem> Playlists { get; }
        public ObservableCollection<SongItem> PersonalizedSongs { get; }
        public ObservableCollection<PlaylistItem> RadarPlaylists { get; }
        public ObservableCollection<SongItem> DailySongs { get; }
        public ObservableCollection<SongItem> IntelligenceSongs { get; }
        public ObservableCollection<SongItem> GuessSongs { get; }

        public AsyncRelayCommand RefreshCommand { get; }
        public AsyncRelayCommand PlayAllPersonalizedCommand { get; }

        public bool ShowLoggedInSections
        {
            get => _showLoggedInSections;
            private set => SetProperty(ref _showLoggedInSections, value);
        }

        public async Task LoadAsync()
        {
            var (version, token) = BeginLoad();
            ShowLoading();

            try
            {
                var isLoggedIn = _sessionService.IsLoggedIn;

                // 严格保留原有 6 次 API 调用顺序
                var playlists = await _recommendationService.GetRecommendedPlaylistsAsync(20);
                if (!IsCurrentLoad(version, token)) return;
                var personalized = await _recommendationService.GetPersonalizedSongsAsync(20);
                if (!IsCurrentLoad(version, token)) return;
                var dailyResult = await _recommendationService.GetDailyRecommendSongsAsync(38);
                if (!IsCurrentLoad(version, token)) return;
                var daily = dailyResult.Value;
                var radar = isLoggedIn ? await _recommendationService.GetRecommendResourceAsync() : new List<RecommendedPlaylist>();
                if (!IsCurrentLoad(version, token)) return;
                var guess = await _recommendationService.GetPersonalizedSongsAsync(38);
                if (!IsCurrentLoad(version, token)) return;
                var liked = isLoggedIn ? await _recommendationService.GetIntelligenceSongsAsync() : new List<Song>();
                if (!IsCurrentLoad(version, token)) return;

                PopulatePlaylists(Playlists, playlists);

                _personalizedModels = personalized;
                PopulateSongs(PersonalizedSongs, personalized);

                _dailyModels = daily;
                PopulateSongs(DailySongs, daily);

                ShowLoggedInSections = isLoggedIn;

                RadarPlaylists.Clear();
                if (isLoggedIn)
                {
                    PopulatePlaylists(RadarPlaylists, radar);
                }

                _intelligenceModels = liked;
                IntelligenceSongs.Clear();
                if (isLoggedIn && liked.Count > 0)
                {
                    PopulateSongs(IntelligenceSongs, liked);
                }

                _guessModels = guess;
                PopulateSongs(GuessSongs, guess);

                ShowContent();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Recommend] Load Error: {ex.Message}");
                if (IsCurrentLoad(version, token))
                    ShowError("加载失败，请稍后重试");
            }
        }

        private static void PopulatePlaylists(ObservableCollection<PlaylistItem> target, List<RecommendedPlaylist> source)
        {
            target.Clear();
            foreach (var p in source)
            {
                target.Add(new PlaylistItem
                {
                    Id = p.Id,
                    Name = p.Name,
                    PicUrl = p.PicUrl,
                    PlayCount = p.PlayCount,
                    TrackCount = p.TrackCount,
                    PlayCountFormatted = p.PlayCountFormatted
                });
            }
        }

        private static void PopulateSongs(ObservableCollection<SongItem> target, List<Song> source)
        {
            target.Clear();
            foreach (var item in SongItemFactory.FromSongs(source))
            {
                target.Add(item);
            }
        }

        public Task PlayDailyAtAsync(int index) =>
            index >= 0 && index < _dailyModels.Count ? _playback.PlayAsync(_dailyModels[index]) : Task.CompletedTask;

        public Task PlayDailyListItemAsync(SongItem? item)
        {
            if (item == null) return Task.CompletedTask;
            var index = DailySongs.IndexOf(item);
            return index >= 0 && index < _dailyModels.Count
                ? _playback.PlayAsync(_dailyModels, index)
                : Task.CompletedTask;
        }

        public Task PlayPersonalizedItemAsync(SongItem? item)
        {
            if (item == null) return Task.CompletedTask;
            var index = PersonalizedSongs.IndexOf(item);
            return index >= 0 && index < _personalizedModels.Count
                ? _playback.PlayAsync(_personalizedModels[index])
                : Task.CompletedTask;
        }

        public Task PlayIntelligenceAtAsync(int index) =>
            index >= 0 && index < _intelligenceModels.Count ? _playback.PlayAsync(_intelligenceModels[index]) : Task.CompletedTask;

        public Task PlayGuessAtAsync(int index) =>
            index >= 0 && index < _guessModels.Count ? _playback.PlayAsync(_guessModels[index]) : Task.CompletedTask;

        public void OpenPlaylist(PlaylistItem? item) { if (item != null) _navigation.Navigate(PageKey.PlaylistDetail, item.Id); }
        public void OpenAllPlaylists() => _navigation.Navigate(PageKey.AllPlaylists);
        public void OpenAllDailySongs() => _navigation.Navigate(PageKey.RecommendDailySongs, "daily");
        public void OpenAllIntelligence() => _navigation.Navigate(PageKey.RecommendDailySongs, "intelligence");
        public void OpenAllRadar() => _navigation.Navigate(PageKey.RecommendRadar);
        public void OpenAllGuess() => _navigation.Navigate(PageKey.RecommendDailySongs, "guess");
    }

    public sealed class LyricsViewModel : ObservableObject, IDisposable
    {
        private readonly ILyricsService _lyricsService;
        private readonly IPlaybackCoordinator _playback;
        private readonly INavigationService _navigation;
        private readonly IUiDispatcher _dispatcher;
        private readonly IApiLog _log;
        private readonly LatestLyricRequest _latestLyrics = new();

        private CancellationTokenSource? _lyricsCancellation;
        private Song? _currentSong;
        private List<LyricLine> _lyrics = new();
        private int _currentLyricIndex = -1;
        private string _songInfoText = string.Empty;
        private bool _disposed;

        public LyricsViewModel(
            ILyricsService lyricsService,
            IPlaybackCoordinator playback,
            INavigationService navigation,
            IUiDispatcher dispatcher,
            IApiLog log)
        {
            _lyricsService = lyricsService;
            _playback = playback;
            _navigation = navigation;
            _dispatcher = dispatcher;
            _log = log;
        }

        public event EventHandler? LyricsUpdated;
        public event EventHandler<int>? ActiveLineChanged;

        public string SongInfoText { get => _songInfoText; private set => SetProperty(ref _songInfoText, value); }
        public IReadOnlyList<LyricLine> Lyrics => _lyrics;
        public int CurrentLyricIndex => _currentLyricIndex;

        public void Activate(Song? song)
        {
            _playback.CurrentSongChanged -= OnCurrentSongChanged;
            _playback.PositionChanged -= OnPositionChanged;
            _playback.CurrentSongChanged += OnCurrentSongChanged;
            _playback.PositionChanged += OnPositionChanged;

            if (song != null)
            {
                _currentSong = song;
                SongInfoText = $"{song.Name} - {song.ArtistNames}";
                LoadLyricsAsync().Forget(_log, "LyricsPage.Load");
            }
        }

        public void Close()
        {
            if (_navigation.CanGoBack)
                _navigation.GoBack();
        }

        private async Task LoadLyricsAsync()
        {
            if (_currentSong == null || _disposed) return;

            if (!string.IsNullOrEmpty(_currentSong.LocalFilePath))
            {
                _lyrics = new List<LyricLine>();
                _currentLyricIndex = -1;
                _dispatcher.TryEnqueue(() => LyricsUpdated?.Invoke(this, EventArgs.Empty));
                return;
            }

            _lyricsCancellation?.Cancel();
            _lyricsCancellation?.Dispose();
            _lyricsCancellation = new CancellationTokenSource();
            var cancellation = _lyricsCancellation;
            var songId = _currentSong.Id;
            var request = _latestLyrics.Next(songId);

            try
            {
                var lyricInfo = await _lyricsService.GetLyricsAsync(songId, cancellation.Token);
                if (_disposed || cancellation.IsCancellationRequested) return;
                if (!_latestLyrics.IsCurrent(request, songId, _currentSong?.Id ?? string.Empty)) return;

                _lyrics = lyricInfo == null || string.IsNullOrEmpty(lyricInfo.LrcLyric)
                    ? new List<LyricLine>()
                    : LrcParser.ParseSimple(lyricInfo.LrcLyric);
                _currentLyricIndex = -1;
                _dispatcher.TryEnqueue(() =>
                {
                    if (!_disposed) LyricsUpdated?.Invoke(this, EventArgs.Empty);
                });
            }
            catch (OperationCanceledException) { }
        }

        private void OnCurrentSongChanged(object? sender, Song song)
        {
            if (_disposed) return;
            _currentSong = song;
            _lyrics = new List<LyricLine>();
            _currentLyricIndex = -1;
            _dispatcher.TryEnqueue(() =>
            {
                if (_disposed) return;
                SongInfoText = $"{song.Name} - {song.ArtistNames}";
            });
            LoadLyricsAsync().Forget(_log, "LyricsPage.SongChanged");
        }

        private void OnPositionChanged(object? sender, TimeSpan position)
        {
            if (_disposed) return;
            _dispatcher.TryEnqueue(() =>
            {
                if (_disposed) return;
                var currentMs = position.TotalMilliseconds;
                var newIndex = -1;
                for (int i = _lyrics.Count - 1; i >= 0; i--)
                {
                    if (currentMs >= _lyrics[i].Time.TotalMilliseconds)
                    {
                        newIndex = i;
                        break;
                    }
                }

                if (newIndex != _currentLyricIndex)
                {
                    _currentLyricIndex = newIndex;
                    ActiveLineChanged?.Invoke(this, newIndex);
                }
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _playback.CurrentSongChanged -= OnCurrentSongChanged;
            _playback.PositionChanged -= OnPositionChanged;
            _lyricsCancellation?.Cancel();
            _lyricsCancellation?.Dispose();
            _lyricsCancellation = null;
        }
    }
}
