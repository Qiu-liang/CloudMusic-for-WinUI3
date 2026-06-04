using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using music.Models;
using music.Services;

namespace music.Pages
{
    public sealed partial class ArtistTopSongsPage : Page
    {
        private readonly ObservableCollection<SongItem> _songs = new();
        private List<Song> _songModels = new();
        private string _artistId = string.Empty;

        public ArtistTopSongsPage()
        {
            this.InitializeComponent();
            SongsListView.ItemsSource = _songs;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is string artistId)
            {
                _artistId = artistId;
                await LoadArtistSongsAsync(artistId);
            }
        }

        private async System.Threading.Tasks.Task LoadArtistSongsAsync(string artistId)
        {
            LoadingPanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            SongsListView.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;

            var songs = await App.ApiService.GetArtistSongsAsync(artistId, 50);

            if (songs == null || songs.Count == 0)
            {
                LoadingPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
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
                    Fee = song.Fee,
                    IsVip = song.IsVip,
                    IsPaid = song.IsPaid,
                    FeeText = song.FeeText
                });
            }

            LoadingPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            SongsListView.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
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

        public List<Song> GetSongs() => _songModels;
    }
}
