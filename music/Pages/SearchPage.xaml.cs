using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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

        public SearchPage()
        {
            this.InitializeComponent();
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
                var songsTask = App.ApiService.SearchSongsAsync(keywords, 6);
                var playlistsTask = App.ApiService.SearchPlaylistsAsync(keywords, 10);
                var albumsTask = App.ApiService.SearchAlbumsAsync(keywords, 10);

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

                // 更新单曲 (3x2 矩阵)
                _songModels = songs;
                _songs.Clear();
                SongsGrid.Children.Clear();
                SongsGrid.RowDefinitions.Clear();
                SongsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                SongsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                for (int i = 0; i < songs.Count && i < 6; i++)
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

                    var row = i / 3;
                    var col = i % 3;

                    var card = CreateSongCard(songItem, i);
                    Grid.SetRow(card, row);
                    Grid.SetColumn(card, col);
                    SongsGrid.Children.Add(card);
                }
                SongsSection.Visibility = songs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

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
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 8),
                Tag = index
            };
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
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                MaxLines = 1,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            infoPanel.Children.Add(artistText);

            Grid.SetColumn(infoPanel, 1);
            grid.Children.Add(infoPanel);

            // 时长
            var durationText = new TextBlock
            {
                Text = song.DurationFormatted,
                FontSize = 11,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
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

        private async void PlayAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_songModels.Count > 0)
            {
                await MainWindow.PlaybackService.PlayAsync(_songModels, 0);
            }
        }

        private void ArtistItem_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is ArtistItem artist)
            {
                var mainWindow = App.m_window as MainWindow;
                if (mainWindow != null)
                {
                    mainWindow.MainContentFrame.Navigate(typeof(PlaylistDetailPage), $"artist_{artist.Id}");
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
                var delta = e.GetCurrentPoint(scroller).Properties.MouseWheelDelta;
                scroller.ChangeView(scroller.HorizontalOffset - delta, null, null);
                e.Handled = true;
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
                var delta = e.GetCurrentPoint(scroller).Properties.MouseWheelDelta;
                scroller.ChangeView(scroller.HorizontalOffset - delta, null, null);
                e.Handled = true;
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
                var delta = e.GetCurrentPoint(scroller).Properties.MouseWheelDelta;
                scroller.ChangeView(scroller.HorizontalOffset - delta, null, null);
                e.Handled = true;
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
