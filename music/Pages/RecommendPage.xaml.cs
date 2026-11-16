using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using music.Core.Abstractions;
using music.Core.Mvvm;
using music.Core.ViewModels;
using CoreItems = music.Core.ViewModels.Items;

namespace music.Pages
{
    public class TrackCountConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is int count) return $"{count}首";
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public sealed partial class RecommendPage : Page
    {
        public RecommendViewModel? ViewModel { get; private set; }

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
        private double _wheelTargetOffset = double.NaN;

        public RecommendPage()
        {
            InitializeComponent();
            RegisterWheelForwarding();
            ActualThemeChanged += (_, _) => RefreshCardBackgrounds();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is NavigationRequest request)
            {
                DetachViewModel();
                ViewModel = (RecommendViewModel)request.CreateViewModel();
                ViewModel.PropertyChanged += ViewModel_PropertyChanged;

                PlaylistItems.ItemsSource = ViewModel.Playlists;
                PersonalizedItems.ItemsSource = ViewModel.PersonalizedSongs;
                RadarItems.ItemsSource = ViewModel.RadarPlaylists;

                await ViewModel.LoadAsync();
                SyncFromViewModel();
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
            if (e.PropertyName is nameof(RecommendViewModel.State) or nameof(RecommendViewModel.ErrorMessage))
            {
                SyncFromViewModel();
            }
        }

        private void SyncFromViewModel()
        {
            if (ViewModel == null) return;

            if (ViewModel.IsLoading)
            {
                ShowLoading();
                return;
            }
            if (ViewModel.IsError)
            {
                ShowError(ViewModel.ErrorMessage);
                return;
            }

            IntelligenceSection.Visibility = ViewModel.ShowLoggedInSections ? Visibility.Visible : Visibility.Collapsed;
            RadarSection.Visibility = ViewModel.ShowLoggedInSections ? Visibility.Visible : Visibility.Collapsed;

            RebuildCards(
                ViewModel.DailySongs, DailySongsContainer,
                _dailySongCardBorders, _dailySongNameTexts, _dailySongArtistTexts, _dailySongDurationTexts,
                DailySongCard_PointerPressed);

            RebuildCards(
                ViewModel.IntelligenceSongs, IntelligenceContainer,
                _intelligenceCardBorders, _intelligenceNameTexts, _intelligenceArtistTexts, _intelligenceDurationTexts,
                IntelligenceSongCard_PointerPressed);

            RebuildCards(
                ViewModel.GuessSongs, GuessContainer,
                _guessCardBorders, _guessNameTexts, _guessArtistTexts, _guessDurationTexts,
                GuessSongCard_PointerPressed);

            RefreshCardBackgrounds();
            ShowContent();
        }

        private void RebuildCards(
            IReadOnlyList<CoreItems.SongItem> songs,
            StackPanel container,
            List<Border> cardBorders,
            List<TextBlock> nameTexts,
            List<TextBlock> artistTexts,
            List<TextBlock> durationTexts,
            PointerEventHandler onPressed)
        {
            container.Children.Clear();
            cardBorders.Clear();
            nameTexts.Clear();
            artistTexts.Clear();
            durationTexts.Clear();

            for (int col = 0; col < (songs.Count + 2) / 3; col++)
            {
                var column = new StackPanel { Spacing = 8, Width = 300 };
                for (int row = 0; row < 3; row++)
                {
                    int index = col * 3 + row;
                    if (index < songs.Count)
                    {
                        var card = CreateSongCard(songs[index], index, nameTexts, artistTexts, durationTexts, onPressed);
                        cardBorders.Add(card);
                        column.Children.Add(card);
                    }
                }
                container.Children.Add(column);
            }
        }

        private static Border CreateSongCard(
            CoreItems.SongItem song,
            int index,
            List<TextBlock> nameTexts,
            List<TextBlock> artistTexts,
            List<TextBlock> durationTexts,
            PointerEventHandler onPressed)
        {
            var border = new Border
            {
                Width = double.NaN,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 8),
                Tag = index
            };
            border.PointerPressed += onPressed;

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
            nameTexts.Add(nameText);
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
            artistTexts.Add(artistText);
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
            durationTexts.Add(durationText);
            Grid.SetColumn(durationText, 2);
            grid.Children.Add(durationText);

            border.Child = grid;
            return border;
        }

        private void RefreshCardBackgrounds()
        {
            var isDark = ActualTheme == ElementTheme.Dark;
            var cardBrush = isDark
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 39, 39, 39))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 249, 249, 249));
            var primaryBrush = isDark
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0));
            var secondaryBrush = isDark
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 153, 153, 153))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 102, 102, 102));

            ApplySectionTheme(_dailySongCardBorders, _dailySongNameTexts, _dailySongArtistTexts, _dailySongDurationTexts, cardBrush, primaryBrush, secondaryBrush);
            ApplySectionTheme(_intelligenceCardBorders, _intelligenceNameTexts, _intelligenceArtistTexts, _intelligenceDurationTexts, cardBrush, primaryBrush, secondaryBrush);
            ApplySectionTheme(_guessCardBorders, _guessNameTexts, _guessArtistTexts, _guessDurationTexts, cardBrush, primaryBrush, secondaryBrush);
        }

        private static void ApplySectionTheme(
            List<Border> borders,
            List<TextBlock> names,
            List<TextBlock> artists,
            List<TextBlock> durations,
            Brush cardBrush,
            Brush primaryBrush,
            Brush secondaryBrush)
        {
            for (int i = 0; i < borders.Count; i++)
            {
                borders[i].Background = cardBrush;
                if (i < names.Count) names[i].Foreground = primaryBrush;
                if (i < artists.Count) artists[i].Foreground = secondaryBrush;
                if (i < durations.Count) durations[i].Foreground = secondaryBrush;
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

        private void RefreshButton_Click(object sender, RoutedEventArgs e) => ViewModel?.LoadAsync().Forget();

        private void SongsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is CoreItems.SongItem songItem)
                ViewModel?.PlayDailyListItemAsync(songItem).Forget();
        }

        private void PlaylistItem_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is CoreItems.PlaylistItem playlistItem)
                ViewModel?.OpenPlaylist(playlistItem);
        }

        private void ViewAllPlaylistsButton_Click(object sender, RoutedEventArgs e) => ViewModel?.OpenAllPlaylists();

        private void PersonalizedItem_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is CoreItems.SongItem songItem)
                ViewModel?.PlayPersonalizedItemAsync(songItem).Forget();
        }

        private void PlayAllButton_Click(object sender, RoutedEventArgs e) => ViewModel?.PlayAllPersonalizedCommand.Execute(null);

        private void PlaylistLeftButton_Click(object sender, RoutedEventArgs e) =>
            PlaylistScroller.ChangeView(PlaylistScroller.HorizontalOffset - 300, null, null);
        private void PlaylistRightButton_Click(object sender, RoutedEventArgs e) =>
            PlaylistScroller.ChangeView(PlaylistScroller.HorizontalOffset + 300, null, null);
        private void PlaylistScroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            PlaylistLeftButton.Visibility = PlaylistScroller.HorizontalOffset > 0 ? Visibility.Visible : Visibility.Collapsed;
            PlaylistRightButton.Visibility = PlaylistScroller.HorizontalOffset < PlaylistScroller.ScrollableWidth ? Visibility.Visible : Visibility.Collapsed;
        }

        private void PersonalizedLeftButton_Click(object sender, RoutedEventArgs e) =>
            PersonalizedScroller.ChangeView(PersonalizedScroller.HorizontalOffset - 300, null, null);
        private void PersonalizedRightButton_Click(object sender, RoutedEventArgs e) =>
            PersonalizedScroller.ChangeView(PersonalizedScroller.HorizontalOffset + 300, null, null);
        private void PersonalizedScroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            PersonalizedLeftButton.Visibility = PersonalizedScroller.HorizontalOffset > 0 ? Visibility.Visible : Visibility.Collapsed;
            PersonalizedRightButton.Visibility = PersonalizedScroller.HorizontalOffset < PersonalizedScroller.ScrollableWidth ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RegisterWheelForwarding()
        {
            var inners = new[]
            {
                PlaylistScroller, PersonalizedScroller, DailySongsScroller,
                IntelligenceScroller, RadarScroller, GuessScroller
            };
            foreach (var sv in inners)
            {
                sv.AddHandler(UIElement.PointerWheelChangedEvent,
                    new PointerEventHandler(InnerScroller_ForwardVerticalWheel), true);
            }
            ContentPanel.ViewChanged += (s, e) =>
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
            double current = ContentPanel.VerticalOffset;

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

            double target = Math.Max(0, Math.Min(ContentPanel.ScrollableHeight, baseOffset - delta));
            _wheelTargetOffset = target;
            ContentPanel.ChangeView(null, target, null);
            e.Handled = true;
        }

        private static void HandleNestedWheel(ScrollViewer inner, ScrollViewer outer, PointerRoutedEventArgs e)
        {
            var props = e.GetCurrentPoint(inner).Properties;
            if (props.IsHorizontalMouseWheel)
            {
                inner.ChangeView(inner.HorizontalOffset - props.MouseWheelDelta, null, null);
                e.Handled = true;
            }
        }

        private void PlaylistScroller_PointerWheelChanged(object sender, PointerRoutedEventArgs e) =>
            HandleNestedWheel(PlaylistScroller, ContentPanel, e);
        private void PersonalizedScroller_PointerWheelChanged(object sender, PointerRoutedEventArgs e) =>
            HandleNestedWheel(PersonalizedScroller, ContentPanel, e);

        private void DailySongCard_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border border && border.Tag is int index)
                ViewModel?.PlayDailyAtAsync(index).Forget();
        }

        private void DailySongsLeftButton_Click(object sender, RoutedEventArgs e) =>
            DailySongsScroller.ChangeView(DailySongsScroller.HorizontalOffset - 300, null, null);
        private void DailySongsRightButton_Click(object sender, RoutedEventArgs e) =>
            DailySongsScroller.ChangeView(DailySongsScroller.HorizontalOffset + 300, null, null);
        private void DailySongsScroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            DailySongsLeftButton.Visibility = DailySongsScroller.HorizontalOffset > 0 ? Visibility.Visible : Visibility.Collapsed;
            DailySongsRightButton.Visibility = DailySongsScroller.HorizontalOffset < DailySongsScroller.ScrollableWidth ? Visibility.Visible : Visibility.Collapsed;
        }
        private void DailySongsScroller_PointerWheelChanged(object sender, PointerRoutedEventArgs e) =>
            HandleNestedWheel(DailySongsScroller, ContentPanel, e);

        private void GuessSongCard_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border border && border.Tag is int index)
                ViewModel?.PlayGuessAtAsync(index).Forget();
        }

        private void GuessLeftButton_Click(object sender, RoutedEventArgs e) =>
            GuessScroller.ChangeView(GuessScroller.HorizontalOffset - 300, null, null);
        private void GuessRightButton_Click(object sender, RoutedEventArgs e) =>
            GuessScroller.ChangeView(GuessScroller.HorizontalOffset + 300, null, null);
        private void GuessScroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            GuessLeftButton.Visibility = GuessScroller.HorizontalOffset > 0 ? Visibility.Visible : Visibility.Collapsed;
            GuessRightButton.Visibility = GuessScroller.HorizontalOffset < GuessScroller.ScrollableWidth ? Visibility.Visible : Visibility.Collapsed;
        }
        private void GuessScroller_PointerWheelChanged(object sender, PointerRoutedEventArgs e) =>
            HandleNestedWheel(GuessScroller, ContentPanel, e);

        private void IntelligenceLeftButton_Click(object sender, RoutedEventArgs e) =>
            IntelligenceScroller.ChangeView(IntelligenceScroller.HorizontalOffset - 300, null, null);
        private void IntelligenceRightButton_Click(object sender, RoutedEventArgs e) =>
            IntelligenceScroller.ChangeView(IntelligenceScroller.HorizontalOffset + 300, null, null);
        private void IntelligenceScroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            IntelligenceLeftButton.Visibility = IntelligenceScroller.HorizontalOffset > 0 ? Visibility.Visible : Visibility.Collapsed;
            IntelligenceRightButton.Visibility = IntelligenceScroller.HorizontalOffset < IntelligenceScroller.ScrollableWidth ? Visibility.Visible : Visibility.Collapsed;
        }
        private void IntelligenceScroller_PointerWheelChanged(object sender, PointerRoutedEventArgs e) =>
            HandleNestedWheel(IntelligenceScroller, ContentPanel, e);

        private void RadarLeftButton_Click(object sender, RoutedEventArgs e) =>
            RadarScroller.ChangeView(RadarScroller.HorizontalOffset - 300, null, null);
        private void RadarRightButton_Click(object sender, RoutedEventArgs e) =>
            RadarScroller.ChangeView(RadarScroller.HorizontalOffset + 300, null, null);
        private void RadarScroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            RadarLeftButton.Visibility = RadarScroller.HorizontalOffset > 0 ? Visibility.Visible : Visibility.Collapsed;
            RadarRightButton.Visibility = RadarScroller.HorizontalOffset < RadarScroller.ScrollableWidth ? Visibility.Visible : Visibility.Collapsed;
        }
        private void RadarScroller_PointerWheelChanged(object sender, PointerRoutedEventArgs e) =>
            HandleNestedWheel(RadarScroller, ContentPanel, e);

        private void RadarItem_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is CoreItems.PlaylistItem playlistItem)
                ViewModel?.OpenPlaylist(playlistItem);
        }

        private void IntelligenceSongCard_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border border && border.Tag is int index)
                ViewModel?.PlayIntelligenceAtAsync(index).Forget();
        }

        private void ViewAllDailySongsButton_Click(object sender, RoutedEventArgs e) => ViewModel?.OpenAllDailySongs();
        private void ViewAllIntelligenceButton_Click(object sender, RoutedEventArgs e) => ViewModel?.OpenAllIntelligence();
        private void ViewAllRadarButton_Click(object sender, RoutedEventArgs e) => ViewModel?.OpenAllRadar();
        private void ViewAllGuessButton_Click(object sender, RoutedEventArgs e) => ViewModel?.OpenAllGuess();
    }
}
