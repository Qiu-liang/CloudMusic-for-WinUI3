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
                // 先尝试歌手接口
                var detailJson = await App.ApiService.GetAsync($"/artist/detail?id={artistId}");
                var detailResult = JsonSerializer.Deserialize<JsonElement>(detailJson);

                if (detailResult.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("artist", out var artist) &&
                    artist.TryGetProperty("name", out var nameEl) &&
                    !string.IsNullOrEmpty(nameEl.GetString()))
                {
                    // 是歌手，显示歌手信息
                    var name = nameEl.GetString() ?? string.Empty;
                    var cover = artist.TryGetProperty("cover", out var coverEl) ? coverEl.GetString() ?? string.Empty : string.Empty;
                    var avatar = artist.TryGetProperty("avatar", out var avatarEl) ? avatarEl.GetString() ?? string.Empty : string.Empty;
                    var alias = artist.TryGetProperty("alias", out var aliasEl) ? aliasEl : default;
                    var desc = artist.TryGetProperty("briefDesc", out var descEl) ? descEl.GetString() ?? string.Empty : string.Empty;

                    var picUrl = !string.IsNullOrEmpty(cover) ? cover : avatar;

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
                    ArtistNavView.Visibility = Visibility.Visible;
                    PlayAllButton.Visibility = Visibility.Visible;
                    LoadingPanel.Visibility = Visibility.Collapsed;
                    return;
                }

                // 不是歌手，尝试用户接口
                if (long.TryParse(artistId, out var uid))
                {
                    var userInfo = await App.ApiService.GetUserDetailAsync(uid);
                    if (userInfo != null)
                    {
                        TitleText.Text = "用户详情";
                        ArtistNameText.Text = userInfo.Nickname;

                        if (!string.IsNullOrEmpty(userInfo.AvatarUrl))
                        {
                            ArtistImage.Source = new BitmapImage(new Uri(userInfo.AvatarUrl));
                            ArtistImage.Visibility = Visibility.Visible;
                        }

                        var stats = $"关注 {userInfo.FollowCount}  |  粉丝 {userInfo.FollowedCount}";
                        ArtistAliasText.Text = stats;
                        ArtistAliasText.Visibility = Visibility.Visible;

                        if (!string.IsNullOrEmpty(userInfo.Signature))
                        {
                            ArtistDescriptionText.Text = userInfo.Signature;
                            ArtistDescriptionText.Visibility = Visibility.Visible;
                        }

                        ArtistInfoSection.Visibility = Visibility.Visible;
                        // 用户不是歌手，隐藏歌手相关功能
                        ArtistNavView.Visibility = Visibility.Collapsed;
                        PlayAllButton.Visibility = Visibility.Collapsed;
                        LoadingPanel.Visibility = Visibility.Collapsed;
                        return;
                    }
                }

                // 都失败了
                TitleText.Text = "未找到";
                ArtistNameText.Text = "无法加载信息";
                ArtistInfoSection.Visibility = Visibility.Visible;
                ArtistNavView.Visibility = Visibility.Collapsed;
                PlayAllButton.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ArtistDetail] LoadArtistInfo Error: {ex.Message}");
                TitleText.Text = "加载失败";
                ArtistNameText.Text = ex.Message;
                ArtistInfoSection.Visibility = Visibility.Visible;
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
