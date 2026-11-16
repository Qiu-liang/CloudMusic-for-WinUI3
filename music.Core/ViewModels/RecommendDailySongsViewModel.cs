using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using music.Core.Abstractions;
using music.Core.Models;
using music.Core.Mvvm;
using music.Core.Services;

namespace music.Core.ViewModels
{
    public sealed class RecommendDailySongsViewModel : SongListViewModelBase
    {
        private readonly IRecommendationService _recommendationService;
        private readonly ISessionService _sessionService;
        private readonly IDialogService _dialogs;
        private readonly IApiLog _log;
        private string _pageType = "daily";
        private string _title = "推荐歌曲";

        public RecommendDailySongsViewModel(
            IRecommendationService recommendationService,
            ISessionService sessionService,
            IPlaybackCoordinator playback,
            IDialogService dialogs,
            IApiLog log)
            : base(playback, log)
        {
            _recommendationService = recommendationService;
            _sessionService = sessionService;
            _dialogs = dialogs;
            _log = log;

            RetryCommand = new AsyncRelayCommand(() => LoadAsync(_pageType), log: log);
            LoginCommand = new AsyncRelayCommand(LoginAndReloadAsync, log: log);
        }

        public AsyncRelayCommand RetryCommand { get; }
        public AsyncRelayCommand LoginCommand { get; }

        public string Title
        {
            get => _title;
            private set => SetProperty(ref _title, value);
        }

        public async Task LoadAsync(string pageType)
        {
            _pageType = string.IsNullOrEmpty(pageType) ? "daily" : pageType;
            var (version, token) = BeginLoad();
            ShowLoading();

            try
            {
                List<Song> songs;
                string emptyMessage;
                string? failureMessage = null;

                switch (_pageType)
                {
                    case "intelligence":
                        Title = "你的红心歌曲和相似推荐";
                        var intelRes = await _recommendationService.GetIntelligenceSongsAsync(token);
                        songs = intelRes.Value;
                        if (!intelRes.Success) failureMessage = intelRes.Status.DisplayMessage;
                        emptyMessage = "暂无智能推荐";
                        break;
                    case "guess":
                        Title = "猜你喜欢的歌";
                        var guessRes = await _recommendationService.GetPersonalizedSongsAsync(100, token);
                        songs = guessRes.Value;
                        if (!guessRes.Success) failureMessage = guessRes.Status.DisplayMessage;
                        emptyMessage = "暂无推荐歌曲";
                        break;
                    default:
                        Title = "推荐歌曲";
                        var res = await _recommendationService.GetDailyRecommendSongsAsync(100, token);
                        songs = res.Value;
                        if (!res.Success) failureMessage = res.Status.DisplayMessage;
                        emptyMessage = "每日推荐为空";
                        break;
                }

                if (!IsCurrentLoad(version, token)) return;

                if (songs.Count == 0)
                {
                    ClearSongs(string.Empty);
                    ShowError(failureMessage ?? emptyMessage);
                    return;
                }

                SetSongs(songs);
                ShowContent();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested || IsDisposed)
            {
            }
            catch (Exception ex)
            {
                _log.Write($"RecommendAll Load Error: {ex}");
                if (IsCurrentLoad(version, token))
                    ShowError("加载失败");
            }
        }

        private async Task LoginAndReloadAsync()
        {
            if (await _dialogs.ShowLoginAsync() || _sessionService.IsLoggedIn)
            {
                await LoadAsync(_pageType);
            }
        }
    }
}
