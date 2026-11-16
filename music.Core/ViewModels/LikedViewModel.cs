using System.Threading.Tasks;
using music.Core.Abstractions;
using music.Core.Mvvm;
using music.Core.Services;
using music.Services;

namespace music.Core.ViewModels
{
    public sealed class LikedViewModel : SongListViewModelBase
    {
        private readonly ILibraryService _libraryService;
        private readonly ISessionService _sessionService;
        private readonly IDialogService _dialogs;
        private readonly IUiDispatcher _dispatcher;
        private readonly IApiLog _log;

        public LikedViewModel(
            ILibraryService libraryService,
            ISessionService sessionService,
            IPlaybackCoordinator playback,
            IDialogService dialogs,
            IUiDispatcher dispatcher,
            IApiLog log)
            : base(playback, log)
        {
            _libraryService = libraryService;
            _sessionService = sessionService;
            _dialogs = dialogs;
            _dispatcher = dispatcher;
            _log = log;

            RetryCommand = new AsyncRelayCommand(LoadAsync, log: log);
            LoginCommand = new AsyncRelayCommand(LoginAndReloadAsync, log: log);

            _libraryService.SongLikeChanged += OnSongLikeChanged;
        }

        public AsyncRelayCommand RetryCommand { get; }
        public AsyncRelayCommand LoginCommand { get; }

        public async Task LoadAsync()
        {
            var (version, token) = BeginLoad();

            if (!_sessionService.IsLoggedIn)
            {
                ClearSongs(string.Empty);
                ShowError("请先登录");
                return;
            }

            ShowLoading();

            var result = await _libraryService.GetLikedSongsAsync(limit: 0, bypassCache: true);
            if (!IsCurrentLoad(version, token)) return;

            if (!result.Success)
            {
                ClearSongs(string.Empty);
                ShowError(result.Status.DisplayMessage);
                return;
            }

            if (result.Value.Count == 0)
            {
                ClearSongs();
                ShowEmpty();
                return;
            }

            SetSongs(result.Value, forceLiked: true);
            ShowContent();
        }

        private async Task LoginAndReloadAsync()
        {
            if (await _dialogs.ShowLoginAsync())
            {
                await LoadAsync();
            }
        }

        private void OnSongLikeChanged(object? sender, SongLikeChangedEventArgs e)
        {
            _dispatcher.TryEnqueue(() =>
            {
                if (!IsDisposed)
                    LoadAsync().Forget(_log, "Liked.Reload");
            });
        }

        public override void Dispose()
        {
            _libraryService.SongLikeChanged -= OnSongLikeChanged;
            base.Dispose();
        }
    }
}
