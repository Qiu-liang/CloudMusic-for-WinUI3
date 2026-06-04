using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using music.Models;
using music.Services;

namespace music.Pages
{
    public sealed partial class SearchPage : Page
    {
        private readonly ObservableCollection<SongItem> _songs = new();
        private readonly ObservableCollection<ArtistItem> _artists = new();
        private readonly ObservableCollection<PlaylistItem> _playlists = new();
        private readonly ObservableCollection<AlbumItem> _albums = new();
        private List<Song> _songModels = new();
        private string _currentQuery = string.Empty;
        private readonly List<Border> _songCardBorders = new();
        private readonly List<TextBlock> _songNameTexts = new();
        private readonly List<TextBlock> _songArtistTexts = new();
        private readonly List<TextBlock> _songDurationTexts = new();

        public SearchPage()
        {
            this.InitializeComponent();
            this.ActualThemeChanged += SearchPage_ActualThemeChanged;
        }

        private void SearchPage_ActualThemeChanged(FrameworkElement sender, object args)
        {
            RefreshSongCardBackgrounds();
        }

        private void RefreshSongCardBackgrounds()
        {
            var isDark = this.ActualTheme == ElementTheme.Dark;
            var primaryBrush = isDark
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0));
            var secondaryBrush = isDark
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 153, 153, 153))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 102, 102, 102));

            for (int i = 0; i < _songCardBorders.Count; i++)
            {
                ApplyCardBackground(_songCardBorders[i]);
                if (i < _songNameTexts.Count) _songNameTexts[i].Foreground = primaryBrush;
                if (i < _songArtistTexts.Count) _songArtistTexts[i].Foreground = secondaryBrush;
                if (i < _songDurationTexts.Count) _songDurationTexts[i].Foreground = secondaryBrush;
            }
        }

        private void ApplyCardBackground(Border border)
        {
            if (this.ActualTheme == ElementTheme.Dark)
            {
                border.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 39, 39, 39));
            }
            else
            {
                border.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 249, 249, 249));
            }
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is string query && !string.IsNullOrWhiteSpace(query))
            {
                _currentQuery = query;
                TitleText.Text = $"搜索: {query}";
                await SearchAllAsync(query);
            }
            else
            {
                ShowEmptyState();
            }
        }

        private async System.Threading.Tasks.Task SearchAllAsync(string keywords)
        {
            ShowLoading();

            try
            {
                var artistsTask = App.ApiService.SearchArtistsAsync(keywords, 10);
                var songsTask = App.ApiService.SearchSongsAsync(keywords, 39);
                var playlistsTask = App.ApiService.SearchPlaylistsAsync(keywords, 20);
                var albumsTask = App.ApiService.SearchAlbumsAsync(keywords, 20);

                await System.Threading.Tasks.Task.WhenAll(artistsTask, songsTask, playlistsTask, albumsTask);

                var artists = artistsTask.Result;
                var songs = songsTask.Result;
                var playlists = playlistsTask.Result;
                var albums = albumsTask.Result;

                // 更新歌手
                _artists.Clear();
                foreach (var artist in artists)
                {
                    _artists.Add(new ArtistItem
                    {
                        Id = artist.Id,
                        Name = artist.Name,
                        PicUrl = artist.PicUrl,
                        SongCount = artist.SongCount
                    });
                }
                ArtistsItems.ItemsSource = _artists;
                ArtistsSection.Visibility = _artists.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

                // 更新单曲 (每列3首，横向排列)
                _songModels = songs;
                _songs.Clear();
                SongsContainer.Children.Clear();
                _songCardBorders.Clear();
                _songNameTexts.Clear();
                _songArtistTexts.Clear();
                _songDurationTexts.Clear();

                for (int i = 0; i < songs.Count && i < 39; i++)
                {
                    var song = songs[i];
                    var songItem = new SongItem
                    {
                        Id = song.Id,
                        Name = song.Name,
                        ArtistNames = song.ArtistNames,
                        AlbumName = song.Album.Name,
                        CoverUrl = song.CoverImgUrl,
                        DurationFormatted = song.DurationFormatted,
                        IsLiked = song.Liked,
                        CanPlay = song.CanPlay,
                        Fee = song.Fee,
                        IsVip = song.IsVip,
                        IsPaid = song.IsPaid,
                        FeeText = song.FeeText
                    };
                    _songs.Add(songItem);
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
                            var card = CreateSongCard(_songs[index], index);
                            _songCardBorders.Add(card);
                            column.Children.Add(card);
                        }
                    }
                    SongsContainer.Children.Add(column);
                }
                SongsSection.Visibility = songs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                RefreshSongCardBackgrounds();

                // 更新歌单
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
                PlaylistsItems.ItemsSource = _playlists;
                PlaylistsSection.Visibility = _playlists.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

                // 更新专辑
                _albums.Clear();
                foreach (var album in albums)
                {
                    _albums.Add(new AlbumItem
                    {
                        Id = album.Id,
                        Name = album.Name,
                        PicUrl = album.PicUrl,
                        ArtistName = album.ArtistName,
                        PublishTime = album.PublishTime,
                        Size = album.Size
                    });
                }
                AlbumsItems.ItemsSource = _albums;
                AlbumsSection.Visibility = _albums.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

                if (_artists.Count == 0 && _songs.Count == 0 && _playlists.Count == 0 && _albums.Count == 0)
                {
                    ShowEmptyState();
                    TitleText.Text = $"未找到 \"{keywords}\" 的结果";
                }
                else
                {
                    ShowResults();
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Search] Error: {ex.Message}");
                ShowEmptyState();
                TitleText.Text = $"搜索 \"{keywords}\" 失败";
            }
        }

        private Border CreateSongCard(SongItem song, int index)
        {
            var border = new Border
            {
                Width = double.NaN,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 8),
                Tag = index
            };
            ApplyCardBackground(border);
            border.PointerPressed += SongCard_PointerPressed;

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
            _songNameTexts.Add(nameText);
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
            _songArtistTexts.Add(artistText);
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
            _songDurationTexts.Add(durationText);
            Grid.SetColumn(durationText, 2);
            grid.Children.Add(durationText);

            border.Child = grid;
            return border;
        }

        private void ShowEmptyState()
        {
            EmptyState.Visibility = Visibility.Visible;
            LoadingPanel.Visibility = Visibility.Collapsed;
            ResultsPanel.Visibility = Visibility.Collapsed;
        }

        private void ShowLoading()
        {
            EmptyState.Visibility = Visibility.Collapsed;
            LoadingPanel.Visibility = Visibility.Visible;
            ResultsPanel.Visibility = Visibility.Collapsed;
        }

        private void ShowResults()
        {
            EmptyState.Visibility = Visibility.Collapsed;
            LoadingPanel.Visibility = Visibility.Collapsed;
            ResultsPanel.Visibility = Visibility.Visible;
        }

        private async void SongCard_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border border && border.Tag is int index)
            {
                if (index >= 0 && index < _songModels.Count)
                {
                    await MainWindow.PlaybackService.PlayAsync(_songModels[index]);
                }
            }
        }

        private void ViewAllSongsButton_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = App.m_window as MainWindow;
            if (mainWindow != null)
            {
                mainWindow.MainContentFrame.Navigate(typeof(SearchAllSongsPage), _currentQuery);
            }
        }

        private void ArtistItem_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is ArtistItem artist)
            {
                var mainWindow = App.m_window as MainWindow;
                if (mainWindow != null)
                {
                    mainWindow.MainContentFrame.Navigate(typeof(ArtistDetailPage), artist.Id);
                }
            }
        }

        private void PlaylistItem_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is PlaylistItem playlist)
            {
                var mainWindow = App.m_window as MainWindow;
                if (mainWindow != null)
                {
                    mainWindow.MainContentFrame.Navigate(typeof(PlaylistDetailPage), playlist.Id);
                }
            }
        }

        private void AlbumItem_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is AlbumItem album)
            {
                var mainWindow = App.m_window as MainWindow;
                if (mainWindow != null)
                {
                    mainWindow.MainContentFrame.Navigate(typeof(PlaylistDetailPage), $"album_{album.Id}");
                }
            }
        }

        // 相关歌手左右箭头
        private void ArtistsLeftButton_Click(object sender, RoutedEventArgs e)
        {
            ArtistsScroller.ChangeView(ArtistsScroller.HorizontalOffset - 300, null, null);
        }

        private void ArtistsRightButton_Click(object sender, RoutedEventArgs e)
        {
            ArtistsScroller.ChangeView(ArtistsScroller.HorizontalOffset + 300, null, null);
        }

        private void ArtistsScroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            ArtistsLeftButton.Visibility = ArtistsScroller.HorizontalOffset > 0 ? Visibility.Visible : Visibility.Collapsed;
            ArtistsRightButton.Visibility = ArtistsScroller.HorizontalOffset < ArtistsScroller.ScrollableWidth ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ArtistsScroller_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var scroller = sender as ScrollViewer;
            if (scroller != null)
            {
                var properties = e.GetCurrentPoint(scroller).Properties;
                if (properties.IsHorizontalMouseWheel)
                {
                    var delta = properties.MouseWheelDelta;
                    scroller.ChangeView(scroller.HorizontalOffset - delta, null, null);
                    e.Handled = true;
                }
            }
        }

        // 相关单曲左右箭头
        private void SongsLeftButton_Click(object sender, RoutedEventArgs e)
        {
            SongsScroller.ChangeView(SongsScroller.HorizontalOffset - 300, null, null);
        }

        private void SongsRightButton_Click(object sender, RoutedEventArgs e)
        {
            SongsScroller.ChangeView(SongsScroller.HorizontalOffset + 300, null, null);
        }

        private void SongsScroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            SongsLeftButton.Visibility = SongsScroller.HorizontalOffset > 0 ? Visibility.Visible : Visibility.Collapsed;
            SongsRightButton.Visibility = SongsScroller.HorizontalOffset < SongsScroller.ScrollableWidth ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SongsScroller_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var scroller = sender as ScrollViewer;
            if (scroller != null)
            {
                var properties = e.GetCurrentPoint(scroller).Properties;
                if (properties.IsHorizontalMouseWheel)
                {
                    var delta = properties.MouseWheelDelta;
                    scroller.ChangeView(scroller.HorizontalOffset - delta, null, null);
                    e.Handled = true;
                }
            }
        }

        // 相关歌单左右箭头
        private void PlaylistsLeftButton_Click(object sender, RoutedEventArgs e)
        {
            PlaylistsScroller.ChangeView(PlaylistsScroller.HorizontalOffset - 300, null, null);
        }

        private void PlaylistsRightButton_Click(object sender, RoutedEventArgs e)
        {
            PlaylistsScroller.ChangeView(PlaylistsScroller.HorizontalOffset + 300, null, null);
        }

        private void PlaylistsScroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            PlaylistsLeftButton.Visibility = PlaylistsScroller.HorizontalOffset > 0 ? Visibility.Visible : Visibility.Collapsed;
            PlaylistsRightButton.Visibility = PlaylistsScroller.HorizontalOffset < PlaylistsScroller.ScrollableWidth ? Visibility.Visible : Visibility.Collapsed;
        }

        private void PlaylistsScroller_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var scroller = sender as ScrollViewer;
            if (scroller != null)
            {
                var properties = e.GetCurrentPoint(scroller).Properties;
                if (properties.IsHorizontalMouseWheel)
                {
                    var delta = properties.MouseWheelDelta;
                    scroller.ChangeView(scroller.HorizontalOffset - delta, null, null);
                    e.Handled = true;
                }
            }
        }

        // 相关专辑左右箭头
        private void AlbumsLeftButton_Click(object sender, RoutedEventArgs e)
        {
            AlbumsScroller.ChangeView(AlbumsScroller.HorizontalOffset - 300, null, null);
        }

        private void AlbumsRightButton_Click(object sender, RoutedEventArgs e)
        {
            AlbumsScroller.ChangeView(AlbumsScroller.HorizontalOffset + 300, null, null);
        }

        private void AlbumsScroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            AlbumsLeftButton.Visibility = AlbumsScroller.HorizontalOffset > 0 ? Visibility.Visible : Visibility.Collapsed;
            AlbumsRightButton.Visibility = AlbumsScroller.HorizontalOffset < AlbumsScroller.ScrollableWidth ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AlbumsScroller_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var scroller = sender as ScrollViewer;
            if (scroller != null)
            {
                var properties = e.GetCurrentPoint(scroller).Properties;
                if (properties.IsHorizontalMouseWheel)
                {
                    var delta = properties.MouseWheelDelta;
                    scroller.ChangeView(scroller.HorizontalOffset - delta, null, null);
                    e.Handled = true;
                }
            }
        }

        private void ViewAllPlaylistsButton_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = App.m_window as MainWindow;
            if (mainWindow != null)
            {
                mainWindow.MainContentFrame.Navigate(typeof(SearchAllResultsPage), new SearchAllResultsParams
                {
                    Type = "playlist",
                    Keywords = _currentQuery
                });
            }
        }

        private void ViewAllAlbumsButton_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = App.m_window as MainWindow;
            if (mainWindow != null)
            {
                mainWindow.MainContentFrame.Navigate(typeof(SearchAllResultsPage), new SearchAllResultsParams
                {
                    Type = "album",
                    Keywords = _currentQuery
                });
            }
        }
    }
}
