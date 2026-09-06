using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using music.Services;

namespace music.Pages
{
    public sealed partial class ProfilePage : Page
    {
        public ProfilePage()
        {
            this.InitializeComponent();
            this.Loaded += ProfilePage_Loaded;
        }

        private void ProfilePage_Loaded(object sender, RoutedEventArgs e)
        {
            if (ContentFrame.Content == null)
            {
                ContentFrame.Navigate(typeof(ProfilePlaylistsPage));
            }

            if (App.ApiService.IsLoggedIn)
            {
                _ = LoadUserInfoAsync();
            }
            else
            {
                NicknameText.Text = "未登录";
                StatsText.Text = "请先登录";
                StatsText.Visibility = Visibility.Visible;
                UserInfoSection.Visibility = Visibility.Visible;
                LoadingPanel.Visibility = Visibility.Collapsed;
            }
        }

        private async System.Threading.Tasks.Task LoadUserInfoAsync()
        {
            try
            {
                var userInfo = await App.ApiService.GetUserDetailAsync(App.ApiService.UserId);
                if (userInfo != null)
                {
                    NicknameText.Text = userInfo.Nickname;

                    if (!string.IsNullOrEmpty(userInfo.AvatarUrl))
                    {
                        AvatarImage.Source = new BitmapImage(new Uri(userInfo.AvatarUrl));
                        AvatarImage.Visibility = Visibility.Visible;
                    }

                    var stats = $"关注 {userInfo.FollowCount}  |  粉丝 {userInfo.FollowedCount}  |  动态 {userInfo.EventCount}";
                    StatsText.Text = stats;
                    StatsText.Visibility = Visibility.Visible;

                    if (!string.IsNullOrEmpty(userInfo.Signature))
                    {
                        SignatureText.Text = userInfo.Signature;
                        SignatureText.Visibility = Visibility.Visible;
                    }
                }

                var isVip = await App.ApiService.CheckVipStatusAsync();
                if (isVip)
                {
                    VipBadge.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Profile] LoadError: {ex.Message}");
            }

            // 拉取不到用户信息时给出提示并提供退出登录的出口，避免个人中心一片空白
            if (string.IsNullOrEmpty(NicknameText.Text))
            {
                NicknameText.Text = "无法加载用户信息";
                StatsText.Text = "登录可能已失效，可退出登录后重新扫码";
                StatsText.Visibility = Visibility.Visible;
                SignatureText.Visibility = Visibility.Collapsed;
                ForceLogoutButton.Visibility = Visibility.Visible;
            }

            UserInfoSection.Visibility = Visibility.Visible;
            LoadingPanel.Visibility = Visibility.Collapsed;
        }

        private void ForceLogoutButton_Click(object sender, RoutedEventArgs e)
        {
            if (App.m_window is MainWindow mainWindow)
            {
                mainWindow.PerformLogout();
            }
        }

        private void ProfileNavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItemContainer != null)
            {
                var tag = args.SelectedItemContainer.Tag.ToString();
                switch (tag)
                {
                    case "playlists":
                        ContentFrame.Navigate(typeof(ProfilePlaylistsPage));
                        break;
                    case "recent":
                        ContentFrame.Navigate(typeof(RecentPage));
                        break;
                    case "follows":
                        ContentFrame.Navigate(typeof(ProfileFollowListPage), "follows");
                        break;
                    case "followeds":
                        ContentFrame.Navigate(typeof(ProfileFollowListPage), "followeds");
                        break;
                }
            }
        }
    }
}
