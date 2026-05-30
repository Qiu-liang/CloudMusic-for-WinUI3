using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using music.Services;

namespace music.Pages
{
    public sealed partial class SearchAllResultsPage : Page
    {
        private readonly ObservableCollection<PlaylistItem> _items = new();
        private List<RecommendedPlaylist> _itemModels = new();
        private string _searchType = "playlist";
        private string _keywords = string.Empty;

        public SearchAllResultsPage()
        {
            InitializeComponent();
            ResultsGridView.ItemsSource = _items;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is SearchAllResultsParams param)
            {
                _searchType = param.Type;
                _keywords = param.Keywords;

                if (_searchType == "playlist")
                {
                    TitleText.Text = $"歌单: {_keywords}";
                }
                else if (_searchType == "album")
                {
                    TitleText.Text = $"专辑: {_keywords}";
                }

                await LoadResultsAsync();
            }
        }

        private async System.Threading.Tasks.Task LoadResultsAsync()
        {
            ShowLoading();

            try
            {
                List<RecommendedPlaylist> results;

                if (_searchType == "playlist")
                {
                    results = await App.ApiService.SearchPlaylistsAsync(_keywords, 50);
                }
                else
                {
                    var albums = await App.ApiService.SearchAlbumsAsync(_keywords, 50);
                    results = new List<RecommendedPlaylist>();
                    foreach (var album in albums)
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

                if (results == null || results.Count == 0)
                {
                    ShowError("暂无搜索结果");
                    return;
                }

                _itemModels = results;
                _items.Clear();
                foreach (var item in results)
                {
                    _items.Add(new PlaylistItem
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
                ShowError("加载失败，请稍后重试");
            }
        }

        private void ShowLoading()
        {
            LoadingPanel.Visibility = Visibility.Visible;
            ErrorPanel.Visibility = Visibility.Collapsed;
            ResultsGridView.Visibility = Visibility.Collapsed;
        }

        private void ShowError(string message)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
            ResultsGridView.Visibility = Visibility.Collapsed;
            ErrorText.Text = message;
        }

        private void ShowContent()
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;
            ResultsGridView.Visibility = Visibility.Visible;
        }

        private async void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadResultsAsync();
        }

        private void ResultsGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is PlaylistItem item)
            {
                var mainWindow = App.m_window as MainWindow;
                if (mainWindow != null)
                {
                    if (_searchType == "album")
                    {
                        mainWindow.MainContentFrame.Navigate(typeof(PlaylistDetailPage), $"album_{item.Id}");
                    }
                    else
                    {
                        mainWindow.MainContentFrame.Navigate(typeof(PlaylistDetailPage), item.Id);
                    }
                }
            }
        }
    }

    public class SearchAllResultsParams
    {
        public string Type { get; set; } = "playlist";
        public string Keywords { get; set; } = string.Empty;
    }
}
