using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using music.Dialogs;
using music.Models;
using music.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Storage;

namespace music
{
    public sealed partial class MainWindow : Window
    {
        public static PlaybackService PlaybackService { get; private set; } = null!;
        public Frame MainContentFrame => ContentFrame;
        private bool _isProgressDragging = false;
        private bool _isPlaylistPanelOpen = false;

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

            // 初始化随机播放和循环播放按钮状态（默认关闭）
            ShuffleButton.Opacity = 0.5;
            RepeatIcon.Glyph = "\uE8EE";
            RepeatButton.Opacity = 0.5;

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
                // 在歌单详情、歌手详情、全部歌单、搜索页面、搜索结果页面显示返回按钮（歌词页面不显示）
                var currentPage = ContentFrame.CurrentSourcePageType;
                var showBack = currentPage == typeof(Pages.PlaylistDetailPage) ||
                               currentPage == typeof(Pages.ArtistDetailPage) ||
                               currentPage == typeof(Pages.AllPlaylistsPage) ||
                               currentPage == typeof(Pages.SearchPage) ||
                               currentPage == typeof(Pages.SearchAllResultsPage) ||
                               currentPage == typeof(Pages.SearchAllSongsPage) ||
                               currentPage == typeof(Pages.RecommendDailySongsPage) ||
                               currentPage == typeof(Pages.RecommendRadarPage);

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

        private double _paneStartWidth;
        private double _startX;
        private bool _isPaneResizing = false;

        private void PaneResizer_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _isPaneResizing = true;
            _paneStartWidth = NavView.OpenPaneLength;
            _startX = e.GetCurrentPoint(PaneResizer).Position.X;
            PaneResizer.CapturePointer(e.Pointer);
        }

        private void PaneResizer_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isPaneResizing) return;

            var currentX = e.GetCurrentPoint(PaneResizer).Position.X;
            var delta = currentX - _startX;
            var newWidth = Math.Clamp(_paneStartWidth + delta, 180, 500);
            NavView.OpenPaneLength = newWidth;
        }

        private void PaneResizer_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_isPaneResizing) return;

            _isPaneResizing = false;
            PaneResizer.ReleasePointerCapture(e.Pointer);
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
                    RefreshCurrentPage();
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

                    // 先隐藏VIP，等待异步检查后再显示
                    VipBadge.Visibility = Visibility.Collapsed;
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

            RefreshCurrentPage();
        }

        private List<NavigationViewItem> _createdPlaylistItems = new();
        private List<NavigationViewItem> _collectedPlaylistItems = new();

        private async System.Threading.Tasks.Task LoadPlaylistsAsync(bool bypassCache = false)
        {
            if (!App.ApiService.IsLoggedIn) return;

            var userId = App.ApiService.UserId;
            var playlists = await App.ApiService.GetUserPlaylistsAsync(userId, bypassCache);

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
                var createdPlaylists = playlists.Where(p => p.CreatorId == App.ApiService.UserId).ToList();
                var collectedPlaylists = playlists.Where(p => p.CreatorId != App.ApiService.UserId).ToList();

                // 添加创建的歌单
                int insertIndex = NavView.MenuItems.IndexOf(CreatePlaylistItem) + 1;
                foreach (var playlist in createdPlaylists)
                {
                    var item = new NavigationViewItem
                    {
                        Content = playlist.Name,
                        Tag = $"playlist_{playlist.Id}"
                    };
                    item.Icon = new FontIcon { Glyph = "\uE8B8" };
                    ToolTipService.SetToolTip(item, $"{playlist.TrackCount} 首歌曲");

                    var deleteButton = new MenuFlyoutItem { Text = "删除歌单", Tag = playlist.Id };
                    deleteButton.Click += DeletePlaylist_Click;
                    var flyout = new MenuFlyout();
                    flyout.Items.Add(deleteButton);
                    item.ContextFlyout = flyout;

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

                        var deleteButton = new MenuFlyoutItem { Text = "取消收藏", Tag = playlist.Id };
                        deleteButton.Click += UnsubscribePlaylist_Click;
                        var flyout = new MenuFlyout();
                        flyout.Items.Add(deleteButton);
                        item.ContextFlyout = flyout;

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
            PlaybackService?.ToggleShuffle();
            ShuffleButton.Opacity = PlaybackService?.IsShuffleEnabled == true ? 1.0 : 0.5;
        }

        private void RepeatButton_Click(object sender, RoutedEventArgs e)
        {
            PlaybackService?.ToggleRepeat();
            var mode = PlaybackService?.GetRepeatMode();
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
                if (tag == "login" || tag == "create_playlist")
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

        private void RefreshCurrentPage()
        {
            if (ContentFrame.CurrentSourcePageType == typeof(Pages.RecommendPage))
            {
                ContentFrame.Navigate(typeof(Pages.RecommendPage));
            }
        }

        private void NavView_PaneClosing(NavigationView sender, NavigationViewPaneClosingEventArgs args)
        {
            CreatePlaylistItem.Visibility = Visibility.Collapsed;
            CreatedPlaylistHeader.Visibility = Visibility.Collapsed;
        }

        private void NavView_PaneOpening(NavigationView sender, object args)
        {
            CreatePlaylistItem.Visibility = Visibility.Visible;
            CreatedPlaylistHeader.Visibility = Visibility.Visible;
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

        public void RefreshPlayerBarBackground()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                // 强制刷新播放控件背景，使其跟随主题变化
                var isDark = PlayerBar.ActualTheme == ElementTheme.Dark;
                PlayerBar.Background = isDark
                    ? new AcrylicBrush
                    {
                        TintColor = Windows.UI.Color.FromArgb(255, 32, 32, 32),
                        TintOpacity = 0.8,
                        FallbackColor = Windows.UI.Color.FromArgb(255, 32, 32, 32),
                        AlwaysUseFallback = false
                    }
                    : new AcrylicBrush
                    {
                        TintColor = Windows.UI.Color.FromArgb(255, 243, 243, 243),
                        TintOpacity = 0.8,
                        FallbackColor = Windows.UI.Color.FromArgb(255, 243, 243, 243),
                        AlwaysUseFallback = false
                    };
            });
        }

        private void PlaylistButton_Click(object sender, RoutedEventArgs e)
        {
            TogglePlaylistPanel();
        }

        private void TogglePlaylistPanel()
        {
            if (_isPlaylistPanelOpen)
            {
                HidePlaylistPanel();
            }
            else
            {
                ShowPlaylistPanel();
            }
        }

        private void ShowPlaylistPanel()
        {
            _isPlaylistPanelOpen = true;
            UpdatePlaylistPanel();
            PlaylistColumn.Width = new GridLength(360);
            PlaylistPanelSlideIn();
        }

        private void HidePlaylistPanel()
        {
            _isPlaylistPanelOpen = false;
            PlaylistPanelSlideOut();
        }

        private void PlaylistPanelSlideIn()
        {
            PlaylistPanel.Translation = new System.Numerics.Vector3(360, 0, 0);
            PlaylistPanel.Opacity = 0;

            double progress = 0;
            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(16);
            timer.Tick += (s, e) =>
            {
                progress += 0.06;
                if (progress >= 1)
                {
                    progress = 1;
                    timer.Stop();
                }
                var eased = BackEaseOut(progress);
                var width = 360 * Math.Min(1, eased);
                PlaylistColumn.Width = new GridLength(Math.Max(0, width));
                PlaylistPanel.Translation = new System.Numerics.Vector3((float)Math.Max(0, 360 - width), 0, 0);
                PlaylistPanel.Opacity = (float)Math.Min(1, progress * 5);
            };
            timer.Start();
        }

        private void PlaylistPanelSlideOut()
        {
            double startWidth = PlaylistColumn.Width.Value;
            double progress = 0;
            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(16);
            timer.Tick += (s, e) =>
            {
                progress += 0.08;
                if (progress >= 1)
                {
                    progress = 1;
                    timer.Stop();
                    PlaylistColumn.Width = new GridLength(0);
                }
                var eased = CubicEaseIn(progress);
                PlaylistColumn.Width = new GridLength(startWidth * (1 - eased));
                PlaylistPanel.Translation = new System.Numerics.Vector3((float)(360 * eased), 0, 0);
                PlaylistPanel.Opacity = (float)(1 - eased);
            };
            timer.Start();
        }

        // 平滑缓出带轻微过冲 - 无晃动
        private double BackEaseOut(double t)
        {
            const double c1 = 1.70158;
            const double c3 = c1 + 1;
            return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
        }

        // 三次缓入
        private double CubicEaseIn(double t)
        {
            return t * t * t;
        }

        private void UpdatePlaylistPanel()
        {
            PlaylistListView.Items.Clear();

            var currentSong = PlaybackService.CurrentSong;
            var playlist = PlaybackService.GetPlaylist();

            PlaylistCountText.Text = $"{playlist.Count}首歌曲";

            foreach (var song in playlist)
            {
                PlaylistListView.Items.Add(song.Name);
            }
        }

        private void PlaylistListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is string songName)
            {
                var playlist = PlaybackService.GetPlaylist();
                var index = playlist.FindIndex(s => s.Name == songName);
                if (index >= 0)
                {
                    _ = PlaybackService.PlayAsync(playlist, index);
                }
                HidePlaylistPanel();
            }
        }

        private async void CreatePlaylistItem_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (!App.ApiService.IsLoggedIn)
            {
                var dialog = new LoginDialog();
                dialog.XamlRoot = ContentFrame.XamlRoot;
                await dialog.ShowAsync();
                return;
            }

            var inputTextBox = new TextBox
            {
                PlaceholderText = "请输入歌单名称",
                MaxLength = 40,
                Width = 300
            };

            var dialog2 = new ContentDialog
            {
                Title = "新建歌单",
                Content = inputTextBox,
                PrimaryButtonText = "创建",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = ContentFrame.XamlRoot
            };

            var result = await dialog2.ShowAsync();
            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(inputTextBox.Text))
            {
                var name = inputTextBox.Text.Trim();
                var success = await App.ApiService.CreatePlaylistAsync(name);
                if (success)
                {
                    await LoadPlaylistsAsync(bypassCache: true);
                }
            }
        }

        private async void DeletePlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem button && button.Tag is string playlistId)
            {
                var dialog = new ContentDialog
                {
                    Title = "删除歌单",
                    Content = "确定要删除这个歌单吗？此操作不可撤销。",
                    PrimaryButtonText = "删除",
                    CloseButtonText = "取消",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = ContentFrame.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    var success = await App.ApiService.DeletePlaylistAsync(playlistId);
                    if (success)
                    {
                        await LoadPlaylistsAsync(bypassCache: true);
                    }
                }
            }
        }

        private async void UnsubscribePlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem button && button.Tag is string playlistId)
            {
                var success = await App.ApiService.UnsubscribePlaylistAsync(playlistId);
                if (success)
                {
                    await LoadPlaylistsAsync(bypassCache: true);
                }
            }
        }
    }
}