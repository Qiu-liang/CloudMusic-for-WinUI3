using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using music.Core.Abstractions;
using music.Core.Mvvm;
using music.Core.ViewModels;
using music.Core.ViewModels.Items;

namespace music.Pages
{
    public sealed partial class SearchPage : Page
    {
        public SearchViewModel? ViewModel { get; private set; }
        private readonly List<Border> _songCardBorders = new();
        private readonly List<TextBlock> _songNameTexts = new();
        private readonly List<TextBlock> _songArtistTexts = new();
        private readonly List<TextBlock> _songDurationTexts = new();
        private double _wheelTargetOffset = double.NaN;

        public SearchPage()
        {
            InitializeComponent();
            RegisterWheelForwarding();
            ActualThemeChanged += (_, _) => RefreshSongCardBackgrounds();
        }

        private void RefreshSongCardBackgrounds()
        {
            var isDark = ActualTheme == ElementTheme.Dark;
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
            border.Background = ActualTheme == ElementTheme.Dark
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 39, 39, 39))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 249, 249, 249));
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is NavigationRequest request)
            {
                DetachViewModel();
                ViewModel = (SearchViewModel)request.CreateViewModel();
                ViewModel.PropertyChanged += ViewModel_PropertyChanged;

                var query = request.Parameter as string;
                await ViewModel.LoadAsync(query);
                SyncViewFromViewModel();
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            DetachViewModel();
        }

        private void DetachViewModel()
        {
            if (ViewModel != null)
            {
                ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
                ViewModel.Dispose();
                ViewModel = null;
            }
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(SearchViewModel.State) or nameof(SearchViewModel.Title))
            {
                SyncViewFromViewModel();
            }
        }

        private void SyncViewFromViewModel()
        {
            if (ViewModel == null) return;

            TitleText.Text = ViewModel.Title;

            if (ViewModel.IsLoading)
            {
                ShowLoading();
                return;
            }

            if (ViewModel.IsEmpty)
            {
                ShowEmptyState();
                return;
            }

            ArtistsItems.ItemsSource = ViewModel.Artists;
            ArtistsSection.Visibility = ViewModel.HasArtists ? Visibility.Visible : Visibility.Collapsed;

            RebuildSongCards();
            SongsSection.Visibility = ViewModel.HasSongs ? Visibility.Visible : Visibility.Collapsed;

            PlaylistsItems.ItemsSource = ViewModel.Playlists;
            PlaylistsSection.Visibility = ViewModel.HasPlaylists ? Visibility.Visible : Visibility.Collapsed;

            AlbumsItems.ItemsSource = ViewModel.Albums;
            AlbumsSection.Visibility = ViewModel.HasAlbums ? Visibility.Visible : Visibility.Collapsed;

            ShowResults();
        }

        private void RebuildSongCards()
        {
            if (ViewModel == null) return;

            SongsContainer.Children.Clear();
            _songCardBorders.Clear();
            _songNameTexts.Clear();
            _songArtistTexts.Clear();
            _songDurationTexts.Clear();

            for (int col = 0; col < (ViewModel.Songs.Count + 2) / 3; col++)
            {
                var column = new StackPanel { Spacing = 8, Width = 300 };
                for (int row = 0; row < 3; row++)
                {
                    int index = col * 3 + row;
                    if (index < ViewModel.Songs.Count)
                    {
                        var card = CreateSongCard(ViewModel.Songs[index], index);
                        _songCardBorders.Add(card);
                        column.Children.Add(card);
                    }
                }
                SongsContainer.Children.Add(column);
            }

            RefreshSongCardBackgrounds();
        }

        private Border CreateSongCard(music.Core.ViewModels.Items.SongItem song, int index)
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

            var coverBorder = new Border
            {
                Width = 56,
                Height = 56,
                CornerRadius = new CornerRadius(4),
                VerticalAlignment = VerticalAlignment.Center
            };

            var coverGrid = new Grid();
            coverGrid.Children.Add(new FontIcon
            {
                Glyph = "\uE8D6",
                FontSize = 24,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });

            if (!string.IsNullOrEmpty(song.CoverUrl))
            {
                coverGrid.Children.Add(new Image
                {
                    Stretch = Stretch.UniformToFill,
                    Source = new BitmapImage(new Uri(song.CoverUrl))
                });
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
            _songNameTexts.Add(nameText);
            namePanel.Children.Add(nameText);

            if (song.IsVip)
            {
                var vipBadge = new Border
                {
                    Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(4, 1, 4, 1),
                    VerticalAlignment = VerticalAlignment.Center
                };
                vipBadge.Child = new TextBlock
                {
                    Text = "VIP",
                    FontSize = 9,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
                };
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

        private void SongCard_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border border && border.Tag is int index && ViewModel != null)
            {
                ViewModel.PlaySongAtAsync(index).Forget();
            }
        }

        private void ViewAllSongsButton_Click(object sender, RoutedEventArgs e) => ViewModel?.OpenAllSongs();

        private void ArtistItem_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is music.Core.ViewModels.Items.ArtistItem artist)
                ViewModel?.OpenArtist(artist);
        }

        private void PlaylistItem_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is music.Core.ViewModels.Items.PlaylistItem playlist)
                ViewModel?.OpenPlaylist(playlist);
        }

        private void AlbumItem_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is music.Core.ViewModels.Items.AlbumItem album)
                ViewModel?.OpenAlbum(album);
        }

        private void ArtistsLeftButton_Click(object sender, RoutedEventArgs e) =>
            ArtistsScroller.ChangeView(ArtistsScroller.HorizontalOffset - 300, null, null);

        private void ArtistsRightButton_Click(object sender, RoutedEventArgs e) =>
            ArtistsScroller.ChangeView(ArtistsScroller.HorizontalOffset + 300, null, null);

        private void ArtistsScroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            ArtistsLeftButton.Visibility = ArtistsScroller.HorizontalOffset > 0 ? Visibility.Visible : Visibility.Collapsed;
            ArtistsRightButton.Visibility = ArtistsScroller.HorizontalOffset < ArtistsScroller.ScrollableWidth ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RegisterWheelForwarding()
        {
            var inners = new[] { ArtistsScroller, SongsScroller, PlaylistsScroller, AlbumsScroller };
            foreach (var sv in inners)
            {
                sv.AddHandler(UIElement.PointerWheelChangedEvent,
                    new PointerEventHandler(InnerScroller_ForwardVerticalWheel), true);
            }
            ResultsPanel.ViewChanged += (s, e) =>
            {
                if (!e.IsIntermediate) _wheelTargetOffset = double.NaN;
            };
        }

        private void InnerScroller_ForwardVerticalWheel(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not ScrollViewer inner) return;
            var props = e.GetCurrentPoint(inner).Properties;
            if (props.IsHorizontalMouseWheel) return;

            int delta = props.MouseWheelDelta;
            double current = ResultsPanel.VerticalOffset;

            double baseOffset;
            if (double.IsNaN(_wheelTargetOffset))
            {
                baseOffset = current;
            }
            else
            {
                bool goingDown = delta < 0;
                bool pendingDown = _wheelTargetOffset > current;
                baseOffset = (goingDown == pendingDown) ? _wheelTargetOffset : current;
            }

            double target = Math.Max(0, Math.Min(ResultsPanel.ScrollableHeight, baseOffset - delta));
            _wheelTargetOffset = target;
            ResultsPanel.ChangeView(null, target, null);
            e.Handled = true;
        }

        private void HandleNestedWheel(ScrollViewer inner, ScrollViewer outer, PointerRoutedEventArgs e)
        {
            var props = e.GetCurrentPoint(inner).Properties;
            if (props.IsHorizontalMouseWheel)
            {
                inner.ChangeView(inner.HorizontalOffset - props.MouseWheelDelta, null, null);
                e.Handled = true;
            }
        }

        private void ArtistsScroller_PointerWheelChanged(object sender, PointerRoutedEventArgs e) =>
            HandleNestedWheel(ArtistsScroller, ResultsPanel, e);

        private void SongsLeftButton_Click(object sender, RoutedEventArgs e) =>
            SongsScroller.ChangeView(SongsScroller.HorizontalOffset - 300, null, null);

        private void SongsRightButton_Click(object sender, RoutedEventArgs e) =>
            SongsScroller.ChangeView(SongsScroller.HorizontalOffset + 300, null, null);

        private void SongsScroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            SongsLeftButton.Visibility = SongsScroller.HorizontalOffset > 0 ? Visibility.Visible : Visibility.Collapsed;
            SongsRightButton.Visibility = SongsScroller.HorizontalOffset < SongsScroller.ScrollableWidth ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SongsScroller_PointerWheelChanged(object sender, PointerRoutedEventArgs e) =>
            HandleNestedWheel(SongsScroller, ResultsPanel, e);

        private void PlaylistsLeftButton_Click(object sender, RoutedEventArgs e) =>
            PlaylistsScroller.ChangeView(PlaylistsScroller.HorizontalOffset - 300, null, null);

        private void PlaylistsRightButton_Click(object sender, RoutedEventArgs e) =>
            PlaylistsScroller.ChangeView(PlaylistsScroller.HorizontalOffset + 300, null, null);

        private void PlaylistsScroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            PlaylistsLeftButton.Visibility = PlaylistsScroller.HorizontalOffset > 0 ? Visibility.Visible : Visibility.Collapsed;
            PlaylistsRightButton.Visibility = PlaylistsScroller.HorizontalOffset < PlaylistsScroller.ScrollableWidth ? Visibility.Visible : Visibility.Collapsed;
        }

        private void PlaylistsScroller_PointerWheelChanged(object sender, PointerRoutedEventArgs e) =>
            HandleNestedWheel(PlaylistsScroller, ResultsPanel, e);

        private void AlbumsLeftButton_Click(object sender, RoutedEventArgs e) =>
            AlbumsScroller.ChangeView(AlbumsScroller.HorizontalOffset - 300, null, null);

        private void AlbumsRightButton_Click(object sender, RoutedEventArgs e) =>
            AlbumsScroller.ChangeView(AlbumsScroller.HorizontalOffset + 300, null, null);

        private void AlbumsScroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            AlbumsLeftButton.Visibility = AlbumsScroller.HorizontalOffset > 0 ? Visibility.Visible : Visibility.Collapsed;
            AlbumsRightButton.Visibility = AlbumsScroller.HorizontalOffset < AlbumsScroller.ScrollableWidth ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AlbumsScroller_PointerWheelChanged(object sender, PointerRoutedEventArgs e) =>
            HandleNestedWheel(AlbumsScroller, ResultsPanel, e);

        private void ViewAllPlaylistsButton_Click(object sender, RoutedEventArgs e) => ViewModel?.OpenAllPlaylists();

        private void ViewAllAlbumsButton_Click(object sender, RoutedEventArgs e) => ViewModel?.OpenAllAlbums();
    }
}
