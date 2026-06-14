using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using music.Services;

namespace music.Pages
{
    public sealed partial class ProfilePlaylistsPage : Page
    {
        private readonly ObservableCollection<PlaylistItem> _createdPlaylists = new();
        private readonly ObservableCollection<PlaylistItem> _collectedPlaylists = new();

        public ProfilePlaylistsPage()
        {
            this.InitializeComponent();
            CreatedGridView.ItemsSource = _createdPlaylists;
            CollectedGridView.ItemsSource = _collectedPlaylists;
            this.Loaded += async (s, e) => await LoadDataAsync();
        }

        private async System.Threading.Tasks.Task LoadDataAsync()
        {
            if (!App.ApiService.IsLoggedIn) return;

            LoadingPanel.Visibility = Visibility.Visible;
            ContentPanel.Visibility = Visibility.Collapsed;

            var allPlaylists = await App.ApiService.GetUserPlaylistsAsync(App.ApiService.UserId);

            var created = new List<PlaylistItem>();
            var collected = new List<PlaylistItem>();

            foreach (var p in allPlaylists)
            {
                var item = new PlaylistItem
                {
                    Id = p.Id,
                    Name = p.Name,
                    PicUrl = p.CoverImgUrl,
                    TrackCount = p.TrackCount,
                    PlayCount = p.PlayCount,
                    PlayCountFormatted = p.PlayCountFormatted
                };

                if (p.CreatorId == App.ApiService.UserId)
                    created.Add(item);
                else
                    collected.Add(item);
            }

            _createdPlaylists.Clear();
            foreach (var item in created)
                _createdPlaylists.Add(item);

            _collectedPlaylists.Clear();
            foreach (var item in collected)
                _collectedPlaylists.Add(item);

            LoadingPanel.Visibility = Visibility.Collapsed;

            CreatedSection.Visibility = _createdPlaylists.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            CollectedSection.Visibility = _collectedPlaylists.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            if (_createdPlaylists.Count == 0 && _collectedPlaylists.Count == 0)
                EmptyState.Visibility = Visibility.Visible;
            else
                ContentPanel.Visibility = Visibility.Visible;
        }

        private void GridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is PlaylistItem playlist)
            {
                var mainWindow = App.m_window as MainWindow;
                mainWindow?.MainContentFrame.Navigate(typeof(PlaylistDetailPage), playlist.Id);
            }
        }
    }
}
