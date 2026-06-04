using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
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
        private bool _isInitialized = false;
        private bool _isProgressDragging = false;

        public LyricsPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is Song song)
            {
                _currentSong = song;
                LoadSongInfo();
                _ = LoadLyricsAsync();
            }

            if (!_isInitialized)
            {
                SetupPlaybackEvents();
                _isInitialized = true;
            }
        }

        private void LoadSongInfo()
        {
            if (_currentSong == null) return;

            SongInfoText.Text = $"{_currentSong.Name} - {_currentSong.ArtistNames}";
            SongTitleText.Text = _currentSong.Name;
            ArtistText.Text = _currentSong.ArtistNames;

            if (!string.IsNullOrEmpty(_currentSong.CoverImgUrl))
            {
                AlbumImage.Source = new BitmapImage(new Uri(_currentSong.CoverImgUrl));
            }

            var playbackService = MainWindow.PlaybackService;
            if (playbackService != null)
            {
                TotalTimeText.Text = FormatTime(playbackService.GetDuration());
                PlayPauseIcon.Glyph = playbackService.IsPlaying ? "\uE769" : "\uE768";
                
                // 同步随机播放按钮状态
                ShuffleButton.Opacity = playbackService.IsShuffleEnabled ? 1.0 : 0.5;
                
                // 同步循环播放按钮状态
                var repeatMode = playbackService.GetRepeatMode();
                switch (repeatMode)
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
        }

        private async System.Threading.Tasks.Task LoadLyricsAsync()
        {
            if (_currentSong == null) return;

            System.Diagnostics.Debug.WriteLine($"[Lyrics] Loading lyrics for: {_currentSong.Name}");
            var lyricInfo = await App.ApiService.GetLyricsAsync(_currentSong.Id);
            if (lyricInfo == null || string.IsNullOrEmpty(lyricInfo.LrcLyric))
            {
                System.Diagnostics.Debug.WriteLine("[Lyrics] No lyrics found");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[Lyrics] LrcLyric length: {lyricInfo.LrcLyric.Length}");
            _lyrics = ParseLyrics(lyricInfo.LrcLyric);
            System.Diagnostics.Debug.WriteLine($"[Lyrics] Parsed {_lyrics.Count} lines");

            DispatcherQueue.TryEnqueue(() => DisplayLyrics());
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
                    FontSize = 18,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 128, 128, 128)),
                    HorizontalAlignment = HorizontalAlignment.Left
                });
                return;
            }

            foreach (var lyric in _lyrics)
            {
                LyricsPanel.Children.Add(new TextBlock
                {
                    Text = lyric.Text,
                    FontSize = 20,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(200, 80, 80, 80)),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 600,
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

            playbackService.CurrentSongChanged += (s, song) =>
            {
                _currentSong = song;
                _lyrics.Clear();
                _currentLyricIndex = -1;
                DispatcherQueue.TryEnqueue(() => LoadSongInfo());
                _ = LoadLyricsAsync();
            };

            playbackService.PositionChanged += (s, position) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    UpdateProgress(position);
                    UpdateLyricsHighlight(position);
                });
            };

            playbackService.DurationChanged += (s, duration) =>
            {
                DispatcherQueue.TryEnqueue(() => TotalTimeText.Text = FormatTime(duration));
            };

            playbackService.PlaybackStateChanged += (s, isPlaying) =>
            {
                DispatcherQueue.TryEnqueue(() => PlayPauseIcon.Glyph = isPlaying ? "\uE769" : "\uE768");
            };
        }

        private void UpdateProgress(TimeSpan position)
        {
            if (_isProgressDragging) return;
            var duration = MainWindow.PlaybackService?.GetDuration() ?? TimeSpan.Zero;
            if (duration.TotalSeconds > 0)
                ProgressSlider.Value = (position.TotalSeconds / duration.TotalSeconds) * 100;
            CurrentTimeText.Text = FormatTime(position);
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
                        textBlock.FontSize = 24;
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
                        textBlock.FontSize = 20;
                        textBlock.FontWeight = FontWeights.Normal;
                        textBlock.Foreground = new SolidColorBrush(defaultColor);
                    }
                }
            }
        }

        private string FormatTime(TimeSpan time) => $"{(int)time.TotalMinutes:D2}:{time.Seconds:D2}";

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }

        private void ProgressSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isProgressDragging && MainWindow.PlaybackService != null)
            {
                var duration = MainWindow.PlaybackService.GetDuration();
                if (duration.TotalSeconds > 0)
                    MainWindow.PlaybackService.Seek(TimeSpan.FromSeconds((e.NewValue / 100) * duration.TotalSeconds));
            }
        }

        private void ProgressSlider_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => _isProgressDragging = true;
        private void ProgressSlider_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => _isProgressDragging = false;

        private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (MainWindow.PlaybackService != null)
                MainWindow.PlaybackService.Volume = e.NewValue / 100;
        }

        private void ShuffleButton_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.PlaybackService?.ToggleShuffle();
            ShuffleButton.Opacity = MainWindow.PlaybackService?.IsShuffleEnabled == true ? 1.0 : 0.5;
        }

        private async void PrevButton_Click(object sender, RoutedEventArgs e) { if (MainWindow.PlaybackService != null) await MainWindow.PlaybackService.PreviousAsync(); }
        private void PlayPauseButton_Click(object sender, RoutedEventArgs e) => MainWindow.PlaybackService?.TogglePlayPause();
        private async void NextButton_Click(object sender, RoutedEventArgs e) { if (MainWindow.PlaybackService != null) await MainWindow.PlaybackService.NextAsync(); }

        private void RepeatButton_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.PlaybackService?.ToggleRepeat();
            var mode = MainWindow.PlaybackService?.GetRepeatMode();
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
    }

    public class LyricLine
    {
        public TimeSpan Time { get; set; }
        public string Text { get; set; } = string.Empty;
    }
}
