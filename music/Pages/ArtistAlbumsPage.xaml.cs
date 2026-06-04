using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using music.Services;

namespace music.Pages
{
    public sealed partial class ArtistAlbumsPage : Page
    {
        private readonly ObservableCollection<AlbumItem> _albums = new();
        private string _artistId = string.Empty;

        public ArtistAlbumsPage()
        {
            this.InitializeComponent();
            AlbumsGridView.ItemsSource = _albums;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is string artistId)
            {
                _artistId = artistId;
                await LoadArtistAlbumsAsync(artistId);
            }
        }

        private async System.Threading.Tasks.Task LoadArtistAlbumsAsync(string artistId)
        {
            LoadingPanel.Visibility = Visibility.Visible;
            AlbumsGridView.Visibility = Visibility.Collapsed;

            try
            {
                var json = await App.ApiService.GetAsync($"/artist/album?id={artistId}&limit=50");
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                _albums.Clear();
                if (result.TryGetProperty("hotAlbums", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        var album = new AlbumItem
                        {
                            Id = item.GetProperty("id").GetInt64().ToString(),
                            Name = item.GetProperty("name").GetString() ?? string.Empty,
                            PicUrl = item.TryGetProperty("picUrl", out var picUrl) ? picUrl.GetString() ?? string.Empty : string.Empty,
                            Size = item.TryGetProperty("size", out var size) ? size.GetInt32() : 0
                        };

                        _albums.Add(album);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ArtistAlbums] Error: {ex.Message}");
            }

            LoadingPanel.Visibility = Visibility.Collapsed;
            AlbumsGridView.Visibility = Visibility.Visible;
        }

        private void AlbumsGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is AlbumItem album)
            {
                var mainWindow = App.m_window as MainWindow;
                mainWindow?.MainContentFrame.Navigate(typeof(PlaylistDetailPage), $"album_{album.Id}");
            }
        }
    }
}
