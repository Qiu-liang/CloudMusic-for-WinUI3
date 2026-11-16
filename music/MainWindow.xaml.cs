using System;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using music.Composition;
using music.Core.Mvvm;
using music.Core.ViewModels;
using music.Models;
using music.Services;

namespace music
{
    public sealed partial class MainWindow : Window
    {
        private readonly AppCompositionRoot _root;
        private readonly ShellViewModel _shellVm;
        private readonly PlayerBarViewModel _playerBarVm;
        private readonly SearchSuggestionsViewModel _searchSuggestionsVm;
        private readonly List<NavigationViewItem> _createdPlaylistItems = new();
        private readonly List<NavigationViewItem> _collectedPlaylistItems = new();

        private bool _isPlaylistPanelOpen;
        private bool _syncingVolume;
        private double _paneStartWidth;
        private double _startX;
        private bool _isPaneResizing;

        internal MainWindow(AppCompositionRoot root)
        {
            InitializeComponent();

            _root = root;
            root.InitializeUi(this.DispatcherQueue);
            root.AttachMainWindow(this, ContentFrame, ApplyTheme);

            _shellVm = root.CreateShellViewModel();
            _playerBarVm = root.CreatePlayerBarViewModel();
            _searchSuggestionsVm = root.CreateSearchSuggestionsViewModel();

            SearchBox.ItemsSource = _searchSuggestionsVm.Suggestions;

            _shellVm.PropertyChanged += ShellVm_PropertyChanged;
            _shellVm.PlaylistsMenuUpdated += ShellVm_PlaylistsMenuUpdated;
            _playerBarVm.PropertyChanged += PlayerBarVm_PropertyChanged;
            _playerBarVm.PlaylistUpdated += PlayerBarVm_PlaylistUpdated;
            Closed += MainWindow_Closed;

            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            appWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 1460, Height = 1064 });

            SizeChanged += MainWindow_SizeChanged;
            NavView.SelectedItem = NavView.MenuItems[0];
            SetWindowIcon();

            ProgressSlider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(ProgressSlider_PointerPressed), true);
            ProgressSlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(ProgressSlider_PointerReleased), true);
            ProgressSlider.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(ProgressSlider_PointerCaptureLost), true);

            LoginItem.Tapped += (_, _) =>
                _shellVm.HandleLoginItemTappedAsync().Forget(_root.ApiLog, "MainWindow.LoginItem");

            SyncShellToUi();
            SyncPlayerBarToUi();
            _shellVm.InitializeSessionRestore();
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            _shellVm.PropertyChanged -= ShellVm_PropertyChanged;
            _shellVm.PlaylistsMenuUpdated -= ShellVm_PlaylistsMenuUpdated;
            _playerBarVm.PropertyChanged -= PlayerBarVm_PropertyChanged;
            _playerBarVm.PlaylistUpdated -= PlayerBarVm_PlaylistUpdated;

            _shellVm.Dispose();
            _playerBarVm.Dispose();
            _searchSuggestionsVm.Dispose();
        }

        private void ShellVm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(ShellViewModel.LoginStatusText):
                    LoginStatusText.Text = _shellVm.LoginStatusText;
                    break;
                case nameof(ShellViewModel.LoginSubText):
                    LoginSubText.Text = _shellVm.LoginSubText;
                    break;
                case nameof(ShellViewModel.ShowLogoutButton):
                    LogoutButton.Visibility = _shellVm.ShowLogoutButton ? Visibility.Visible : Visibility.Collapsed;
                    break;
                case nameof(ShellViewModel.ShowVipBadge):
                    VipBadge.Visibility = _shellVm.ShowVipBadge ? Visibility.Visible : Visibility.Collapsed;
                    break;
                case nameof(ShellViewModel.ShowBackBar):
                    SyncBackBarToUi();
                    break;
            }
        }

        private void ShellVm_PlaylistsMenuUpdated(object? sender, EventArgs e)
        {
            SyncPlaylistsMenu();
        }

        private void PlayerBarVm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(PlayerBarViewModel.SongTitle):
                    SongTitle.Text = _playerBarVm.SongTitle;
                    break;
                case nameof(PlayerBarViewModel.ArtistName):
                    ArtistName.Text = _playerBarVm.ArtistName;
                    break;
                case nameof(PlayerBarViewModel.CoverUrl):
                case nameof(PlayerBarViewModel.HasCover):
                    SyncCoverToUi();
                    break;
                case nameof(PlayerBarViewModel.IsPlaying):
                    PlayPauseIcon.Glyph = _playerBarVm.IsPlaying ? "\uE769" : "\uE768";
                    break;
                case nameof(PlayerBarViewModel.CurrentSongLiked):
                    LikeIcon.Glyph = _playerBarVm.CurrentSongLiked ? "\uE735" : "\uE734";
                    break;
                case nameof(PlayerBarViewModel.IsLikeEnabled):
                    LikeButton.IsEnabled = _playerBarVm.IsLikeEnabled;
                    break;
                case nameof(PlayerBarViewModel.IsDownloadEnabled):
                    DownloadButton.IsEnabled = _playerBarVm.IsDownloadEnabled;
                    break;
                case nameof(PlayerBarViewModel.CurrentTimeText):
                    CurrentTime.Text = _playerBarVm.CurrentTimeText;
                    break;
                case nameof(PlayerBarViewModel.TotalTimeText):
                    TotalTime.Text = _playerBarVm.TotalTimeText;
                    break;
                case nameof(PlayerBarViewModel.ProgressPercent):
                    ProgressSlider.Value = _playerBarVm.ProgressPercent;
                    break;
                case nameof(PlayerBarViewModel.VolumePercent):
                    _syncingVolume = true;
                    try
                    {
                        VolumeSlider.Value = _playerBarVm.VolumePercent;
                        UpdateVolumeIcon(_playerBarVm.VolumePercent);
                    }
                    finally
                    {
                        _syncingVolume = false;
                    }
                    break;
                case nameof(PlayerBarViewModel.IsShuffleEnabled):
                    ShuffleButton.Opacity = _playerBarVm.IsShuffleEnabled ? 1.0 : 0.5;
                    break;
                case nameof(PlayerBarViewModel.RepeatMode):
                    SyncRepeatToUi();
                    break;
                case nameof(PlayerBarViewModel.QualityText):
                    QualityText.Text = _playerBarVm.QualityText;
                    break;
                case nameof(PlayerBarViewModel.QualityOptions):
                    SyncQualityOptionsToUi();
                    break;
                case nameof(PlayerBarViewModel.IsErrorOpen):
                case nameof(PlayerBarViewModel.ErrorMessage):
                    PlaybackErrorInfoBar.Message = _playerBarVm.ErrorMessage;
                    PlaybackErrorInfoBar.IsOpen = _playerBarVm.IsErrorOpen;
                    break;
            }
        }

        private void PlayerBarVm_PlaylistUpdated(object? sender, EventArgs e)
        {
            if (_isPlaylistPanelOpen)
            {
                UpdatePlaylistPanel();
            }
        }

        private void SyncShellToUi()
        {
            LoginStatusText.Text = _shellVm.LoginStatusText;
            LoginSubText.Text = _shellVm.LoginSubText;
            LoginIcon.Symbol = Symbol.Contact;
            LogoutButton.Visibility = _shellVm.ShowLogoutButton ? Visibility.Visible : Visibility.Collapsed;
            VipBadge.Visibility = _shellVm.ShowVipBadge ? Visibility.Visible : Visibility.Collapsed;
            SyncBackBarToUi();
        }

        private void SyncPlayerBarToUi()
        {
            PlayPauseIcon.Glyph = _playerBarVm.IsPlaying ? "\uE769" : "\uE768";
            LikeIcon.Glyph = _playerBarVm.CurrentSongLiked ? "\uE735" : "\uE734";
            LikeButton.IsEnabled = _playerBarVm.IsLikeEnabled;
            DownloadButton.IsEnabled = _playerBarVm.IsDownloadEnabled;
            ShuffleButton.Opacity = _playerBarVm.IsShuffleEnabled ? 1.0 : 0.5;
            SyncRepeatToUi();
            QualityText.Text = _playerBarVm.QualityText;
            SyncQualityOptionsToUi();
            if (_playerBarVm.CurrentSong != null)
            {
                SongTitle.Text = _playerBarVm.SongTitle;
                ArtistName.Text = _playerBarVm.ArtistName;
                SyncCoverToUi();
            }
        }

        private void SyncCoverToUi()
        {
            if (!string.IsNullOrEmpty(_playerBarVm.CoverUrl))
            {
                AlbumImage.Source = new BitmapImage(new Uri(_playerBarVm.CoverUrl));
                AlbumImage.Visibility = Visibility.Visible;
                AlbumCoverIcon.Visibility = Visibility.Collapsed;
            }
            else
            {
                AlbumImage.Source = null;
                AlbumImage.Visibility = Visibility.Collapsed;
                AlbumCoverIcon.Visibility = Visibility.Visible;
            }
        }

        private void SyncRepeatToUi()
        {
            switch (_playerBarVm.RepeatMode)
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

        private void SyncQualityOptionsToUi()
        {
            if (QualityMenuFlyout?.Items == null) return;
            var map = new Dictionary<string, QualityOptionDisplay>(StringComparer.OrdinalIgnoreCase);
            foreach (var option in _playerBarVm.QualityOptions)
            {
                map[option.Tag] = option;
            }

            foreach (var element in QualityMenuFlyout.Items)
            {
                if (element is MenuFlyoutItem item &&
                    item.Tag is string tag &&
                    map.TryGetValue(tag, out var option))
                {
                    item.Text = option.Text;
                    item.Opacity = option.Opacity;
                }
            }
        }

        private void SyncBackBarToUi()
        {
            if (_shellVm.ShowBackBar)
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
        }

        private void SyncPlaylistsMenu()
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

            if (_shellVm.CreatedPlaylists.Count == 0 && _shellVm.CollectedPlaylists.Count == 0)
            {
                CollectedPlaylistHeader.Visibility = Visibility.Collapsed;
                CreatedPlaylistHeader.Visibility = Visibility.Collapsed;
                return;
            }

            CreatedPlaylistHeader.Visibility = Visibility.Visible;
            int insertIndex = NavView.MenuItems.IndexOf(CreatePlaylistItem) + 1;
            foreach (var playlist in _shellVm.CreatedPlaylists)
            {
                var item = new NavigationViewItem
                {
                    Content = playlist.Name,
                    Tag = $"playlist_{playlist.Id}",
                    Icon = new FontIcon { Glyph = "\uE8B8" }
                };
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

            if (_shellVm.CollectedPlaylists.Count > 0)
            {
                CollectedPlaylistHeader.Visibility = Visibility.Visible;
                insertIndex = NavView.MenuItems.IndexOf(CollectedPlaylistHeader) + 1;

                foreach (var playlist in _shellVm.CollectedPlaylists)
                {
                    var item = new NavigationViewItem
                    {
                        Content = playlist.Name,
                        Tag = $"playlist_{playlist.Id}",
                        Icon = new FontIcon { Glyph = "\uE8B8" }
                    };
                    ToolTipService.SetToolTip(item, $"{playlist.TrackCount} 首歌曲");

                    var unsubscribeButton = new MenuFlyoutItem { Text = "取消收藏", Tag = playlist.Id };
                    unsubscribeButton.Click += UnsubscribePlaylist_Click;
                    var flyout = new MenuFlyout();
                    flyout.Items.Add(unsubscribeButton);
                    item.ContextFlyout = flyout;

                    NavView.MenuItems.Insert(insertIndex, item);
                    _collectedPlaylistItems.Add(item);
                    insertIndex++;
                }
            }
            else
            {
                CollectedPlaylistHeader.Visibility = Visibility.Collapsed;
            }
        }

        private void QualityMenuFlyout_Opening(object sender, object e)
        {
            _playerBarVm.OnQualityMenuOpeningAsync().Forget(_root.ApiLog, "MainWindow.QualityOpening");
        }

        private void Quality_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is string quality)
            {
                _playerBarVm.SelectQualityAsync(quality).Forget(_root.ApiLog, "MainWindow.SelectQuality");
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e) => _shellVm.GoBack();

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
            NavView.OpenPaneLength = Math.Clamp(_paneStartWidth + delta, 180, 500);
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

        private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs e)
        {
            const int minWidth = 800;
            const int minHeight = 700;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

            if (appWindow.Size.Width < minWidth || appWindow.Size.Height < minHeight)
            {
                appWindow.Resize(new Windows.Graphics.SizeInt32
                {
                    Width = Math.Max(appWindow.Size.Width, minWidth),
                    Height = Math.Max(appWindow.Size.Height, minHeight)
                });
            }
        }

        private void AlbumCover_Tapped(object sender, TappedRoutedEventArgs e) =>
            _playerBarVm.OpenLyricsOrWindow();

        private void AlbumCover_PointerEntered(object sender, PointerRoutedEventArgs e) =>
            AlbumCover.Opacity = 0.8;

        private void AlbumCover_PointerExited(object sender, PointerRoutedEventArgs e) =>
            AlbumCover.Opacity = 1.0;

        private void LogoutButton_Click(object sender, RoutedEventArgs e) =>
            _shellVm.PerformLogout();

        private void LikeButton_Click(object sender, RoutedEventArgs e) =>
            _playerBarVm.ToggleLikeAsync().Forget(_root.ApiLog, "MainWindow.Like");

        private void DownloadButton_Click(object sender, RoutedEventArgs e) =>
            _playerBarVm.DownloadCurrentAsync().Forget(_root.ApiLog, "MainWindow.Download");

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e) =>
            _playerBarVm.TogglePlayPause();

        private void PrevButton_Click(object sender, RoutedEventArgs e) =>
            _playerBarVm.PreviousAsync().Forget(_root.ApiLog, "MainWindow.Prev");

        private void NextButton_Click(object sender, RoutedEventArgs e) =>
            _playerBarVm.NextAsync().Forget(_root.ApiLog, "MainWindow.Next");

        private void ShuffleButton_Click(object sender, RoutedEventArgs e) =>
            _playerBarVm.ToggleShuffle();

        private void RepeatButton_Click(object sender, RoutedEventArgs e) =>
            _playerBarVm.ToggleRepeat();

        private void ProgressSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e) =>
            _playerBarVm.ProgressValueChanged(e.NewValue);

        private void ProgressSlider_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _playerBarVm.ProgressPointerPressed();
            ProgressSlider.CapturePointer(e.Pointer);
        }

        private void ProgressSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _playerBarVm.ProgressPointerReleased();
            ProgressSlider.ReleasePointerCapture(e.Pointer);
        }

        private void ProgressSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e) =>
            _playerBarVm.ProgressPointerCaptureLost();

        private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_syncingVolume || _playerBarVm == null) return;
            _playerBarVm.SetVolumePercent(e.NewValue);
            UpdateVolumeIcon(e.NewValue);
        }

        private void VolumeButton_Click(object sender, RoutedEventArgs e)
        {
            VolumeSlider.Value = VolumeSlider.Value > 0 ? 0 : 100;
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
            var tag = args.SelectedItemContainer?.Tag?.ToString();
            _shellVm.NavigateByTag(tag, args.IsSettingsSelected);
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

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            _searchSuggestionsVm.UpdateQueryAsync(sender.Text, () => sender.Text)
                .Forget(_root.ApiLog, "MainWindow.SearchSuggest");
        }

        private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            var queryText = args.ChosenSuggestion is SearchSuggestion suggestion
                ? suggestion.Name
                : args.QueryText;
            _searchSuggestionsVm.SubmitQuery(queryText);
        }

        public void ApplySavedTheme()
        {
            ApplyTheme(_root.SettingsService.Theme);
            if (Content is FrameworkElement root)
                root.ActualThemeChanged += (_, _) => UpdateWindowChrome();
        }

        public void ApplyTheme(string theme)
        {
            if (Content is FrameworkElement root)
            {
                root.RequestedTheme = ThemeSettings.Parse(theme) switch
                {
                    ThemeMode.Light => ElementTheme.Light,
                    ThemeMode.Dark => ElementTheme.Dark,
                    _ => ElementTheme.Default
                };
            }
            DispatcherQueue.TryEnqueue(UpdateWindowChrome);
        }

        private void UpdateWindowChrome()
        {
            try
            {
                var isDark = (Content as FrameworkElement)?.ActualTheme == ElementTheme.Dark;
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd));
                if (appWindow.TitleBar != null)
                {
                    var background = isDark
                        ? Windows.UI.Color.FromArgb(255, 32, 32, 32)
                        : Windows.UI.Color.FromArgb(255, 243, 243, 243);
                    var foreground = isDark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
                    appWindow.TitleBar.BackgroundColor = background;
                    appWindow.TitleBar.ButtonBackgroundColor = background;
                    appWindow.TitleBar.ForegroundColor = foreground;
                    appWindow.TitleBar.ButtonForegroundColor = foreground;
                    appWindow.TitleBar.ButtonHoverBackgroundColor = isDark
                        ? Windows.UI.Color.FromArgb(255, 55, 55, 55)
                        : Windows.UI.Color.FromArgb(255, 225, 225, 225);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Theme] Window chrome update failed: {ex.Message}");
            }
            RefreshPlayerBarBackground();
        }

        public void RefreshPlayerBarBackground()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
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

        private void PlaylistButton_Click(object sender, RoutedEventArgs e) => TogglePlaylistPanel();

        private void TogglePlaylistPanel()
        {
            if (_isPlaylistPanelOpen)
                HidePlaylistPanel();
            else
                ShowPlaylistPanel();
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
            timer.Tick += (_, _) =>
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
            timer.Tick += (_, _) =>
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

        private static double BackEaseOut(double t)
        {
            const double c1 = 1.70158;
            const double c3 = c1 + 1;
            return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
        }

        private static double CubicEaseIn(double t) => t * t * t;

        private void UpdatePlaylistPanel()
        {
            PlaylistListView.Items.Clear();

            var playlist = _playerBarVm.GetPlaylist();
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
                var playlist = _playerBarVm.GetPlaylist();
                var index = playlist.FindIndex(s => s.Name == songName);
                if (index >= 0)
                {
                    _playerBarVm.PlayFromPlaylistIndexAsync(index).Forget(_root.ApiLog, "MainWindow.PlayFromQueue");
                }
                HidePlaylistPanel();
            }
        }

        private void CreatePlaylistItem_Tapped(object sender, TappedRoutedEventArgs e) =>
            _shellVm.CreatePlaylistAsync().Forget(_root.ApiLog, "MainWindow.CreatePlaylist");

        private void DeletePlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem button && button.Tag is string playlistId)
            {
                _shellVm.DeletePlaylistAsync(playlistId).Forget(_root.ApiLog, "MainWindow.DeletePlaylist");
            }
        }

        private void UnsubscribePlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem button && button.Tag is string playlistId)
            {
                _shellVm.UnsubscribePlaylistAsync(playlistId).Forget(_root.ApiLog, "MainWindow.UnsubscribePlaylist");
            }
        }
    }
}
