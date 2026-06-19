using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using music.Models;
using music.Services;

namespace music.Pages
{
    public class TrackCountConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is int count)
            {
                return $"{count}首";
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public sealed partial class RecommendPage : Page
    {
        private readonly ObservableCollection<SongItem> _songs = new();
        private readonly ObservableCollection<PlaylistItem> _playlists = new();
        private readonly ObservableCollection<SongItem> _personalizedSongs = new();
        private readonly ObservableCollection<PlaylistItem> _radarPlaylists = new();
        private readonly ObservableCollection<SongItem> _intelligenceSongs = new();
        private readonly ObservableCollection<SongItem> _guessSongs = new();
        private List<Song> _songModels = new();
        private List<Song> _personalizedSongModels = new();
        private List<Song> _intelligenceModels = new();
        private List<Song> _guessModels = new();
        private readonly List<Border> _dailySongCardBorders = new();
        private readonly List<Border> _intelligenceCardBorders = new();
        private readonly List<Border> _guessCardBorders = new();
        private readonly List<TextBlock> _dailySongNameTexts = new();
        private readonly List<TextBlock> _dailySongArtistTexts = new();
        private readonly List<TextBlock> _dailySongDurationTexts = new();
        private readonly List<TextBlock> _intelligenceNameTexts = new();
        private readonly List<TextBlock> _intelligenceArtistTexts = new();
        private readonly List<TextBlock> _intelligenceDurationTexts = new();
        private readonly List<TextBlock> _guessNameTexts = new();
        private readonly List<TextBlock> _guessArtistTexts = new();
        private readonly List<TextBlock> _guessDurationTexts = new();

        public RecommendPage()
        {
            this.InitializeComponent();
            PlaylistItems.ItemsSource = _playlists;
            PersonalizedItems.ItemsSource = _personalizedSongs;
            RadarItems.ItemsSource = _radarPlaylists;
            RegisterWheelForwarding();
            this.ActualThemeChanged += RecommendPage_ActualThemeChanged;
            Loaded += RecommendPage_Loaded;
        }

        private void RecommendPage_ActualThemeChanged(FrameworkElement sender, object args)
        {
            RefreshCardBackgrounds();
        }

        private void RefreshCardBackgrounds()
        {
            var isDark = this.ActualTheme == ElementTheme.Dark;
            var cardBrush = isDark
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 39, 39, 39))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 249, 249, 249));
            var primaryBrush = isDark
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0));
            var secondaryBrush = isDark
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 153, 153, 153))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 102, 102, 102));

            for (int i = 0; i < _dailySongCardBorders.Count; i++)
            {
                _dailySongCardBorders[i].Background = cardBrush;
                if (i < _dailySongNameTexts.Count) _dailySongNameTexts[i].Foreground = primaryBrush;
                if (i < _dailySongArtistTexts.Count) _dailySongArtistTexts[i].Foreground = secondaryBrush;
                if (i < _dailySongDurationTexts.Count) _dailySongDurationTexts[i].Foreground = secondaryBrush;
            }
            for (int i = 0; i < _intelligenceCardBorders.Count; i++)
            {
                _intelligenceCardBorders[i].Background = cardBrush;
                if (i < _intelligenceNameTexts.Count) _intelligenceNameTexts[i].Foreground = primaryBrush;
                if (i < _intelligenceArtistTexts.Count) _intelligenceArtistTexts[i].Foreground = secondaryBrush;
                if (i < _intelligenceDurationTexts.Count) _intelligenceDurationTexts[i].Foreground = secondaryBrush;
            }
            for (int i = 0; i < _guessCardBorders.Count; i++)
            {
                _guessCardBorders[i].Background = cardBrush;
                if (i < _guessNameTexts.Count) _guessNameTexts[i].Foreground = primaryBrush;
                if (i < _guessArtistTexts.Count) _guessArtistTexts[i].Foreground = secondaryBrush;
                if (i < _guessDurationTexts.Count) _guessDurationTexts[i].Foreground = secondaryBrush;
            }
        }

        private async void RecommendPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadAllDataAsync();
        }

        private async System.Threading.Tasks.Task LoadAllDataAsync()
        {
            ShowLoading();

            try
            {
                var playlists = await App.ApiService.GetRecommendedPlaylistsAsync(20);
                var personalized = await App.ApiService.GetPersonalizedSongsAsync(20);
                var daily = await App.ApiService.GetDailyRecommendSongsAsync(38);
                var radar = await App.ApiService.GetRecommendResourceAsync();
                var guess = await App.ApiService.GetPersonalizedSongsAsync(38);
                var liked = await App.ApiService.GetLikedSongsListAsync();

                _playlists.Clear();
                foreach (var playlist in playlists)
                {
                    _playlists.Add(new PlaylistItem
                    {
                        Id = playlist.Id,
                        Name = playlist.Name,
                        PicUrl = playlist.PicUrl,
                        PlayCount = playlist.PlayCount,
                        TrackCount = playlist.TrackCount,
                        PlayCountFormatted = playlist.PlayCountFormatted
                    });
                }

                _personalizedSongModels = personalized;
                _personalizedSongs.Clear();
                foreach (var song in personalized)
                {
                    _personalizedSongs.Add(new SongItem
                    {
                        Id = song.Id,
                        Name = song.Name,
                        ArtistNames = song.ArtistNames,
                        CoverUrl = song.CoverImgUrl,
                        CanPlay = song.CanPlay,
                        Fee = song.Fee,
                        IsVip = song.IsVip,
                        IsPaid = song.IsPaid,
                        FeeText = song.FeeText
                    });
                }

                _songModels = daily;
                _songs.Clear();
                DailySongsContainer.Children.Clear();
                _dailySongCardBorders.Clear();
                _dailySongNameTexts.Clear();
                _dailySongArtistTexts.Clear();
                _dailySongDurationTexts.Clear();
                
                if (daily != null)
                {
                    for (int i = 0; i < daily.Count; i++)
                    {
                        var song = daily[i];
                        _songs.Add(new SongItem
                        {
                            Id = song.Id,
                            Name = song.Name,
                            ArtistNames = song.ArtistNames,
                            AlbumName = song.Album.Name,
                            CoverUrl = song.CoverImgUrl,
                            DurationFormatted = song.DurationFormatted,
                            Index = i + 1,
                            IsLiked = song.Liked,
                            CanPlay = song.CanPlay,
                            Fee = song.Fee,
                            IsVip = song.IsVip,
                            IsPaid = song.IsPaid,
                            FeeText = song.FeeText
                        });
                    }
                    
                    // 每3首歌一组，创建列
                    for (int col = 0; col < (_songs.Count + 2) / 3; col++)
                    {
                        var column = new StackPanel { Spacing = 8, Width = 300 };
                        for (int row = 0; row < 3; row++)
                        {
                            int index = col * 3 + row;
                            if (index < _songs.Count)
                            {
                                var card = CreateDailySongCard(_songs[index], index);
                                _dailySongCardBorders.Add(card);
                                column.Children.Add(card);
                            }
                        }
                        DailySongsContainer.Children.Add(column);
                    }
                }

                // 根据登录状态显示/隐藏需要登录的模块
                var isLoggedIn = App.ApiService.IsLoggedIn;
                IntelligenceSection.Visibility = isLoggedIn ? Visibility.Visible : Visibility.Collapsed;
                RadarSection.Visibility = isLoggedIn ? Visibility.Visible : Visibility.Collapsed;

                // 雷达歌单
                _radarPlaylists.Clear();
                if (isLoggedIn)
                {
                    foreach (var playlist in radar)
                    {
                        _radarPlaylists.Add(new PlaylistItem
                        {
                            Id = playlist.Id,
                            Name = playlist.Name,
                            PicUrl = playlist.PicUrl,
                            PlayCount = playlist.PlayCount,
                            TrackCount = playlist.TrackCount,
                            PlayCountFormatted = playlist.PlayCountFormatted
                        });
                    }
                }

                // 你的红心歌曲和相似推荐
                _intelligenceModels = liked;
                _intelligenceSongs.Clear();
                IntelligenceContainer.Children.Clear();
                _intelligenceCardBorders.Clear();
                _intelligenceNameTexts.Clear();
                _intelligenceArtistTexts.Clear();
                _intelligenceDurationTexts.Clear();
                
                if (isLoggedIn && liked != null && liked.Count > 0)
                {
                    for (int i = 0; i < liked.Count; i++)
                    {
                        var song = liked[i];
                        _intelligenceSongs.Add(new SongItem
                        {
                            Id = song.Id,
                            Name = song.Name,
                            ArtistNames = song.ArtistNames,
                            AlbumName = song.Album.Name,
                            CoverUrl = song.CoverImgUrl,
                            DurationFormatted = song.DurationFormatted,
                            Index = i + 1,
                            IsLiked = song.Liked,
                            CanPlay = song.CanPlay,
                            Fee = song.Fee,
                            IsVip = song.IsVip,
                            IsPaid = song.IsPaid,
                            FeeText = song.FeeText
                        });
                    }
                    
                    // 每3首歌一组，创建列
                    for (int col = 0; col < (_intelligenceSongs.Count + 2) / 3; col++)
                    {
                        var column = new StackPanel { Spacing = 8, Width = 300 };
                        for (int row = 0; row < 3; row++)
                        {
                            int index = col * 3 + row;
                            if (index < _intelligenceSongs.Count)
                            {
                                var card = CreateIntelligenceSongCard(_intelligenceSongs[index], index);
                                _intelligenceCardBorders.Add(card);
                                column.Children.Add(card);
                            }
                        }
                        IntelligenceContainer.Children.Add(column);
                    }
                }

                // 猜你喜欢的歌
                _guessModels = guess;
                _guessSongs.Clear();
                GuessContainer.Children.Clear();
                _guessCardBorders.Clear();
                _guessNameTexts.Clear();
                _guessArtistTexts.Clear();
                _guessDurationTexts.Clear();
                
                if (guess != null)
                {
                    for (int i = 0; i < guess.Count; i++)
                    {
                        var song = guess[i];
                        _guessSongs.Add(new SongItem
                        {
                            Id = song.Id,
                            Name = song.Name,
                            ArtistNames = song.ArtistNames,
                            AlbumName = song.Album.Name,
                            CoverUrl = song.CoverImgUrl,
                            DurationFormatted = song.DurationFormatted,
                            Index = i + 1,
                            IsLiked = song.Liked,
                            CanPlay = song.CanPlay,
                            Fee = song.Fee,
                            IsVip = song.IsVip,
                            IsPaid = song.IsPaid,
                            FeeText = song.FeeText
                        });
                    }
                    
                    // 每3首歌一组，创建列
                    for (int col = 0; col < (_guessSongs.Count + 2) / 3; col++)
                    {
                        var column = new StackPanel { Spacing = 8, Width = 300 };
                        for (int row = 0; row < 3; row++)
                        {
                            int index = col * 3 + row;
                            if (index < _guessSongs.Count)
                            {
                                var card = CreateGuessSongCard(_guessSongs[index], index);
                                _guessCardBorders.Add(card);
                                column.Children.Add(card);
                            }
                        }
                        GuessContainer.Children.Add(column);
                    }
                }

                RefreshCardBackgrounds();
                ShowContent();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Recommend] Load Error: {ex.Message}");
                ShowError("加载失败，请稍后重试");
            }
        }

        private void ShowLoading()
        {
            LoadingPanel.Visibility = Visibility.Visible;
            ErrorPanel.Visibility = Visibility.Collapsed;
            ContentPanel.Visibility = Visibility.Collapsed;
        }

        private void ShowError(string message)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
            ContentPanel.Visibility = Visibility.Collapsed;
            ErrorText.Text = message;
        }

        private void ShowContent()
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;
            ContentPanel.Visibility = Visibility.Visible;
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadAllDataAsync();
        }

        private async void SongsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is SongItem songItem)
            {
                var index = _songs.IndexOf(songItem);
                if (index >= 0 && index < _songModels.Count)
                {
                    await MainWindow.PlaybackService.PlayAsync(_songModels, index);
                }
            }
        }

        private void PlaylistItem_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is PlaylistItem playlistItem)
            {
                // 导航到歌单详情页面
                var mainWindow = App.m_window as MainWindow;
                if (mainWindow != null)
                {
                    mainWindow.MainContentFrame.Navigate(typeof(PlaylistDetailPage), playlistItem.Id);
                }
            }
        }

        private void ViewAllPlaylistsButton_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = App.m_window as MainWindow;
            if (mainWindow != null)
            {
                mainWindow.MainContentFrame.Navigate(typeof(AllPlaylistsPage));
            }
        }

        private async void PersonalizedItem_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is SongItem songItem)
            {
                var index = _personalizedSongs.IndexOf(songItem);
                if (index >= 0 && index < _personalizedSongModels.Count)
                {
                    await MainWindow.PlaybackService.PlayAsync(_personalizedSongModels[index]);
                }
            }
        }

        private async void PlayAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_personalizedSongModels.Count > 0)
            {
                await MainWindow.PlaybackService.PlayAsync(_personalizedSongModels, 0);
            }
        }

        // 推荐歌单左右箭头
        private void PlaylistLeftButton_Click(object sender, RoutedEventArgs e)
        {
            PlaylistScroller.ChangeView(PlaylistScroller.HorizontalOffset - 300, null, null);
        }

        private void PlaylistRightButton_Click(object sender, RoutedEventArgs e)
        {
            PlaylistScroller.ChangeView(PlaylistScroller.HorizontalOffset + 300, null, null);
        }

        private void PlaylistScroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            PlaylistLeftButton.Visibility = PlaylistScroller.HorizontalOffset > 0 ? Visibility.Visible : Visibility.Collapsed;
            PlaylistRightButton.Visibility = PlaylistScroller.HorizontalOffset < PlaylistScroller.ScrollableWidth ? Visibility.Visible : Visibility.Collapsed;
        }

        // 精选歌曲左右箭头
        private void PersonalizedLeftButton_Click(object sender, RoutedEventArgs e)
        {
            PersonalizedScroller.ChangeView(PersonalizedScroller.HorizontalOffset - 300, null, null);
        }

        private void PersonalizedRightButton_Click(object sender, RoutedEventArgs e)
        {
            PersonalizedScroller.ChangeView(PersonalizedScroller.HorizontalOffset + 300, null, null);
        }

        private void PersonalizedScroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            PersonalizedLeftButton.Visibility = PersonalizedScroller.HorizontalOffset > 0 ? Visibility.Visible : Visibility.Collapsed;
            PersonalizedRightButton.Visibility = PersonalizedScroller.HorizontalOffset < PersonalizedScroller.ScrollableWidth ? Visibility.Visible : Visibility.Collapsed;
        }

        // 以 handledEventsToo=true 注册：内层横向 ScrollViewer 会在其内部类处理器中
        // 先“吞掉”垂直滚轮事件（即使它没有可垂直滚动的内容），导致普通 PointerWheelChanged
        // 处理器收不到该事件。这里强制接收并把垂直滚动转发给外层页面滚动容器 ContentPanel，
        // 从而让指针悬停在内容板块上时仍能正常纵向滚动整页。
        private void RegisterWheelForwarding()
        {
            var inners = new[]
            {
                PlaylistScroller, PersonalizedScroller, DailySongsScroller,
                GuessScroller, IntelligenceScroller, RadarScroller
            };
            foreach (var sv in inners)
            {
                sv.AddHandler(UIElement.PointerWheelChangedEvent,
                    new Microsoft.UI.Xaml.Input.PointerEventHandler(InnerScroller_ForwardVerticalWheel), true);
            }
        }

        private void InnerScroller_ForwardVerticalWheel(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is not ScrollViewer inner) return;
            var props = e.GetCurrentPoint(inner).Properties;
            if (props.IsHorizontalMouseWheel) return; // 横向（Shift+滚轮）交给 HandleNestedWheel
            ContentPanel.ChangeView(null, ContentPanel.VerticalOffset - props.MouseWheelDelta, null);
            e.Handled = true;
        }

        // Shift + 滚轮：内层横向滚动（垂直滚动统一由 InnerScroller_ForwardVerticalWheel 处理）
        private void HandleNestedWheel(ScrollViewer inner, ScrollViewer outer, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            var props = e.GetCurrentPoint(inner).Properties;
            if (props.IsHorizontalMouseWheel)
            {
                inner.ChangeView(inner.HorizontalOffset - props.MouseWheelDelta, null, null);
                e.Handled = true;
            }
        }

        private void PlaylistScroller_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            HandleNestedWheel(PlaylistScroller, ContentPanel, e);
        }

        private void PersonalizedScroller_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            HandleNestedWheel(PersonalizedScroller, ContentPanel, e);
        }

        // 每日推荐歌曲卡片
        private Border CreateDailySongCard(SongItem song, int index)
        {
            var border = new Border
            {
                Width = double.NaN,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 8),
                Tag = index
            };
            border.PointerPressed += DailySongCard_PointerPressed;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });

            // 封面
            var coverBorder = new Border
            {
                Width = 56,
                Height = 56,
                CornerRadius = new CornerRadius(4),
                VerticalAlignment = VerticalAlignment.Center
            };

            var coverGrid = new Grid();
            var coverIcon = new FontIcon
            {
                Glyph = "\uE8D6",
                FontSize = 24,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            coverGrid.Children.Add(coverIcon);

            if (!string.IsNullOrEmpty(song.CoverUrl))
            {
                var coverImage = new Image
                {
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                    Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new System.Uri(song.CoverUrl))
                };
                coverGrid.Children.Add(coverImage);
            }

            coverBorder.Child = coverGrid;
            Grid.SetColumn(coverBorder, 0);
            grid.Children.Add(coverBorder);

            // 歌曲信息
            var infoPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
                Spacing = 2
            };

            var namePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };

            var nameText = new TextBlock
            {
                Text = song.Name,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                MaxLines = 1,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _dailySongNameTexts.Add(nameText);
            namePanel.Children.Add(nameText);

            if (song.IsVip)
            {
                var vipBadge = new Border
                {
                    Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(4, 1, 4, 1),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var vipText = new TextBlock
                {
                    Text = "VIP",
                    FontSize = 9,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White)
                };
                vipBadge.Child = vipText;
                namePanel.Children.Add(vipBadge);
            }

            infoPanel.Children.Add(namePanel);

            var artistText = new TextBlock
            {
                Text = song.ArtistNames,
                FontSize = 11,
                MaxLines = 1,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _dailySongArtistTexts.Add(artistText);
            infoPanel.Children.Add(artistText);

            Grid.SetColumn(infoPanel, 1);
            grid.Children.Add(infoPanel);

            // 时长
            var durationText = new TextBlock
            {
                Text = song.DurationFormatted,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            _dailySongDurationTexts.Add(durationText);
            Grid.SetColumn(durationText, 2);
            grid.Children.Add(durationText);

            border.Child = grid;
            return border;
        }

        private async void DailySongCard_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Border border && border.Tag is int index)
            {
                if (index >= 0 && index < _songModels.Count)
                {
                    await MainWindow.PlaybackService.PlayAsync(_songModels[index]);
                }
            }
        }

        // 每日推荐歌曲左右箭头
        private void DailySongsLeftButton_Click(object sender, RoutedEventArgs e)
        {
            DailySongsScroller.ChangeView(DailySongsScroller.HorizontalOffset - 300, null, null);
        }

        private void DailySongsRightButton_Click(object sender, RoutedEventArgs e)
        {
            DailySongsScroller.ChangeView(DailySongsScroller.HorizontalOffset + 300, null, null);
        }

        private void DailySongsScroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            DailySongsLeftButton.Visibility = DailySongsScroller.HorizontalOffset > 0 ? Visibility.Visible : Visibility.Collapsed;
            DailySongsRightButton.Visibility = DailySongsScroller.HorizontalOffset < DailySongsScroller.ScrollableWidth ? Visibility.Visible : Visibility.Collapsed;
        }

        private void DailySongsScroller_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            HandleNestedWheel(DailySongsScroller, ContentPanel, e);
        }

        // 猜你喜欢的歌卡片
        private Border CreateGuessSongCard(SongItem song, int index)
        {
            var border = new Border
            {
                Width = double.NaN,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 8),
                Tag = index
            };
            border.PointerPressed += GuessSongCard_PointerPressed;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });

            var coverBorder = new Border
            {
                Width = 56,
                Height = 56,
                CornerRadius = new CornerRadius(4),
                VerticalAlignment = VerticalAlignment.Center
            };

            var coverGrid = new Grid();
            var coverIcon = new FontIcon
            {
                Glyph = "\uE8D6",
                FontSize = 24,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            coverGrid.Children.Add(coverIcon);

            if (!string.IsNullOrEmpty(song.CoverUrl))
            {
                var coverImage = new Image
                {
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                    Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new System.Uri(song.CoverUrl))
                };
                coverGrid.Children.Add(coverImage);
            }

            coverBorder.Child = coverGrid;
            Grid.SetColumn(coverBorder, 0);
            grid.Children.Add(coverBorder);

            var infoPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
                Spacing = 2
            };

            var namePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            var nameText = new TextBlock
            {
                Text = song.Name,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                MaxLines = 1,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _guessNameTexts.Add(nameText);
            namePanel.Children.Add(nameText);

            if (song.IsVip)
            {
                var vipBadge = new Border
                {
                    Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(4, 1, 4, 1),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var vipText = new TextBlock
                {
                    Text = "VIP",
                    FontSize = 9,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White)
                };
                vipBadge.Child = vipText;
                namePanel.Children.Add(vipBadge);
            }

            infoPanel.Children.Add(namePanel);

            var artistText = new TextBlock
            {
                Text = song.ArtistNames,
                FontSize = 11,
                MaxLines = 1,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _guessArtistTexts.Add(artistText);
            infoPanel.Children.Add(artistText);

            Grid.SetColumn(infoPanel, 1);
            grid.Children.Add(infoPanel);

            var durationText = new TextBlock
            {
                Text = song.DurationFormatted,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            _guessDurationTexts.Add(durationText);
            Grid.SetColumn(durationText, 2);
            grid.Children.Add(durationText);

            border.Child = grid;
            return border;
        }

        private async void GuessSongCard_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Border border && border.Tag is int index)
            {
                if (index >= 0 && index < _guessModels.Count)
                {
                    await MainWindow.PlaybackService.PlayAsync(_guessModels[index]);
                }
            }
        }

        // 猜你喜欢的歌左右箭头
        private void GuessLeftButton_Click(object sender, RoutedEventArgs e)
        {
            GuessScroller.ChangeView(GuessScroller.HorizontalOffset - 300, null, null);
        }

        private void GuessRightButton_Click(object sender, RoutedEventArgs e)
        {
            GuessScroller.ChangeView(GuessScroller.HorizontalOffset + 300, null, null);
        }

        private void GuessScroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            GuessLeftButton.Visibility = GuessScroller.HorizontalOffset > 0 ? Visibility.Visible : Visibility.Collapsed;
            GuessRightButton.Visibility = GuessScroller.HorizontalOffset < GuessScroller.ScrollableWidth ? Visibility.Visible : Visibility.Collapsed;
        }

        private void GuessScroller_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            HandleNestedWheel(GuessScroller, ContentPanel, e);
        }

        // 你的红心歌曲和相似推荐左右箭头
        private void IntelligenceLeftButton_Click(object sender, RoutedEventArgs e)
        {
            IntelligenceScroller.ChangeView(IntelligenceScroller.HorizontalOffset - 300, null, null);
        }

        private void IntelligenceRightButton_Click(object sender, RoutedEventArgs e)
        {
            IntelligenceScroller.ChangeView(IntelligenceScroller.HorizontalOffset + 300, null, null);
        }

        private void IntelligenceScroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            IntelligenceLeftButton.Visibility = IntelligenceScroller.HorizontalOffset > 0 ? Visibility.Visible : Visibility.Collapsed;
            IntelligenceRightButton.Visibility = IntelligenceScroller.HorizontalOffset < IntelligenceScroller.ScrollableWidth ? Visibility.Visible : Visibility.Collapsed;
        }

        private void IntelligenceScroller_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            HandleNestedWheel(IntelligenceScroller, ContentPanel, e);
        }

        // 你的雷达歌单左右箭头
        private void RadarLeftButton_Click(object sender, RoutedEventArgs e)
        {
            RadarScroller.ChangeView(RadarScroller.HorizontalOffset - 300, null, null);
        }

        private void RadarRightButton_Click(object sender, RoutedEventArgs e)
        {
            RadarScroller.ChangeView(RadarScroller.HorizontalOffset + 300, null, null);
        }

        private void RadarScroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            RadarLeftButton.Visibility = RadarScroller.HorizontalOffset > 0 ? Visibility.Visible : Visibility.Collapsed;
            RadarRightButton.Visibility = RadarScroller.HorizontalOffset < RadarScroller.ScrollableWidth ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RadarScroller_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            HandleNestedWheel(RadarScroller, ContentPanel, e);
        }

        private void RadarItem_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is PlaylistItem playlistItem)
            {
                var mainWindow = App.m_window as MainWindow;
                if (mainWindow != null)
                {
                    mainWindow.MainContentFrame.Navigate(typeof(PlaylistDetailPage), playlistItem.Id);
                }
            }
        }

        // 红心歌曲和相似推荐卡片
        private Border CreateIntelligenceSongCard(SongItem song, int index)
        {
            var border = new Border
            {
                Width = double.NaN,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 8),
                Tag = index
            };
            border.PointerPressed += IntelligenceSongCard_PointerPressed;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });

            var coverBorder = new Border
            {
                Width = 56,
                Height = 56,
                CornerRadius = new CornerRadius(4),
                VerticalAlignment = VerticalAlignment.Center
            };

            var coverGrid = new Grid();
            var coverIcon = new FontIcon
            {
                Glyph = "\uE8D6",
                FontSize = 24,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            coverGrid.Children.Add(coverIcon);

            if (!string.IsNullOrEmpty(song.CoverUrl))
            {
                var coverImage = new Image
                {
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                    Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new System.Uri(song.CoverUrl))
                };
                coverGrid.Children.Add(coverImage);
            }

            coverBorder.Child = coverGrid;
            Grid.SetColumn(coverBorder, 0);
            grid.Children.Add(coverBorder);

            var infoPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
                Spacing = 2
            };

            var namePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            var nameText = new TextBlock
            {
                Text = song.Name,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                MaxLines = 1,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _intelligenceNameTexts.Add(nameText);
            namePanel.Children.Add(nameText);

            if (song.IsVip)
            {
                var vipBadge = new Border
                {
                    Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(4, 1, 4, 1),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var vipText = new TextBlock
                {
                    Text = "VIP",
                    FontSize = 9,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White)
                };
                vipBadge.Child = vipText;
                namePanel.Children.Add(vipBadge);
            }

            infoPanel.Children.Add(namePanel);

            var artistText = new TextBlock
            {
                Text = song.ArtistNames,
                FontSize = 11,
                MaxLines = 1,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _intelligenceArtistTexts.Add(artistText);
            infoPanel.Children.Add(artistText);

            Grid.SetColumn(infoPanel, 1);
            grid.Children.Add(infoPanel);

            var durationText = new TextBlock
            {
                Text = song.DurationFormatted,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            _intelligenceDurationTexts.Add(durationText);
            Grid.SetColumn(durationText, 2);
            grid.Children.Add(durationText);

            border.Child = grid;
            return border;
        }

        private async void IntelligenceSongCard_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Border border && border.Tag is int index)
            {
                if (index >= 0 && index < _intelligenceModels.Count)
                {
                    await MainWindow.PlaybackService.PlayAsync(_intelligenceModels[index]);
                }
            }
        }

        // 查看全部按钮点击事件
        private void ViewAllDailySongsButton_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = App.m_window as MainWindow;
            if (mainWindow != null)
            {
                mainWindow.MainContentFrame.Navigate(typeof(RecommendDailySongsPage), "daily");
            }
        }

        private void ViewAllIntelligenceButton_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = App.m_window as MainWindow;
            if (mainWindow != null)
            {
                mainWindow.MainContentFrame.Navigate(typeof(RecommendDailySongsPage), "intelligence");
            }
        }

        private void ViewAllRadarButton_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = App.m_window as MainWindow;
            if (mainWindow != null)
            {
                mainWindow.MainContentFrame.Navigate(typeof(RecommendRadarPage));
            }
        }

        private void ViewAllGuessButton_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = App.m_window as MainWindow;
            if (mainWindow != null)
            {
                mainWindow.MainContentFrame.Navigate(typeof(RecommendDailySongsPage), "guess");
            }
        }
    }
}