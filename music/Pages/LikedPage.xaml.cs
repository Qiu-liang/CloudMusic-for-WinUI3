using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using music.Models;

namespace music.Pages
{
    public sealed partial class LikedPage : Page
    {
        private readonly ObservableCollection<SongItem> _songs = new();
        private List<Song> _songModels = new();

        public LikedPage()
        {
            this.InitializeComponent();
            SongsListView.ItemsSource = _songs;
            Loaded += LikedPage_Loaded;
        }

        private async void LikedPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadLikedSongsAsync();
        }

        private async System.Threading.Tasks.Task LoadLikedSongsAsync()
        {
            if (!App.ApiService.IsLoggedIn)
            {
                ShowError("请先登录");
                return;
            }

            ShowLoading();

            var songs = await App.ApiService.GetLikedSongsAsync();

            if (songs == null || songs.Count == 0)
            {
                ShowEmptyState();
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
                    IsLiked = true,
                    CanPlay = song.CanPlay,
                    Fee = song.Fee,
                    IsVip = song.IsVip,
                    IsPaid = song.IsPaid,
                    FeeText = song.FeeText
                });
            }

            SongsCountText.Text = $"共 {_songs.Count} 首";
            ShowContent();
        }

        private void ShowLoading()
        {
            LoadingPanel.Visibility = Visibility.Visible;
            EmptyState.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;
            SongsListView.Visibility = Visibility.Collapsed;
        }

        private void ShowEmptyState()
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            ErrorPanel.Visibility = Visibility.Collapsed;
            SongsListView.Visibility = Visibility.Collapsed;
        }

        private void ShowError(string message)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
            SongsListView.Visibility = Visibility.Collapsed;
            ErrorText.Text = message;
        }

        private void ShowContent()
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;
            SongsListView.Visibility = Visibility.Visible;
        }

        private async void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadLikedSongsAsync();
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