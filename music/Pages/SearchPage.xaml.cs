using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using music.Models;
using music.Services;

namespace music.Pages
{
    public sealed partial class SearchPage : Page
    {
        private readonly ObservableCollection<SongItem> _songs = new();
        private List<Song> _songModels = new();
        private string _currentQuery = string.Empty;

        public SearchPage()
        {
            this.InitializeComponent();
            SongsListView.ItemsSource = _songs;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is string query && !string.IsNullOrWhiteSpace(query))
            {
                _currentQuery = query;
                TitleText.Text = $"搜索: {query}";
                await SearchSongsAsync(query);
            }
            else
            {
                ShowEmptyState();
            }
        }

        private async System.Threading.Tasks.Task SearchSongsAsync(string keywords)
        {
            ShowLoading();

            var songs = await App.ApiService.SearchSongsAsync(keywords, 50);

            if (songs == null || songs.Count == 0)
            {
                ShowEmptyState();
                TitleText.Text = $"未找到 \"{keywords}\" 的结果";
                return;
            }

            _songModels = songs;
            _songs.Clear();
            foreach (var song in songs)
            {
                _songs.Add(new SongItem
                {
                    Id = song.Id,
                    Name = song.Name,
                    ArtistNames = song.ArtistNames,
                    AlbumName = song.Album.Name,
                    CoverUrl = song.CoverImgUrl,
                    DurationFormatted = song.DurationFormatted,
                    IsLiked = song.Liked,
                    CanPlay = song.CanPlay,
                    Fee = song.Fee,
                    IsVip = song.IsVip,
                    IsPaid = song.IsPaid,
                    FeeText = song.FeeText
                });
            }

            ResultsCountText.Text = $"找到 {_songs.Count} 首歌曲";
            ShowResults();
        }

        private void ShowEmptyState()
        {
            EmptyState.Visibility = Visibility.Visible;
            LoadingPanel.Visibility = Visibility.Collapsed;
            ResultsPanel.Visibility = Visibility.Collapsed;
        }

        private void ShowLoading()
        {
            EmptyState.Visibility = Visibility.Collapsed;
            LoadingPanel.Visibility = Visibility.Visible;
            ResultsPanel.Visibility = Visibility.Collapsed;
        }

        private void ShowResults()
        {
            EmptyState.Visibility = Visibility.Collapsed;
            LoadingPanel.Visibility = Visibility.Collapsed;
            ResultsPanel.Visibility = Visibility.Visible;
        }

        private async void SongsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is SongItem songItem)
            {
                var index = _songs.IndexOf(songItem);
                if (index >= 0 && index < _songModels.Count)
                {
                    await MainWindow.PlaybackService.PlayAsync(_songModels, index);
                }
            }
        }
    }
}