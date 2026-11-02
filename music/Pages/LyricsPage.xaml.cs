using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using music.Models;
using music.Services;

namespace music.Pages
{
    public sealed partial class LyricsPage : Page
    {
        private Song? _currentSong;
        private List<LyricLine> _lyrics = new();
        private int _currentLyricIndex = -1;
        private bool _isActive;
        private CancellationTokenSource? _lyricsCancellation;
        private readonly LatestLyricRequest _latestLyrics = new();

        public LyricsPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _isActive = true;
            SetupPlaybackEvents();

            if (e.Parameter is Song song)
            {
                _currentSong = song;
                LoadSongInfo();
                _ = LoadLyricsAsync();
            }
        }

        private void LoadSongInfo()
        {
            if (_currentSong == null) return;
            SongInfoText.Text = $"{_currentSong.Name} - {_currentSong.ArtistNames}";
        }

        private async System.Threading.Tasks.Task LoadLyricsAsync()
        {
            if (_currentSong == null) return;
            if (!string.IsNullOrEmpty(_currentSong.LocalFilePath))
            {
                _lyrics.Clear();
                DisplayLyrics();
                return;
            }

            _lyricsCancellation?.Cancel();
            _lyricsCancellation?.Dispose();
            _lyricsCancellation = new CancellationTokenSource();
            var cancellation = _lyricsCancellation;
            var songId = _currentSong.Id;
            var request = _latestLyrics.Next(songId);

            try
            {
                var lyricInfo = await App.ApiService.GetLyricsAsync(songId, cancellation.Token);
                if (!_isActive || cancellation.IsCancellationRequested) return;
                if (!_latestLyrics.IsCurrent(request, songId, _currentSong?.Id ?? string.Empty)) return;

                _lyrics = lyricInfo == null || string.IsNullOrEmpty(lyricInfo.LrcLyric)
                    ? new List<LyricLine>()
                    : ParseLyrics(lyricInfo.LrcLyric);
                DispatcherQueue.TryEnqueue(() => DisplayLyrics());
            }
            catch (OperationCanceledException) { }
        }

        private List<LyricLine> ParseLyrics(string lrcContent)
        {
            var lines = new List<LyricLine>();
            if (string.IsNullOrEmpty(lrcContent)) return lines;

            foreach (var line in lrcContent.Split('\n'))
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine)) continue;

                if (trimmedLine.StartsWith("[") && trimmedLine.Contains("]"))
                {
                    var timeEnd = trimmedLine.IndexOf(']');
                    var timeStr = trimmedLine.Substring(1, timeEnd - 1);
                    var text = trimmedLine.Substring(timeEnd + 1).Trim();

                    if (TryParseTime(timeStr, out var time) && !string.IsNullOrEmpty(text))
                    {
                        lines.Add(new LyricLine { Time = time, Text = text });
                    }
                }
            }

            return lines.OrderBy(l => l.Time).ToList();
        }

        private bool TryParseTime(string timeStr, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            try
            {
                var parts = timeStr.Split(':');
                if (parts.Length == 2)
                {
                    var minutes = int.Parse(parts[0]);
                    var seconds = double.Parse(parts[1]);
                    time = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
                    return true;
                }
            }
            catch { }
            return false;
        }

        private void DisplayLyrics()
        {
            LyricsPanel.Children.Clear();

            if (_lyrics.Count == 0)
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

            foreach (var lyric in _lyrics)
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

            UpdateLyricsThemeColors();
        }

        private void UpdateLyricsThemeColors()
        {
            var isDark = this.ActualTheme == ElementTheme.Dark;
            var defaultColor = isDark
                ? Windows.UI.Color.FromArgb(220, 200, 200, 200)
                : Windows.UI.Color.FromArgb(220, 80, 80, 80);
            var highlightColor = isDark
                ? Windows.UI.Color.FromArgb(255, 96, 165, 250)
                : Windows.UI.Color.FromArgb(255, 0, 120, 215);

            foreach (var child in LyricsPanel.Children)
            {
                if (child is TextBlock textBlock)
                {
                    textBlock.Foreground = new SolidColorBrush(defaultColor);
                }
            }

            if (_currentLyricIndex >= 0 && _currentLyricIndex < LyricsPanel.Children.Count)
            {
                if (LyricsPanel.Children[_currentLyricIndex] is TextBlock highlight)
                {
                    highlight.Foreground = new SolidColorBrush(highlightColor);
                }
            }
        }

        private void SetupPlaybackEvents()
        {
            var playbackService = MainWindow.PlaybackService;
            if (playbackService == null) return;
            playbackService.CurrentSongChanged -= PlaybackService_CurrentSongChanged;
            playbackService.PositionChanged -= PlaybackService_PositionChanged;
            playbackService.CurrentSongChanged += PlaybackService_CurrentSongChanged;
            playbackService.PositionChanged += PlaybackService_PositionChanged;
        }

        private void PlaybackService_CurrentSongChanged(object? sender, Song song)
        {
            if (!_isActive) return;
            _currentSong = song;
            _lyrics.Clear();
            _currentLyricIndex = -1;
            DispatcherQueue.TryEnqueue(() => LoadSongInfo());
            _ = LoadLyricsAsync();
        }

        private void PlaybackService_PositionChanged(object? sender, TimeSpan position)
        {
            if (!_isActive) return;
            DispatcherQueue.TryEnqueue(() => UpdateLyricsHighlight(position));
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            _isActive = false;
            _lyricsCancellation?.Cancel();
            var playbackService = MainWindow.PlaybackService;
            if (playbackService != null)
            {
                playbackService.CurrentSongChanged -= PlaybackService_CurrentSongChanged;
                playbackService.PositionChanged -= PlaybackService_PositionChanged;
            }
        }

        private void UpdateLyricsHighlight(TimeSpan position)
        {
            var currentMs = position.TotalMilliseconds;
            var newIndex = -1;

            for (int i = _lyrics.Count - 1; i >= 0; i--)
            {
                if (currentMs >= _lyrics[i].Time.TotalMilliseconds)
                {
                    newIndex = i;
                    break;
                }
            }

            if (newIndex != _currentLyricIndex)
            {
                _currentLyricIndex = newIndex;
                HighlightLyric(newIndex);
            }
        }

        private void HighlightLyric(int index)
        {
            var children = LyricsPanel.Children;
            var isDark = this.ActualTheme == ElementTheme.Dark;
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

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }
    }
}
