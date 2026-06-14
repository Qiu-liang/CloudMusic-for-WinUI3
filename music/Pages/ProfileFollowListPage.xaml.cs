using System;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using music.Services;

namespace music.Pages
{
    public class FollowUserItem
    {
        public long UserId { get; set; }
        public string Nickname { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
    }

    public sealed partial class ProfileFollowListPage : Page
    {
        private readonly ObservableCollection<FollowUserItem> _users = new();
        private bool _isFollowsMode = true;

        public ProfileFollowListPage()
        {
            this.InitializeComponent();
            UserListView.ItemsSource = _users;
            this.Loaded += ProfileFollowListPage_Loaded;
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is string mode)
            {
                _isFollowsMode = mode == "follows";
            }
        }

        private async void ProfileFollowListPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (!App.ApiService.IsLoggedIn)
            {
                ShowEmpty("请先登录");
                return;
            }

            ShowLoading();

            var users = _isFollowsMode
                ? await App.ApiService.GetFollowsAsync(App.ApiService.UserId)
                : await App.ApiService.GetFollowedsAsync(App.ApiService.UserId);

            _users.Clear();
            foreach (var u in users)
            {
                _users.Add(new FollowUserItem
                {
                    UserId = u.UserId,
                    Nickname = u.Nickname,
                    AvatarUrl = u.AvatarUrl,
                    Signature = u.Signature
                });
            }

            if (_users.Count == 0)
                ShowEmpty(_isFollowsMode ? "暂无关注" : "暂无粉丝");
            else
                ShowContent();
        }

        private void ShowLoading()
        {
            LoadingPanel.Visibility = Visibility.Visible;
            EmptyState.Visibility = Visibility.Collapsed;
            UserListView.Visibility = Visibility.Collapsed;
        }

        private void ShowEmpty(string msg)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            UserListView.Visibility = Visibility.Collapsed;
            EmptyText.Text = msg;
        }

        private void ShowContent()
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Collapsed;
            UserListView.Visibility = Visibility.Visible;
        }

        private void User_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is Grid grid && grid.Tag is long userId)
            {
                var mainWindow = App.m_window as MainWindow;
                mainWindow?.MainContentFrame.Navigate(typeof(ArtistDetailPage), userId.ToString());
            }
        }
    }
}
