using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Text.Json;
using music.Services;

namespace music.Pages
{
    public sealed partial class ArtistDetailPage : Page
    {
        private string _artistId = string.Empty;

        public ArtistDetailPage()
        {
            this.InitializeComponent();
            this.Loaded += ArtistDetailPage_Loaded;
        }

        private void ArtistDetailPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (ContentFrame.Content == null && !string.IsNullOrEmpty(_artistId))
            {
                ContentFrame.Navigate(typeof(ArtistTopSongsPage), _artistId);
            }
        }

        protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is string artistId)
            {
                _artistId = artistId;
                await LoadArtistInfoAsync(artistId);

                if (ContentFrame.Content == null)
                {
                    ContentFrame.Navigate(typeof(ArtistTopSongsPage), _artistId);
                }
            }
        }

        private async System.Threading.Tasks.Task LoadArtistInfoAsync(string artistId)
        {
            LoadingPanel.Visibility = Visibility.Visible;
            ArtistInfoSection.Visibility = Visibility.Collapsed;

            try
            {
                var detailJson = await App.ApiService.GetAsync($"/artist/detail?id={artistId}");
                System.Diagnostics.Debug.WriteLine($"[ArtistDetail] API Response: {detailJson}");
                var detailResult = JsonSerializer.Deserialize<JsonElement>(detailJson);

                if (detailResult.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("artist", out var artist))
                {
                    var name = artist.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty;
                    var cover = artist.TryGetProperty("cover", out var coverEl) ? coverEl.GetString() ?? string.Empty : string.Empty;
                    var avatar = artist.TryGetProperty("avatar", out var avatarEl) ? avatarEl.GetString() ?? string.Empty : string.Empty;
                    var alias = artist.TryGetProperty("alias", out var aliasEl) ? aliasEl : default;
                    var desc = artist.TryGetProperty("briefDesc", out var descEl) ? descEl.GetString() ?? string.Empty : string.Empty;

                    // 优先使用 cover，如果为空则使用 avatar
                    var picUrl = !string.IsNullOrEmpty(cover) ? cover : avatar;

                    System.Diagnostics.Debug.WriteLine($"[ArtistDetail] Name: {name}");
                    System.Diagnostics.Debug.WriteLine($"[ArtistDetail] Cover: {cover}");
                    System.Diagnostics.Debug.WriteLine($"[ArtistDetail] Avatar: {avatar}");

                    TitleText.Text = "歌手详情";
                    ArtistNameText.Text = name;

                    if (!string.IsNullOrEmpty(picUrl))
                    {
                        ArtistImage.Source = new BitmapImage(new Uri(picUrl));
                        ArtistImage.Visibility = Visibility.Visible;
                    }

                    if (alias.ValueKind == JsonValueKind.Array && alias.GetArrayLength() > 0)
                    {
                        var aliasText = string.Join("、", alias.EnumerateArray().Select(a => a.GetString()));
                        ArtistAliasText.Text = aliasText;
                        ArtistAliasText.Visibility = Visibility.Visible;
                    }

                    if (!string.IsNullOrEmpty(desc))
                    {
                        ArtistDescriptionText.Text = desc;
                        ArtistDescriptionText.Visibility = Visibility.Visible;
                    }

                    ArtistInfoSection.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ArtistDetail] LoadArtistInfo Error: {ex.Message}");
            }

            LoadingPanel.Visibility = Visibility.Collapsed;
        }

        private void ArtistNavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItemContainer != null && !string.IsNullOrEmpty(_artistId))
            {
                var tag = args.SelectedItemContainer.Tag.ToString();
                switch (tag)
                {
                    case "songs":
                        ContentFrame.Navigate(typeof(ArtistTopSongsPage), _artistId);
                        PlayAllButton.Visibility = Visibility.Visible;
                        break;
                    case "albums":
                        ContentFrame.Navigate(typeof(ArtistAlbumsPage), _artistId);
                        PlayAllButton.Visibility = Visibility.Collapsed;
                        break;
                }
            }
        }

        private async void PlayAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (ContentFrame.Content is ArtistTopSongsPage songsPage)
            {
                var songs = songsPage.GetSongs();
                if (songs != null && songs.Count > 0)
                {
                    await MainWindow.PlaybackService.PlayAsync(songs, 0);
                }
            }
        }
    }
}
