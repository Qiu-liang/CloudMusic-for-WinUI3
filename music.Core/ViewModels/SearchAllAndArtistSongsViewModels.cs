using System;
using System.Threading.Tasks;
using music.Core.Abstractions;
using music.Core.Mvvm;
using music.Core.Services;

namespace music.Core.ViewModels
{
    public sealed class SearchAllSongsViewModel : SongListViewModelBase
    {
        private readonly ISearchService _searchService;
        private string _keywords = string.Empty;
        private string _title = "全部单曲";

        public SearchAllSongsViewModel(
            ISearchService searchService,
            IPlaybackCoordinator playback,
            IApiLog log)
            : base(playback, log)
        {
            _searchService = searchService;
            RetryCommand = new AsyncRelayCommand(() => LoadAsync(_keywords), log: log);
        }

        public AsyncRelayCommand RetryCommand { get; }

        public string Title
        {
            get => _title;
            private set => SetProperty(ref _title, value);
        }

        public async Task LoadAsync(string keywords)
        {
            _keywords = keywords ?? string.Empty;
            Title = $"单曲: {_keywords}";
            var (version, token) = BeginLoad();
            ShowLoading();

            try
            {
                var songs = await _searchService.SearchSongsAsync(_keywords, 100);
                if (!IsCurrentLoad(version, token)) return;

                if (songs.Count == 0)
                {
                    ClearSongs(string.Empty);
                    ShowError("暂无搜索结果");
                    return;
                }

                SetSongs(songs);
                ShowContent();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SearchAllSongs] Load Error: {ex.Message}");
                if (IsCurrentLoad(version, token))
                    ShowError("加载失败，请稍后重试");
            }
        }
    }

    public sealed class ArtistTopSongsViewModel : SongListViewModelBase
    {
        private readonly IArtistService _artistService;
        private string _artistId = string.Empty;

        public ArtistTopSongsViewModel(
            IArtistService artistService,
            IPlaybackCoordinator playback,
            IApiLog log)
            : base(playback, log)
        {
            _artistService = artistService;
        }

        public async Task LoadAsync(string artistId)
        {
            _artistId = artistId ?? string.Empty;
            var (version, token) = BeginLoad();
            ShowLoading();

            var songs = await _artistService.GetArtistSongsAsync(_artistId, 50, token);
            if (!IsCurrentLoad(version, token)) return;

            if (songs.Count == 0)
            {
                ClearSongs(string.Empty);
                ShowEmpty();
                return;
            }

            SetSongs(songs);
            ShowContent();
        }
    }
}
