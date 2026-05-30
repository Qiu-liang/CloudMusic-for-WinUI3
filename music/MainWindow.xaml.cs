using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using music.Services;
using music.Models;
using music.Dialogs;

namespace music
{
    public sealed partial class MainWindow : Window
    {
        public static PlaybackService PlaybackService { get; private set; } = null!;
        public Frame MainContentFrame => ContentFrame;
        private bool _isProgressDragging = false;

        public MainWindow()
        {
            InitializeComponent();
            NavView.SelectedItem = NavView.MenuItems[0];

            // 设置窗口图标
            SetWindowIcon();

            PlaybackService = new PlaybackService();
            SetupPlaybackEvents();
            SetupLoginEvents();
            UpdateLoginStatus();
            LoadQualitySetting();

            // 监听Frame导航事件，更新返回按钮
            ContentFrame.Navigated += ContentFrame_Navigated;
        }

        private void LoadQualitySetting()
        {
            var settings = ApplicationData.Current.LocalSettings;
            var quality = settings.Values["AudioQuality"]?.ToString() ?? "standard";
            
            var qualityName = quality switch
            {
                "standard" => "标准",
                "higher" => "较高",
                "exhigh" => "极高",
                "lossless" => "无损",
                "hires" => "Hi-Res",
                "jyeffect" => "高清环绕声",
                "sky" => "沉浸环绕声",
                "dolby" => "杜比全景声",
                "jymaster" => "超清母带",
                _ => "标准"
            };
            
            QualityText.Text = qualityName;
        }

        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            UpdateBackButtonVisibility();
        }

        private void UpdateBackButtonVisibility()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                // 在歌单详情、全部歌单、搜索页面、搜索结果页面显示返回按钮（歌词页面不显示）
                var currentPage = ContentFrame.CurrentSourcePageType;
                var showBack = currentPage == typeof(Pages.PlaylistDetailPage) || 
                               currentPage == typeof(Pages.AllPlaylistsPage) ||
                               currentPage == typeof(Pages.SearchPage) ||
                               currentPage == typeof(Pages.SearchAllResultsPage) ||
                               currentPage == typeof(Pages.SearchAllSongsPage);
                
                if (showBack)
                {
                    BackBar.Visibility = Visibility.Visible;
                    BackBar.Opacity = 1;
                    BackBar.Translation = new System.Numerics.Vector3(0, 0, 0);
                }
                else
                {
                    BackBar.Opacity = 0;
                    BackBar.Translation = new System.Numerics.Vector3(0, -48, 0);
                    BackBar.Visibility = Visibility.Collapsed;
                }
            });
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (ContentFrame.CanGoBack)
            {
                ContentFrame.GoBack();
            }
        }

        private void SetWindowIcon()
        {
            try
            {
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                
                var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    appWindow.SetIcon(iconPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Set icon error: {ex.Message}");
            }
        }

        private void AlbumCover_Tapped(object sender, TappedRoutedEventArgs e)
        {
            // 如果当前在歌词页面，则关闭歌词页面
            if (ContentFrame.CurrentSourcePageType == typeof(Pages.LyricsPage))
            {
                if (ContentFrame.CanGoBack)
                {
                    ContentFrame.GoBack();
                }
                return;
            }

            // 否则打开歌词页面
            var currentSong = PlaybackService.CurrentSong;
            if (currentSong != null)
            {
                ContentFrame.Navigate(typeof(Pages.LyricsPage), currentSong);
            }
        }

        private void AlbumCover_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            AlbumCover.Opacity = 0.8;
        }

        private void AlbumCover_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            AlbumCover.Opacity = 1.0;
        }

        private void SetupLoginEvents()
        {
            LoginItem.Tapped += async (s, e) =>
            {
                if (App.ApiService.IsLoggedIn)
                {
                    return;
                }

                var dialog = new LoginDialog();
                dialog.XamlRoot = ContentFrame.XamlRoot;
                var result = await dialog.ShowAsync();

                // 登录成功后加载用户信息和歌单
                if (App.ApiService.IsLoggedIn)
                {
                    UpdateLoginStatus();
                    await LoadUserInfoAsync();
                    await LoadPlaylistsAsync();
                }
            };
        }

        private void UpdateLoginStatus()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                var cachedUser = App.ApiService.GetCachedUserInfo();
                if (cachedUser != null && App.ApiService.IsLoggedIn)
                {
                    LoginStatusText.Text = cachedUser.Nickname;
                    LoginSubText.Text = "已登录";
                    LoginIcon.Symbol = Symbol.Contact;
                    LogoutButton.Visibility = Visibility.Visible;
                    
                    // 检查VIP状态
                    var isVip = App.ApiService.GetVipStatus();
                    VipBadge.Visibility = isVip ? Visibility.Visible : Visibility.Collapsed;
                }
                else if (App.ApiService.IsLoggedIn)
                {
                    LoginStatusText.Text = "已登录";
                    LoginSubText.Text = $"ID: {App.ApiService.UserId}";
                    LoginIcon.Symbol = Symbol.Contact;
                    LogoutButton.Visibility = Visibility.Visible;
                    VipBadge.Visibility = Visibility.Collapsed;
                }
                else
                {
                    LoginStatusText.Text = "未登录";
                    LoginSubText.Text = "点击登录";
                    LoginIcon.Symbol = Symbol.Contact;
                    LogoutButton.Visibility = Visibility.Collapsed;
                    VipBadge.Visibility = Visibility.Collapsed;
                }
            });
        }

        private async System.Threading.Tasks.Task LoadUserInfoAsync()
        {
            var userInfo = await App.ApiService.GetUserInfoAsync();
            if (userInfo != null)
            {
                // 获取VIP状态
                var isVip = await App.ApiService.CheckVipStatusAsync();
                
                DispatcherQueue.TryEnqueue(() =>
                {
                    LoginStatusText.Text = userInfo.Nickname;
                    LoginSubText.Text = "已登录";
                    LoginIcon.Symbol = Symbol.Contact;
                    LogoutButton.Visibility = Visibility.Visible;
                    VipBadge.Visibility = isVip ? Visibility.Visible : Visibility.Collapsed;
                });

                // 登录成功后加载歌单
                await LoadPlaylistsAsync();
            }
        }

        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            App.ApiService.ResetLogin();
            UpdateLoginStatus();
            
            // 清除歌单项
            DispatcherQueue.TryEnqueue(() =>
            {
                foreach (var item in _createdPlaylistItems)
                {
                    NavView.MenuItems.Remove(item);
                }
                foreach (var item in _collectedPlaylistItems)
                {
                    NavView.MenuItems.Remove(item);
                }
                _createdPlaylistItems.Clear();
                _collectedPlaylistItems.Clear();
                CollectedPlaylistHeader.Visibility = Visibility.Collapsed;
            });
        }

        private List<NavigationViewItem> _createdPlaylistItems = new();
        private List<NavigationViewItem> _collectedPlaylistItems = new();

        private async System.Threading.Tasks.Task LoadPlaylistsAsync()
        {
            if (!App.ApiService.IsLoggedIn) return;

            var userId = App.ApiService.UserId;
            var playlists = await App.ApiService.GetUserPlaylistsAsync(userId);

            if (playlists == null || playlists.Count == 0) return;

            DispatcherQueue.TryEnqueue(() =>
            {
                // 清除旧的歌单项
                foreach (var item in _createdPlaylistItems)
                {
                    NavView.MenuItems.Remove(item);
                }
                foreach (var item in _collectedPlaylistItems)
                {
                    NavView.MenuItems.Remove(item);
                }
                _createdPlaylistItems.Clear();
                _collectedPlaylistItems.Clear();

                // 分类歌单：创建的和收藏的
                var createdPlaylists = playlists.Where(p => p.CreatorName == App.ApiService.GetCachedUserInfo()?.Nickname).ToList();
                var collectedPlaylists = playlists.Where(p => p.CreatorName != App.ApiService.GetCachedUserInfo()?.Nickname).ToList();

                // 添加创建的歌单
                int insertIndex = NavView.MenuItems.IndexOf(CreatedPlaylistHeader) + 1;
                foreach (var playlist in createdPlaylists)
                {
                    var item = new NavigationViewItem
                    {
                        Content = playlist.Name,
                        Tag = $"playlist_{playlist.Id}"
                    };
                    item.Icon = new FontIcon { Glyph = "\uE8B8" };
                    ToolTipService.SetToolTip(item, $"{playlist.TrackCount} 首歌曲");
                    
                    NavView.MenuItems.Insert(insertIndex, item);
                    _createdPlaylistItems.Add(item);
                    insertIndex++;
                }

                // 添加收藏的歌单
                if (collectedPlaylists.Count > 0)
                {
                    CollectedPlaylistHeader.Visibility = Visibility.Visible;
                    insertIndex = NavView.MenuItems.IndexOf(CollectedPlaylistHeader) + 1;
                    
                    foreach (var playlist in collectedPlaylists)
                    {
                        var item = new NavigationViewItem
                        {
                            Content = playlist.Name,
                            Tag = $"playlist_{playlist.Id}"
                        };
                        item.Icon = new FontIcon { Glyph = "\uE8B8" };
                        ToolTipService.SetToolTip(item, $"{playlist.TrackCount} 首歌曲");
                        
                        NavView.MenuItems.Insert(insertIndex, item);
                        _collectedPlaylistItems.Add(item);
                        insertIndex++;
                    }
                }
            });
        }

        private void SetupPlaybackEvents()
        {
            PlaybackService.PlaybackStateChanged += (s, isPlaying) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    PlayPauseIcon.Glyph = isPlaying ? "\uE769" : "\uE768";
                });
            };

            PlaybackService.CurrentSongChanged += (s, song) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    UpdateSongInfo(song);
                });
            };

            PlaybackService.PositionChanged += (s, position) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (!_isProgressDragging)
                    {
                        CurrentTime.Text = FormatTime(position);
                        var duration = PlaybackService.GetDuration();
                        if (duration.TotalSeconds > 0)
                        {
                            ProgressSlider.Value = (position.TotalSeconds / duration.TotalSeconds) * 100;
                        }
                    }
                });
            };

            PlaybackService.DurationChanged += (s, duration) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    TotalTime.Text = FormatTime(duration);
                });
            };
        }

        private void UpdateSongInfo(Song song)
        {
            if (song == null) return;

            SongTitle.Text = song.Name;
            ArtistName.Text = song.ArtistNames;

            if (!string.IsNullOrEmpty(song.CoverImgUrl))
            {
                AlbumImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(song.CoverImgUrl));
                AlbumImage.Visibility = Visibility.Visible;
                AlbumCoverIcon.Visibility = Visibility.Collapsed;
            }
            else
            {
                AlbumImage.Visibility = Visibility.Collapsed;
                AlbumCoverIcon.Visibility = Visibility.Visible;
            }
        }

        private string FormatTime(TimeSpan time)
        {
            return $"{(int)time.TotalMinutes:D2}:{time.Seconds:D2}";
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            PlaybackService.TogglePlayPause();
        }

        private async void PrevButton_Click(object sender, RoutedEventArgs e)
        {
            await PlaybackService.PreviousAsync();
        }

        private async void NextButton_Click(object sender, RoutedEventArgs e)
        {
            await PlaybackService.NextAsync();
        }

        private void ShuffleButton_Click(object sender, RoutedEventArgs e)
        {
            PlaybackService.ToggleShuffle();
            ShuffleButton.Opacity = PlaybackService.IsShuffleEnabled ? 1.0 : 0.5;
        }

        private void RepeatButton_Click(object sender, RoutedEventArgs e)
        {
            PlaybackService.ToggleRepeat();
            var mode = PlaybackService.GetRepeatMode();
            switch (mode)
            {
                case RepeatMode.None:
                    RepeatIcon.Glyph = "\uE8EE";
                    RepeatButton.Opacity = 0.5;
                    break;
                case RepeatMode.All:
                    RepeatIcon.Glyph = "\uE8EE";
                    RepeatButton.Opacity = 1.0;
                    break;
                case RepeatMode.One:
                    RepeatIcon.Glyph = "\uE8ED";
                    RepeatButton.Opacity = 1.0;
                    break;
            }
        }

        private void ProgressSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isProgressDragging && PlaybackService != null)
            {
                var duration = PlaybackService.GetDuration();
                if (duration.TotalSeconds > 0)
                {
                    var position = TimeSpan.FromSeconds((e.NewValue / 100) * duration.TotalSeconds);
                    PlaybackService.Seek(position);
                }
            }
        }

        private void ProgressSlider_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            _isProgressDragging = true;
        }

        private void ProgressSlider_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            _isProgressDragging = false;
        }

        private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (PlaybackService != null)
            {
                PlaybackService.Volume = e.NewValue / 100;
            }
            UpdateVolumeIcon(e.NewValue);
        }

        private void VolumeButton_Click(object sender, RoutedEventArgs e)
        {
            if (VolumeSlider.Value > 0)
            {
                VolumeSlider.Value = 0;
            }
            else
            {
                VolumeSlider.Value = 100;
            }
        }

        private void UpdateVolumeIcon(double volume)
        {
            if (volume == 0)
                VolumeIcon.Glyph = "\uE74F";
            else if (volume < 50)
                VolumeIcon.Glyph = "\uE993";
            else
                VolumeIcon.Glyph = "\uE767";
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                // 清空返回栈
                ClearBackStack();
                ContentFrame.Navigate(typeof(Pages.SettingsPage));
            }
            else if (args.SelectedItemContainer != null)
            {
                var tag = args.SelectedItemContainer.Tag.ToString();
                if (tag == "login")
                {
                    return;
                }

                // 清空返回栈
                ClearBackStack();

                switch (tag)
                {
                    case "recommend":
                        ContentFrame.Navigate(typeof(Pages.RecommendPage));
                        break;
                    case "follow":
                        ContentFrame.Navigate(typeof(Pages.FollowPage));
                        break;
                    case "liked":
                        ContentFrame.Navigate(typeof(Pages.LikedPage));
                        break;
                    case "recent":
                        ContentFrame.Navigate(typeof(Pages.RecentPage));
                        break;
                    case "downloaded":
                        ContentFrame.Navigate(typeof(Pages.DownloadedPage));
                        break;
                    case "cloud":
                        ContentFrame.Navigate(typeof(Pages.CloudPage));
                        break;
                    default:
                        // 处理动态歌单
                        if (tag != null && tag.StartsWith("playlist_"))
                        {
                            var playlistId = tag.Substring("playlist_".Length);
                            ContentFrame.Navigate(typeof(Pages.PlaylistDetailPage), playlistId);
                        }
                        break;
                }
            }
        }

        private void ClearBackStack()
        {
            while (ContentFrame.CanGoBack)
            {
                ContentFrame.GoBack();
            }
            BackBar.Opacity = 0;
            BackBar.Translation = new System.Numerics.Vector3(0, -48, 0);
            BackBar.Visibility = Visibility.Collapsed;
        }

        private void Quality_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is string quality)
            {
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values["AudioQuality"] = quality;
                
                var qualityName = quality switch
                {
                    "standard" => "标准",
                    "higher" => "较高",
                    "exhigh" => "极高",
                    "lossless" => "无损",
                    "hires" => "Hi-Res",
                    "jyeffect" => "高清环绕声",
                    "sky" => "沉浸环绕声",
                    "dolby" => "杜比全景声",
                    "jymaster" => "超清母带",
                    _ => "标准"
                };
                
                QualityText.Text = qualityName;
                System.Diagnostics.Debug.WriteLine($"[Settings] Audio quality changed to: {quality}");
            }
        }

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            // TODO: 实时搜索建议
        }

        private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            var queryText = args.QueryText;
            if (!string.IsNullOrWhiteSpace(queryText))
            {
                ContentFrame.Navigate(typeof(Pages.SearchPage), queryText);
            }
        }
    }
}