using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using music.Services;

namespace music.Pages
{
    public class RecentSongItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string AlbumName { get; set; } = string.Empty;
        public string DurationFormatted { get; set; } = string.Empty;
        public string PlayTimeFormatted { get; set; } = string.Empty;
        public string PlatformName { get; set; } = string.Empty;
        public bool IsVip { get; set; } = false;
        public Visibility VipVisibility => IsVip ? Visibility.Visible : Visibility.Collapsed;
    }

    public sealed partial class RecentPage : Page
    {
        private readonly ObservableCollection<RecentSongItem> _songs = new();
        private List<RecentSong> _songModels = new();

        public RecentPage()
        {
            this.InitializeComponent();
            SongsListView.ItemsSource = _songs;
            Loaded += RecentPage_Loaded;
        }

        private async void RecentPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadRecentSongsAsync();
        }

        private async System.Threading.Tasks.Task LoadRecentSongsAsync()
        {
            if (!App.ApiService.IsLoggedIn)
            {
                ShowError("请先登录");
                return;
            }

            ShowLoading();

            var songs = await App.ApiService.GetRecentSongsAsync(100);

            if (songs == null || songs.Count == 0)
            {
                ShowEmpty();
                return;
            }

            _songModels = songs;
            _songs.Clear();
            foreach (var song in songs)
            {
                _songs.Add(new RecentSongItem
                {
                    Id = song.Id,
                    Name = song.Name,
                    Artist = song.Artist,
                    AlbumName = song.AlbumName,
                    DurationFormatted = song.DurationFormatted,
                    PlayTimeFormatted = song.PlayTimeFormatted,
                    PlatformName = song.PlatformName,
                    IsVip = song.IsVip
                });
            }

            CountText.Text = $"共 {_songs.Count} 首";
            ShowContent();
        }

        private void ShowLoading()
        {
            LoadingPanel.Visibility = Visibility.Visible;
            EmptyState.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;
            SongsListView.Visibility = Visibility.Collapsed;
        }

        private void ShowEmpty()
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            ErrorPanel.Visibility = Visibility.Collapsed;
            SongsListView.Visibility = Visibility.Collapsed;
            CountText.Text = "";
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

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadRecentSongsAsync();
        }

        private async void SongsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is RecentSongItem songItem)
            {
                var index = _songs.IndexOf(songItem);
                if (index >= 0 && index < _songModels.Count)
                {
                    var song = _songModels[index];
                    var songModel = new Models.Song
                    {
                        Id = song.Id,
                        Name = song.Name,
                        Artists = new List<Models.Artist> { new Models.Artist { Name = song.Artist } },
                        Album = new Models.Album { Name = song.AlbumName },
                        Fee = song.Fee,
                        IsVip = song.IsVip
                    };
                    await MainWindow.PlaybackService.PlayAsync(songModel);
                }
            }
        }
    }
}