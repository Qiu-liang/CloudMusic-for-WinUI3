using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.UI;
using music.Models;
using music.Services;

namespace music
{
    public sealed partial class LyricsWindow : Window
    {
        private Song? _currentSong;
        private List<LyricLine> _lyrics = new();
        private int _currentLyricIndex = -1;
        private bool _isProgressDragging = false;

        public LyricsWindow()
        {
            this.InitializeComponent();

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.Resize(new SizeInt32 { Width = 1200, Height = 800 });
            appWindow.Move(new PointInt32 { X = 200, Y = 100 });
            appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;

            SetupPlaybackEvents();
            LoadCurrentSong();
            _ = LoadLyricsAsync();
        }

        private void LoadCurrentSong()
        {
            var playbackService = MainWindow.PlaybackService;
            if (playbackService == null) return;

            _currentSong = playbackService.CurrentSong;
            if (_currentSong == null) return;

            SongTitleText.Text = _currentSong.Name;
            ArtistText.Text = _currentSong.ArtistNames;
            AlbumText.Text = _currentSong.Album.Name;

            if (!string.IsNullOrEmpty(_currentSong.CoverImgUrl))
            {
                AlbumCoverImage.Source = new BitmapImage(new Uri(_currentSong.CoverImgUrl));
                _ = ExtractDominantColorAsync(_currentSong.CoverImgUrl);
            }

            PlayPauseIcon.Glyph = playbackService.IsPlaying ? "\uE769" : "\uE768";
            ShuffleIcon.Opacity = playbackService.IsShuffleEnabled ? 1.0 : 0.4;

            var mode = playbackService.GetRepeatMode();
            RepeatIcon.Glyph = mode == RepeatMode.One ? "\uE8ED" : "\uE8EE";
            RepeatIcon.Opacity = mode == RepeatMode.None ? 0.4 : 1.0;

            VolumeSlider.ValueChanged -= VolumeSlider_ValueChanged;
            VolumeSlider.Value = playbackService.Volume * 100;
            VolumeSlider.ValueChanged += VolumeSlider_ValueChanged;

            var duration = playbackService.GetDuration();
            if (duration.TotalSeconds > 0)
                TotalTimeText.Text = FormatTime(duration);
        }

        private Task ExtractDominantColorAsync(string imageUrl)
        {
            try
            {
                var hash = imageUrl.GetHashCode();
                var r = (byte)(Math.Abs(hash) % 60 + 15);
                var g = (byte)(Math.Abs(hash >> 8) % 40 + 15);
                var b = (byte)(Math.Abs(hash >> 16) % 50 + 30);
                DynamicBg.TintColor = Color.FromArgb(255, r, g, b);
                DynamicBg.FallbackColor = Color.FromArgb(255, r, g, b);
            }
            catch { }
            return Task.CompletedTask;
        }

        private async Task LoadLyricsAsync()
        {
            if (_currentSong == null) return;

            var lyricInfo = await App.ApiService.GetLyricsAsync(_currentSong.Id);
            if (lyricInfo == null || string.IsNullOrEmpty(lyricInfo.LrcLyric)) return;

            _lyrics = ParseLyrics(lyricInfo.LrcLyric);
            DispatcherQueue.TryEnqueue(() => DisplayLyrics());
        }

        private List<LyricLine> ParseLyrics(string lrcContent)
        {
            var lines = new List<LyricLine>();
            if (string.IsNullOrEmpty(lrcContent)) return lines;

            foreach (var line in lrcContent.Split('\n'))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                if (trimmed.StartsWith("[") && trimmed.Contains("]"))
                {
                    var timeEnd = trimmed.IndexOf(']');
                    var timeStr = trimmed.Substring(1, timeEnd - 1);
                    var text = trimmed.Substring(timeEnd + 1).Trim();

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
                    FontSize = 32,
                    Opacity = 0.5,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = new SolidColorBrush(Colors.White)
                });
                return;
            }

            foreach (var lyric in _lyrics)
            {
                var tb = new TextBlock
                {
                    Text = lyric.Text,
                    FontSize = 34,
                    FontWeight = FontWeights.SemiLight,
                    Foreground = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    MaxWidth = 700,
                    Tag = lyric
                };
                LyricsPanel.Children.Add(tb);
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

        private async void HighlightLyric(int index)
        {
            if (index < 0) return;

            for (int i = 0; i < LyricsPanel.Children.Count; i++)
            {
                if (LyricsPanel.Children[i] is not TextBlock tb) continue;

                int distance = Math.Abs(i - index);

                if (distance == 0)
                {
                    tb.FontSize = 62;
                    tb.FontWeight = FontWeights.Bold;
                    tb.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
                }
                else if (distance == 1)
                {
                    tb.FontSize = 48;
                    tb.FontWeight = FontWeights.SemiBold;
                    tb.Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255));
                }
                else if (distance == 2)
                {
                    tb.FontSize = 40;
                    tb.FontWeight = FontWeights.Normal;
                    tb.Foreground = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255));
                }
                else
                {
                    tb.FontSize = 34;
                    tb.FontWeight = FontWeights.Normal;
                    tb.Foreground = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));
                }
            }

            // 等待两帧布局更新后再滚动
            await Task.Delay(16);
            await Task.Delay(16);

            if (index >= LyricsPanel.Children.Count) return;
            if (LyricsPanel.Children[index] is not FrameworkElement target) return;

            double targetCenter = target.ActualOffset.Y + target.ActualHeight / 2;
            double scrollTarget = targetCenter - LyricsScroller.ViewportHeight / 2;
            LyricsScroller.ChangeView(null, Math.Max(0, scrollTarget), null, false);
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
                DispatcherQueue.TryEnqueue(() =>
                {
                    LyricsPanel.Children.Clear();
                    SongTitleText.Text = "";
                    ArtistText.Text = "";
                    AlbumText.Text = "";
                    LoadCurrentSong();
                    _ = LoadLyricsAsync();
                });
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

        private string FormatTime(TimeSpan time) => $"{(int)time.TotalMinutes:D2}:{time.Seconds:D2}";

        private void ProgressSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isProgressDragging) return;
            if (MainWindow.PlaybackService == null) return;
            var duration = MainWindow.PlaybackService.GetDuration();
            if (duration.TotalSeconds > 0)
            {
                _isProgressDragging = true;
                MainWindow.PlaybackService.Seek(TimeSpan.FromSeconds((e.NewValue / 100) * duration.TotalSeconds));
                _isProgressDragging = false;
            }
        }

        private void ProgressSlider_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            _isProgressDragging = true;
        }

        private void ProgressSlider_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            _isProgressDragging = false;
            if (MainWindow.PlaybackService == null) return;
            var duration = MainWindow.PlaybackService.GetDuration();
            MainWindow.PlaybackService.Seek(TimeSpan.FromSeconds(ProgressSlider.Value / 100 * duration.TotalSeconds));
        }

        private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (MainWindow.PlaybackService != null)
                MainWindow.PlaybackService.Volume = e.NewValue / 100;
        }

        private void ShuffleButton_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.PlaybackService?.ToggleShuffle();
            ShuffleIcon.Opacity = MainWindow.PlaybackService?.IsShuffleEnabled == true ? 1.0 : 0.4;
        }

        private async void PrevButton_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.PlaybackService != null)
                await MainWindow.PlaybackService.PreviousAsync();
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
            => MainWindow.PlaybackService?.TogglePlayPause();

        private async void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.PlaybackService != null)
                await MainWindow.PlaybackService.NextAsync();
        }

        private void RepeatButton_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.PlaybackService?.ToggleRepeat();
            var mode = MainWindow.PlaybackService?.GetRepeatMode();
            RepeatIcon.Glyph = mode == RepeatMode.One ? "\uE8ED" : "\uE8EE";
            RepeatIcon.Opacity = mode == RepeatMode.None ? 0.4 : 1.0;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
