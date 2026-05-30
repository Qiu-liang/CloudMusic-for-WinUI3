using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

            SongTitleText.Text = _currentSong.Name;
            ArtistText.Text = _currentSong.ArtistNames;

            if (!string.IsNullOrEmpty(_currentSong.CoverImgUrl))
            {
                AlbumImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(_currentSong.CoverImgUrl));
            }
        }

        private async System.Threading.Tasks.Task LoadLyricsAsync()
        {
            if (_currentSong == null) return;

            var lyricInfo = await App.ApiService.GetLyricsAsync(_currentSong.Id);
            if (lyricInfo == null) return;

            _lyrics = ParseLyrics(lyricInfo.LrcLyric);

            DispatcherQueue.TryEnqueue(() =>
            {
                DisplayLyrics();
            });
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

                    if (TryParseTime(timeStr, out var time))
                    {
                        lines.Add(new LyricLine
                        {
                            Time = time,
                            Text = text
                        });
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
                var textBlock = new TextBlock
                {
                    Text = "暂无歌词",
                    FontSize = 18,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                LyricsPanel.Children.Add(textBlock);
                return;
            }

            foreach (var lyric in _lyrics)
            {
                var textBlock = new TextBlock
                {
                    Text = lyric.Text,
                    FontSize = 18,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 380
                };
                LyricsPanel.Children.Add(textBlock);
            }
        }

        private void SetupPlaybackEvents()
        {
            var playbackService = MainWindow.PlaybackService;
            if (playbackService == null) return;

            playbackService.CurrentSongChanged += (s, song) =>
            {
                _currentSong = song;
                DispatcherQueue.TryEnqueue(() =>
                {
                    LoadSongInfo();
                });
                _ = LoadLyricsAsync();
            };

            playbackService.PositionChanged += (s, position) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    UpdateLyricsHighlight(position);
                });
            };
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
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is TextBlock textBlock)
                {
                    if (i == index)
                    {
                        textBlock.FontSize = 22;
                        textBlock.FontWeight = FontWeights.Bold;
                        textBlock.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
                        
                        var transform = textBlock.TransformToVisual(LyricsPanel);
                        var position = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                        LyricsScroller.ChangeView(null, position.Y - 200, null, false);
                    }
                    else
                    {
                        textBlock.FontSize = 18;
                        textBlock.FontWeight = FontWeights.Normal;
                        textBlock.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
                    }
                }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }

        private void LikeButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: 实现喜欢功能
        }
    }

    public class LyricLine
    {
        public TimeSpan Time { get; set; }
        public string Text { get; set; } = string.Empty;
    }
}