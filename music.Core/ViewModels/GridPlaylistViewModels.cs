using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using music.Core.Abstractions;
using music.Core.Mvvm;
using music.Core.Services;
using music.Core.ViewModels.Items;
using music.Services;

namespace music.Core.ViewModels
{
    public sealed class SearchAllResultsParams
    {
        public string Type { get; set; } = "playlist";
        public string Keywords { get; set; } = string.Empty;
    }

    public sealed class RecommendRadarViewModel : PageViewModelBase
    {
        private readonly IRecommendationService _recommendationService;
        private readonly INavigationService _navigation;

        public RecommendRadarViewModel(
            IRecommendationService recommendationService,
            INavigationService navigation,
            IApiLog log)
        {
            _recommendationService = recommendationService;
            _navigation = navigation;
            Playlists = new ObservableCollection<PlaylistItem>();
            RetryCommand = new AsyncRelayCommand(LoadAsync, log: log);
        }

        public ObservableCollection<PlaylistItem> Playlists { get; }
        public AsyncRelayCommand RetryCommand { get; }

        public async Task LoadAsync()
        {
            var (version, token) = BeginLoad();
            ShowLoading();

            try
            {
                var result = await _recommendationService.GetRecommendResourceAsync();
                if (!IsCurrentLoad(version, token)) return;

                if (!result.Success)
                {
                    Playlists.Clear();
                    ShowError(result.Status.DisplayMessage);
                    return;
                }

                var playlists = result.Value;
                if (playlists.Count == 0)
                {
                    Playlists.Clear();
                    ShowError("暂无推荐歌单");
                    return;
                }

                Playlists.Clear();
                foreach (var p in playlists)
                {
                    Playlists.Add(new PlaylistItem
                    {
                        Id = p.Id,
                        Name = p.Name,
                        PicUrl = p.PicUrl,
                        PlayCount = p.PlayCount,
                        TrackCount = p.TrackCount,
                        PlayCountFormatted = p.PlayCountFormatted
                    });
                }

                ShowContent();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RecommendRadar] Load Error: {ex.Message}");
                if (IsCurrentLoad(version, token))
                    ShowError("加载失败，请稍后重试");
            }
        }

        public void OpenPlaylist(PlaylistItem? item)
        {
            if (item != null)
                _navigation.Navigate(PageKey.PlaylistDetail, item.Id);
        }
    }

    public sealed class AllPlaylistsViewModel : PageViewModelBase
    {
        private readonly IRecommendationService _recommendationService;
        private readonly INavigationService _navigation;

        public AllPlaylistsViewModel(
            IRecommendationService recommendationService,
            INavigationService navigation,
            IApiLog log)
        {
            _recommendationService = recommendationService;
            _navigation = navigation;
            Playlists = new ObservableCollection<PlaylistItem>();
            RetryCommand = new AsyncRelayCommand(LoadAsync, log: log);
        }

        public ObservableCollection<PlaylistItem> Playlists { get; }
        public AsyncRelayCommand RetryCommand { get; }

        public async Task LoadAsync()
        {
            var (version, token) = BeginLoad();
            ShowLoading();

            try
            {
                var result = await _recommendationService.GetRecommendedPlaylistsAsync(50);
                if (!IsCurrentLoad(version, token)) return;

                if (!result.Success)
                {
                    Playlists.Clear();
                    ShowError(result.Status.DisplayMessage);
                    return;
                }

                var playlists = result.Value;
                if (playlists.Count == 0)
                {
                    Playlists.Clear();
                    ShowError("暂无推荐歌单");
                    return;
                }

                Playlists.Clear();
                foreach (var p in playlists)
                {
                    Playlists.Add(new PlaylistItem
                    {
                        Id = p.Id,
                        Name = p.Name,
                        PicUrl = p.PicUrl,
                        PlayCount = p.PlayCount,
                        TrackCount = p.TrackCount,
                        PlayCountFormatted = p.PlayCountFormatted
                    });
                }

                ShowContent();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AllPlaylists] Load Error: {ex.Message}");
                if (IsCurrentLoad(version, token))
                    ShowError("加载失败，请稍后重试");
            }
        }

        public void OpenPlaylist(PlaylistItem? item)
        {
            if (item != null)
                _navigation.Navigate(PageKey.PlaylistDetail, item.Id);
        }
    }

    public sealed class SearchAllResultsViewModel : PageViewModelBase
    {
        private readonly ISearchService _searchService;
        private readonly INavigationService _navigation;
        private string _searchType = "playlist";
        private string _keywords = string.Empty;
        private string _title = "搜索结果";

        public SearchAllResultsViewModel(
            ISearchService searchService,
            INavigationService navigation,
            IApiLog log)
        {
            _searchService = searchService;
            _navigation = navigation;
            Items = new ObservableCollection<PlaylistItem>();
            RetryCommand = new AsyncRelayCommand(() => LoadAsync(_searchType, _keywords), log: log);
        }

        public ObservableCollection<PlaylistItem> Items { get; }
        public AsyncRelayCommand RetryCommand { get; }

        public string Title
        {
            get => _title;
            private set => SetProperty(ref _title, value);
        }

        public async Task LoadAsync(string searchType, string keywords)
        {
            _searchType = string.IsNullOrEmpty(searchType) ? "playlist" : searchType;
            _keywords = keywords ?? string.Empty;

            if (_searchType == "playlist")
                Title = $"歌单: {_keywords}";
            else if (_searchType == "album")
                Title = $"专辑: {_keywords}";

            var (version, token) = BeginLoad();
            ShowLoading();

            try
            {
                List<RecommendedPlaylist> results;
                ApiResult? failure = null;
                if (_searchType == "playlist")
                {
                    var res = await _searchService.SearchPlaylistsAsync(_keywords, 50);
                    if (!res.Success) failure = res.Status;
                    results = res.Value;
                }
                else
                {
                    var res = await _searchService.SearchAlbumsAsync(_keywords, 50);
                    if (!res.Success) failure = res.Status;
                    results = new List<RecommendedPlaylist>();
                    foreach (var album in res.Value)
                    {
                        results.Add(new RecommendedPlaylist
                        {
                            Id = album.Id,
                            Name = album.Name,
                            PicUrl = album.PicUrl,
                            TrackCount = album.Size
                        });
                    }
                }

                if (!IsCurrentLoad(version, token)) return;

                if (failure != null)
                {
                    Items.Clear();
                    ShowError(failure.DisplayMessage);
                    return;
                }

                if (results.Count == 0)
                {
                    Items.Clear();
                    ShowError("暂无搜索结果");
                    return;
                }

                Items.Clear();
                foreach (var item in results)
                {
                    Items.Add(new PlaylistItem
                    {
                        Id = item.Id,
                        Name = item.Name,
                        PicUrl = item.PicUrl,
                        PlayCount = item.PlayCount,
                        TrackCount = item.TrackCount,
                        PlayCountFormatted = item.PlayCountFormatted
                    });
                }

                ShowContent();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SearchAllResults] Load Error: {ex.Message}");
                if (IsCurrentLoad(version, token))
                    ShowError("加载失败，请稍后重试");
            }
        }

        public void OpenItem(PlaylistItem? item)
        {
            if (item == null) return;
            var param = _searchType == "album" ? $"album_{item.Id}" : item.Id;
            _navigation.Navigate(PageKey.PlaylistDetail, param);
        }
    }
}
