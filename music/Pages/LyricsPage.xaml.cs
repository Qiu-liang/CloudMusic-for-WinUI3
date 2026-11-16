using System;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using music.Core.Abstractions;
using music.Core.Models;
using music.Core.ViewModels;

namespace music.Pages
{
    public sealed partial class LyricsPage : Page
    {
        public LyricsViewModel? ViewModel { get; private set; }

        public LyricsPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is NavigationRequest request)
            {
                DetachViewModel();
                ViewModel = (LyricsViewModel)request.CreateViewModel();
                ViewModel.PropertyChanged += ViewModel_PropertyChanged;
                ViewModel.LyricsUpdated += ViewModel_LyricsUpdated;
                ViewModel.ActiveLineChanged += ViewModel_ActiveLineChanged;

                ViewModel.Activate(request.Parameter as Song);
                SongInfoText.Text = ViewModel.SongInfoText;
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
                ViewModel.LyricsUpdated -= ViewModel_LyricsUpdated;
                ViewModel.ActiveLineChanged -= ViewModel_ActiveLineChanged;
                ViewModel.Dispose();
                ViewModel = null;
            }
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (ViewModel != null && e.PropertyName == nameof(LyricsViewModel.SongInfoText))
                SongInfoText.Text = ViewModel.SongInfoText;
        }

        private void ViewModel_LyricsUpdated(object? sender, EventArgs e) => DisplayLyrics();

        private void ViewModel_ActiveLineChanged(object? sender, int index) => HighlightLyric(index);

        private void DisplayLyrics()
        {
            if (ViewModel == null) return;
            LyricsPanel.Children.Clear();

            if (ViewModel.Lyrics.Count == 0)
            {
                LyricsPanel.Children.Add(new TextBlock
                {
                    Text = "暂无歌词",
                    FontSize = 22,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 128, 128, 128)),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                return;
            }

            foreach (var lyric in ViewModel.Lyrics)
            {
                LyricsPanel.Children.Add(new TextBlock
                {
                    Text = lyric.Text,
                    FontSize = 24,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(200, 80, 80, 80)),
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    MaxWidth = 700,
                    Tag = lyric
                });
            }

            HighlightLyric(ViewModel.CurrentLyricIndex);
        }

        private void HighlightLyric(int index)
        {
            var children = LyricsPanel.Children;
            var isDark = ActualTheme == ElementTheme.Dark;
            var defaultColor = isDark
                ? Windows.UI.Color.FromArgb(220, 200, 200, 200)
                : Windows.UI.Color.FromArgb(220, 80, 80, 80);
            var highlightColor = isDark
                ? Windows.UI.Color.FromArgb(255, 96, 165, 250)
                : Windows.UI.Color.FromArgb(255, 0, 120, 215);

            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is TextBlock textBlock)
                {
                    if (i == index)
                    {
                        textBlock.FontSize = 28;
                        textBlock.FontWeight = FontWeights.Bold;
                        textBlock.Foreground = new SolidColorBrush(highlightColor);

                        var transform = textBlock.TransformToVisual(LyricsPanel);
                        if (transform != null)
                        {
                            var position = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                            LyricsScroller.ChangeView(null, position.Y - 180, null, false);
                        }
                    }
                    else
                    {
                        textBlock.FontSize = 24;
                        textBlock.FontWeight = FontWeights.Normal;
                        textBlock.Foreground = new SolidColorBrush(defaultColor);
                    }
                }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => ViewModel?.Close();
    }
}
