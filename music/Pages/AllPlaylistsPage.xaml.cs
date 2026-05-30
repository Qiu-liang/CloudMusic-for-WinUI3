using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using music.Services;

namespace music.Pages
{
    public sealed partial class AllPlaylistsPage : Page
    {
        private readonly ObservableCollection<PlaylistItem> _playlists = new();
        private List<RecommendedPlaylist> _playlistModels = new();

        public AllPlaylistsPage()
        {
            InitializeComponent();
            PlaylistsGridView.ItemsSource = _playlists;
            Loaded += AllPlaylistsPage_Loaded;
        }

        private async void AllPlaylistsPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadPlaylistsAsync();
        }

        private async System.Threading.Tasks.Task LoadPlaylistsAsync()
        {
            ShowLoading();

            try
            {
                var playlists = await App.ApiService.GetRecommendedPlaylistsAsync(50);

                if (playlists == null || playlists.Count == 0)
                {
                    ShowError("暂无推荐歌单");
                    return;
                }

                _playlistModels = playlists;
                _playlists.Clear();
                foreach (var playlist in playlists)
                {
                    _playlists.Add(new PlaylistItem
                    {
                        Id = playlist.Id,
                        Name = playlist.Name,
                        PicUrl = playlist.PicUrl,
                        PlayCount = playlist.PlayCount,
                        TrackCount = playlist.TrackCount,
                        PlayCountFormatted = playlist.PlayCountFormatted
                    });
                }

                ShowContent();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AllPlaylists] Load Error: {ex.Message}");
                ShowError("加载失败，请稍后重试");
            }
        }

        private void ShowLoading()
        {
            LoadingPanel.Visibility = Visibility.Visible;
            ErrorPanel.Visibility = Visibility.Collapsed;
            PlaylistsGridView.Visibility = Visibility.Collapsed;
        }

        private void ShowError(string message)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
            PlaylistsGridView.Visibility = Visibility.Collapsed;
            ErrorText.Text = message;
        }

        private void ShowContent()
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;
            PlaylistsGridView.Visibility = Visibility.Visible;
        }

        private async void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadPlaylistsAsync();
        }

        private void PlaylistsGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is PlaylistItem playlist)
            {
                var mainWindow = App.m_window as MainWindow;
                if (mainWindow != null)
                {
                    mainWindow.MainContentFrame.Navigate(typeof(PlaylistDetailPage), playlist.Id);
                }
            }
        }
    }
}