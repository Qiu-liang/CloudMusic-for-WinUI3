using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using music.Models;

namespace music.Pages
{
    public sealed partial class PlaylistDetailPage : Page
    {
        private readonly ObservableCollection<SongItem> _songs = new();
        private List<Song> _songModels = new();
        private string _playlistId = string.Empty;

        public PlaylistDetailPage()
        {
            this.InitializeComponent();
            SongsListView.ItemsSource = _songs;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is string playlistId)
            {
                _playlistId = playlistId;
                await LoadPlaylistAsync(playlistId);
            }
        }

        private async System.Threading.Tasks.Task LoadPlaylistAsync(string playlistId)
        {
            ShowLoading();

            var songs = await App.ApiService.GetPlaylistDetailAsync(playlistId);

            if (songs == null || songs.Count == 0)
            {
                ShowError("歌单为空或加载失败");
                return;
            }

            _songModels = songs;
            _songs.Clear();
            for (int i = 0; i < songs.Count; i++)
            {
                var song = songs[i];
                _songs.Add(new SongItem
                {
                    Id = song.Id,
                    Name = song.Name,
                    ArtistNames = song.ArtistNames,
                    AlbumName = song.Album.Name,
                    CoverUrl = song.CoverImgUrl,
                    DurationFormatted = song.DurationFormatted,
                    Index = i + 1,
                    IsLiked = song.Liked,
                    CanPlay = song.CanPlay,
                    Fee = song.Fee,
                    IsVip = song.IsVip,
                    IsPaid = song.IsPaid,
                    FeeText = song.FeeText
                });
            }

            TitleText.Text = $"歌单详情";
            SongsCountText.Text = $"共 {_songs.Count} 首";
            PlayAllButton.Visibility = Visibility.Visible;
            ShowContent();
        }

        private void ShowLoading()
        {
            LoadingPanel.Visibility = Visibility.Visible;
            ErrorPanel.Visibility = Visibility.Collapsed;
            SongsListView.Visibility = Visibility.Collapsed;
        }

        private void ShowError(string message)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
            SongsListView.Visibility = Visibility.Collapsed;
            ErrorText.Text = message;
        }

        private void ShowContent()
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;
            SongsListView.Visibility = Visibility.Visible;
        }

        private async void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadPlaylistAsync(_playlistId);
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

        private async void PlayAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_songModels.Count > 0)
            {
                await MainWindow.PlaybackService.PlayAsync(_songModels, 0);
            }
        }
    }
}