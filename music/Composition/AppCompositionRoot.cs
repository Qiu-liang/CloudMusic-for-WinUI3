using System;
using Microsoft.UI.Dispatching;
using Windows.Storage;
using music.Core.Abstractions;
using music.Core.Netease;
using music.Core.Services;
using music.Core.ViewModels;
using music.Dialogs;
using music.Infrastructure.Platform;
using music.Pages;

namespace music.Composition
{
    internal sealed class AppCompositionRoot : IDisposable
    {
        public LocalSettingsStore SettingsStore { get; }
        public WindowsCredentialStore CredentialStore { get; }
        public FileApiLog ApiLog { get; }

        public ServerEndpoint Endpoint { get; }
        public SessionCookieJar CookieJar { get; }
        public NeteaseHttpTransport Transport { get; }

        public QualityService QualityService { get; }
        public SongSourceResolver SongSourceResolver { get; }
        public LibraryService LibraryService { get; }
        public SessionService SessionService { get; }
        public PlaylistService PlaylistService { get; }
        public ArtistService ArtistService { get; }
        public RecommendationService RecommendationService { get; }
        public SearchService SearchService { get; }
        public CloudService CloudService { get; }
        public ProfileService ProfileService { get; }
        public LyricsService LyricsService { get; }
        public DownloadService DownloadService { get; }
        public ServerConnectionTester ServerConnectionTester { get; }
        public CoverArtService CoverArtService { get; }
        public LocalMusicService LocalMusicService { get; }
        public SettingsService SettingsService { get; }

        // UI / 平台能力（OnLaunched 时初始化）
        public WinUiDispatcher? UiDispatcher { get; private set; }
        public PlaybackCoordinator? PlaybackCoordinator { get; private set; }
        public WindowHost WindowHost { get; } = new();
        public NavigationService? NavigationService { get; private set; }
        public DialogService? DialogService { get; private set; }
        public FilePickerService? FilePickerService { get; private set; }
        public ThemeService? ThemeService { get; private set; }
        public LyricsWindowLauncher? LyricsWindowLauncher { get; private set; }

        public AppCompositionRoot()
        {
            SettingsStore = new LocalSettingsStore(ApplicationData.Current.LocalSettings);
            CredentialStore = new WindowsCredentialStore(ApplicationData.Current.LocalSettings);
            ApiLog = new FileApiLog();

            Endpoint = new ServerEndpoint(SettingsStore, ApiLog);
            CookieJar = new SessionCookieJar(CredentialStore, ApiLog);
            Transport = new NeteaseHttpTransport(Endpoint, CookieJar, SettingsStore, ApiLog);

            QualityService = new QualityService(Transport, SettingsStore);
            SongSourceResolver = new SongSourceResolver(Transport, SettingsStore);

            LibraryService = null!;
            SessionService = new SessionService(
                Transport,
                Endpoint,
                CookieJar,
                SettingsStore,
                ApiLog,
                () => QualityService.ClearCache(),
                () => LibraryService?.ClearState());

            LibraryService = new LibraryService(
                Transport,
                QualityService,
                () => SessionService.IsLoggedIn,
                () => SessionService.UserId);

            PlaylistService = new PlaylistService(Transport, LibraryService, QualityService);
            ArtistService = new ArtistService(Transport, LibraryService, QualityService);
            RecommendationService = new RecommendationService(
                Transport, SessionService, LibraryService, QualityService, CookieJar);
            SearchService = new SearchService(Transport, LibraryService, QualityService);
            CloudService = new CloudService(Transport, SessionService);
            ProfileService = new ProfileService(Transport);
            LyricsService = new LyricsService(Transport);
            DownloadService = new DownloadService(
                SongSourceResolver,
                SettingsStore,
                ApplicationData.Current.LocalFolder.Path);
            ServerConnectionTester = new ServerConnectionTester();
            CoverArtService = new CoverArtService();
            LocalMusicService = new LocalMusicService(SettingsStore);
            SettingsService = new SettingsService(SettingsStore, Endpoint);
        }

        public void InitializeUi(DispatcherQueue dispatcherQueue)
        {
            if (UiDispatcher != null) return;

            UiDispatcher = new WinUiDispatcher(dispatcherQueue);
            var mediaPlayer = new MediaPlayerAdapter();
            PlaybackCoordinator = new PlaybackCoordinator(mediaPlayer, SongSourceResolver, UiDispatcher, ApiLog);

            NavigationService = new NavigationService(WindowHost);
            DialogService = new DialogService(WindowHost, SessionService, CreateLoginDialog);
            FilePickerService = new FilePickerService(WindowHost);
            ThemeService = new ThemeService(SettingsStore, WindowHost);
            LyricsWindowLauncher = new LyricsWindowLauncher(SettingsStore, CreateLyricsWindow);

            RegisterRoutes(NavigationService);
        }

        private void RegisterRoutes(NavigationService nav)
        {
            nav.Register(PageKey.Home, typeof(HomePage));
            nav.Register(PageKey.Recommend, typeof(RecommendPage), () => new RecommendViewModel(
                RecommendationService,
                SessionService,
                PlaybackCoordinator!,
                nav,
                ApiLog));
            nav.Register(PageKey.Liked, typeof(LikedPage), () => new LikedViewModel(
                LibraryService,
                SessionService,
                PlaybackCoordinator!,
                DialogService!,
                UiDispatcher!,
                ApiLog));
            nav.Register(PageKey.Downloaded, typeof(DownloadedPage), () => new DownloadedViewModel(nav));
            nav.Register(PageKey.Cloud, typeof(CloudPage), () => new CloudViewModel(
                CloudService,
                SessionService,
                PlaybackCoordinator!,
                DialogService!,
                FilePickerService!,
                ApiLog));
            nav.Register(PageKey.Playlist, typeof(PlaylistPage));
            nav.Register(PageKey.Collection, typeof(CollectionPage));
            nav.Register(PageKey.PlaylistDetail, typeof(PlaylistDetailPage), () => new PlaylistDetailViewModel(
                PlaylistService,
                ArtistService,
                LibraryService,
                SessionService,
                PlaybackCoordinator!,
                DialogService!,
                UiDispatcher!,
                ApiLog));
            nav.Register(PageKey.ArtistDetail, typeof(ArtistDetailPage), () => new ArtistDetailViewModel(
                ArtistService,
                ProfileService,
                PlaybackCoordinator!,
                nav));
            nav.Register(PageKey.ArtistTopSongs, typeof(ArtistTopSongsPage), () => new ArtistTopSongsViewModel(
                ArtistService,
                PlaybackCoordinator!,
                ApiLog));
            nav.Register(PageKey.ArtistAlbums, typeof(ArtistAlbumsPage), () => new ArtistAlbumsViewModel(
                ArtistService,
                nav));
            nav.Register(PageKey.ArtistInfo, typeof(ArtistInfoPage), () => new ArtistInfoViewModel(
                ArtistService));
            nav.Register(PageKey.AllPlaylists, typeof(AllPlaylistsPage), () => new AllPlaylistsViewModel(
                RecommendationService,
                nav,
                ApiLog));
            nav.Register(PageKey.Search, typeof(SearchPage), () => new SearchViewModel(
                SearchService,
                PlaybackCoordinator!,
                nav));
            nav.Register(PageKey.SearchAllResults, typeof(SearchAllResultsPage), () => new SearchAllResultsViewModel(
                SearchService,
                nav,
                ApiLog));
            nav.Register(PageKey.SearchAllSongs, typeof(SearchAllSongsPage), () => new SearchAllSongsViewModel(
                SearchService,
                PlaybackCoordinator!,
                ApiLog));
            nav.Register(PageKey.RecommendDailySongs, typeof(RecommendDailySongsPage), () => new RecommendDailySongsViewModel(
                RecommendationService,
                SessionService,
                PlaybackCoordinator!,
                DialogService!,
                ApiLog));
            nav.Register(PageKey.RecommendRadar, typeof(RecommendRadarPage), () => new RecommendRadarViewModel(
                RecommendationService,
                nav,
                ApiLog));
            nav.Register(PageKey.Profile, typeof(ProfilePage), () => new ProfileViewModel(
                ProfileService,
                SessionService,
                nav,
                nav));
            nav.Register(PageKey.ProfilePlaylists, typeof(ProfilePlaylistsPage), () => new ProfilePlaylistsViewModel(
                PlaylistService,
                SessionService,
                nav));
            nav.Register(PageKey.ProfileFollowList, typeof(ProfileFollowListPage), () => new ProfileFollowListViewModel(
                ProfileService,
                SessionService,
                nav));
            nav.Register(PageKey.Lyrics, typeof(LyricsPage), () => new LyricsViewModel(
                LyricsService,
                PlaybackCoordinator!,
                nav,
                UiDispatcher!,
                ApiLog));
            nav.Register(PageKey.Settings, typeof(SettingsPage), () => new SettingsViewModel(
                SettingsService,
                ThemeService!,
                QualityService,
                SessionService,
                DownloadService,
                ServerConnectionTester,
                PlaybackCoordinator!,
                DialogService!,
                FilePickerService!,
                UiDispatcher!));
            nav.Register(PageKey.Download, typeof(DownloadPage), () => new DownloadViewModel(
                DownloadService,
                PlaybackCoordinator!,
                DialogService!,
                FilePickerService!,
                ApiLog));
            nav.Register(PageKey.Local, typeof(LocalPage), () => new LocalViewModel(
                LocalMusicService,
                PlaybackCoordinator!,
                FilePickerService!,
                ApiLog));
            nav.Register(PageKey.Recent, typeof(RecentPage), () => new RecentViewModel(
                ProfileService,
                SessionService,
                PlaybackCoordinator!,
                ApiLog));
        }

        public void AttachMainWindow(MainWindow window, Microsoft.UI.Xaml.Controls.Frame contentFrame, Action<string> applyTheme)
        {
            WindowHost.Attach(window, contentFrame, applyTheme);
            NavigationService?.AttachFrame(contentFrame);
        }

        public void DetachMainWindow()
        {
            NavigationService?.DetachFrame();
            WindowHost.Detach();
        }

        public ShellViewModel CreateShellViewModel() => new ShellViewModel(
            SessionService,
            PlaylistService,
            NavigationService!,
            DialogService!,
            UiDispatcher!,
            ApiLog);

        public PlayerBarViewModel CreatePlayerBarViewModel() => new PlayerBarViewModel(
            PlaybackCoordinator!,
            LibraryService,
            QualityService,
            SessionService,
            PlaylistService,
            DownloadService,
            LyricsWindowLauncher!,
            NavigationService!,
            DialogService!,
            UiDispatcher!,
            ApiLog);

        public SearchSuggestionsViewModel CreateSearchSuggestionsViewModel() =>
            new SearchSuggestionsViewModel(SearchService, NavigationService!);

        public MainWindow CreateMainWindow()
        {
            var window = new MainWindow(this);
            window.Closed += (_, _) =>
            {
                DetachMainWindow();
                Dispose();
            };
            return window;
        }

        public LyricsWindow CreateLyricsWindow() => new LyricsWindow(
            new LyricsWindowViewModel(
                LyricsService,
                CoverArtService,
                PlaybackCoordinator!,
                UiDispatcher!,
                ApiLog));

        public LoginDialog CreateLoginDialog() => new LoginDialog(new LoginViewModel(SessionService, ApiLog));

        public void Dispose()
        {
            PlaybackCoordinator?.Dispose();
            Transport.Dispose();
            DownloadService.Dispose();
            ServerConnectionTester.Dispose();
            CoverArtService.Dispose();
        }
    }
}
