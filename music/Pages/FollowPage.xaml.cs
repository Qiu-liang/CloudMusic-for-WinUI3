using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using music.Services;

namespace music.Pages
{
    public class FollowUserItem
    {
        public long UserId { get; set; }
        public string Nickname { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
        public bool Followed { get; set; }
    }

    public class BoolToFollowTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool followed)
            {
                return followed ? "已关注" : "关注";
            }
            return "关注";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public sealed partial class FollowPage : Page
    {
        private readonly ObservableCollection<FollowUserItem> _users = new();
        private bool _isFollowsMode = true;

        public FollowPage()
        {
            this.InitializeComponent();
            UsersListView.ItemsSource = _users;
            Loaded += FollowPage_Loaded;
        }

        private async void FollowPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadDataAsync();
        }

        private async System.Threading.Tasks.Task LoadDataAsync()
        {
            if (!App.ApiService.IsLoggedIn)
            {
                ShowError("请先登录");
                return;
            }

            ShowLoading();

            var userId = App.ApiService.UserId;
            List<FollowUser> users;

            if (_isFollowsMode)
            {
                users = await App.ApiService.GetFollowsAsync(userId);
            }
            else
            {
                users = await App.ApiService.GetFollowedsAsync(userId);
            }

            if (users == null || users.Count == 0)
            {
                ShowEmpty(_isFollowsMode ? "暂无关注" : "暂无粉丝");
                return;
            }

            _users.Clear();
            foreach (var user in users)
            {
                _users.Add(new FollowUserItem
                {
                    UserId = user.UserId,
                    Nickname = user.Nickname,
                    AvatarUrl = user.AvatarUrl,
                    Signature = user.Signature,
                    Followed = user.Followed
                });
            }

            ShowContent();
        }

        private void ShowLoading()
        {
            LoadingPanel.Visibility = Visibility.Visible;
            ErrorPanel.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Collapsed;
            UsersListView.Visibility = Visibility.Collapsed;
        }

        private void ShowError(string message)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
            EmptyState.Visibility = Visibility.Collapsed;
            UsersListView.Visibility = Visibility.Collapsed;
            ErrorText.Text = message;
        }

        private void ShowEmpty(string message)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            UsersListView.Visibility = Visibility.Collapsed;
            EmptyText.Text = message;
        }

        private void ShowContent()
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Collapsed;
            UsersListView.Visibility = Visibility.Visible;
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadDataAsync();
        }

        private async void FollowNavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItemContainer != null)
            {
                var tag = args.SelectedItemContainer.Tag.ToString();
                _isFollowsMode = tag == "follows";
                await LoadDataAsync();
            }
        }

        private async void FollowButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is long userId)
            {
                var user = _users.FirstOrDefault(u => u.UserId == userId);
                if (user == null) return;

                var success = await App.ApiService.FollowUserAsync(userId, !user.Followed);
                if (success)
                {
                    user.Followed = !user.Followed;
                    
                    // 刷新列表显示
                    var index = _users.IndexOf(user);
                    if (index >= 0)
                    {
                        _users[index] = user;
                    }
                }
            }
        }
    }
}