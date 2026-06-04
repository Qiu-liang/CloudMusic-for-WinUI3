using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System.Text.Json;
using music.Services;

namespace music.Pages
{
    public sealed partial class ArtistInfoPage : Page
    {
        private string _artistId = string.Empty;

        public ArtistInfoPage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is string artistId)
            {
                _artistId = artistId;
                await LoadArtistInfoAsync(artistId);
            }
        }

        private async System.Threading.Tasks.Task LoadArtistInfoAsync(string artistId)
        {
            LoadingPanel.Visibility = Visibility.Visible;
            ContentPanel.Visibility = Visibility.Collapsed;

            try
            {
                // 获取歌手详情
                var detailJson = await App.ApiService.GetAsync($"/artist/detail?id={artistId}");
                var detailResult = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(detailJson);

                if (detailResult.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("artist", out var artist))
                {
                    var name = artist.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty;
                    var picUrl = artist.TryGetProperty("picUrl", out var picUrlEl) ? picUrlEl.GetString() ?? string.Empty : string.Empty;
                    var alias = artist.TryGetProperty("alias", out var aliasEl) ? aliasEl : default;
                    var desc = artist.TryGetProperty("briefDesc", out var descEl) ? descEl.GetString() ?? string.Empty : string.Empty;

                    ArtistNameText.Text = name;

                    if (!string.IsNullOrEmpty(picUrl))
                    {
                        ArtistImage.Source = new BitmapImage(new Uri(picUrl));
                        ArtistImage.Visibility = Visibility.Visible;
                    }

                    // 显示别名
                    if (alias.ValueKind == System.Text.Json.JsonValueKind.Array && alias.GetArrayLength() > 0)
                    {
                        var aliasText = string.Join("、", alias.EnumerateArray().Select(a => a.GetString()));
                        AliasText.Text = aliasText;
                        AliasText.Visibility = Visibility.Visible;
                    }

                    // 显示简介
                    if (!string.IsNullOrEmpty(desc))
                    {
                        DescriptionText.Text = desc;
                        DescriptionSection.Visibility = Visibility.Visible;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ArtistInfo] Error: {ex.Message}");
            }

            LoadingPanel.Visibility = Visibility.Collapsed;
            ContentPanel.Visibility = Visibility.Visible;
        }
    }
}
