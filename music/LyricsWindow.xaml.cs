using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Windows.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Graphics.Display;
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
        private bool _isProgressDragging;

        private float _scrollOffset;
        private float _targetScrollOffset;
        private float _lyricsWidth = 800;
        private float _lyricsHeight = 600;
        private readonly List<float> _lineHeights = new();
        private readonly List<float> _lineOffsets = new();
        private bool _canvasReady;

        // 缓存的文本格式，避免在 Draw 中重复创建
        private CanvasTextFormat? _fmtCurrent;
        private CanvasTextFormat? _fmtNear;
        private CanvasTextFormat? _fmtMeasure;
        private CanvasTextFormat? _fmtNoLyrics;

        // 缓存的测量用 RenderTarget，避免每帧/每次重算都创建 GPU 纹理
        private CanvasRenderTarget? _measureTarget;

        // 模糊专辑封面背景缓存
        private CanvasRenderTarget? _blurredBg;
        private bool _isClosed = false;

        private DispatcherTimer? _scrollTimer;

        // Win2D 不支持逗号分隔的 fallback，使用系统默认 + 手动检测
        private const string PrimaryFont = "Segoe UI Variable";
        private const string FallbackFont = "Microsoft YaHei UI";
        private string _activeFont = PrimaryFont;

        // 统一的字号和间距 —— 所有行用相同字号，避免布局/渲染不一致
        private const float FontSize = 28f;
        private const float FontSizeCurrent = 32f;   // 当前行稍大
        private const float LineGap = 24f;             // 行与行之间的空白（px）
        private const float LeftMargin = 48f;
        private const float RightMargin = 64f;

        private float _dpiScale = 1f;

        // LRC 解析正则：支持 [mm:ss.xx] / [mm:ss.xxx] / 多时间戳
        private static readonly Regex LrcTimeRegex = new(@"\[(\d{1,3}):(\d{2})(?:\.(\d{1,3}))?\]", RegexOptions.Compiled);

        public LyricsWindow()
        {
            InitializeComponent();

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.Resize(new SizeInt32 { Width = 1400, Height = 900 });
            appWindow.Move(new PointInt32 { X = 100, Y = 50 });
            appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;

            try
            {
                var displayInfo = DisplayInformation.GetForCurrentView();
                _dpiScale = displayInfo.LogicalDpi / 96f;
            }
            catch { _dpiScale = 1f; }

            // 检测主字体是否可用，不可用则回退
            try
            {
                using var testFmt = new CanvasTextFormat { FontFamily = PrimaryFont, FontSize = 12 };
                _activeFont = PrimaryFont;
            }
            catch
            {
                _activeFont = FallbackFont;
            }

            SetupPlaybackEvents();
            LoadCurrentSong();
            _ = LoadLyricsAsync();

            _scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _scrollTimer.Tick += OnScrollTick;
            _scrollTimer.Start();

            Closed += OnWindowClosed;
        }

        private void OnWindowClosed(object sender, WindowEventArgs args)
        {
            _isClosed = true;
            _scrollTimer?.Stop();
            _scrollTimer = null;

            // 显式释放所有缓存的 Win2D 资源
            _fmtCurrent?.Dispose(); _fmtCurrent = null;
            _fmtNear?.Dispose(); _fmtNear = null;
            _fmtMeasure?.Dispose(); _fmtMeasure = null;
            _fmtNoLyrics?.Dispose(); _fmtNoLyrics = null;
            _measureTarget?.Dispose(); _measureTarget = null;
            _blurredBg?.Dispose(); _blurredBg = null;

            BackgroundCanvas?.RemoveFromVisualTree();
            LyricsCanvas?.RemoveFromVisualTree();
        }

        #region Text Format Cache

        private void EnsureTextFormats(CanvasDevice device)
        {
            if (_fmtCurrent != null && _fmtNear != null && _fmtMeasure != null && _fmtNoLyrics != null)
                return;

            _fmtCurrent?.Dispose();
            _fmtNear?.Dispose();
            _fmtMeasure?.Dispose();
            _fmtNoLyrics?.Dispose();

            // 当前行：稍大、Medium 字重
            _fmtCurrent = new CanvasTextFormat
            {
                FontFamily = _activeFont,
                FontSize = FontSizeCurrent * _dpiScale,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = CanvasHorizontalAlignment.Left,
                WordWrapping = CanvasWordWrapping.Wrap
            };

            // 非当前行：标准大小、Light 字重
            _fmtNear = new CanvasTextFormat
            {
                FontFamily = _activeFont,
                FontSize = FontSize * _dpiScale,
                FontWeight = FontWeights.Light,
                HorizontalAlignment = CanvasHorizontalAlignment.Left,
                WordWrapping = CanvasWordWrapping.Wrap
            };

            // 测量用：取两种字号中较大的，确保高度足够
            _fmtMeasure = new CanvasTextFormat
            {
                FontFamily = _activeFont,
                FontSize = FontSizeCurrent * _dpiScale,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = CanvasHorizontalAlignment.Left,
                WordWrapping = CanvasWordWrapping.Wrap
            };

            _fmtNoLyrics = new CanvasTextFormat
            {
                FontFamily = _activeFont,
                FontSize = 18 * _dpiScale,
                HorizontalAlignment = CanvasHorizontalAlignment.Center
            };
        }

        private void EnsureMeasureTarget(CanvasControl sender)
        {
            if (_measureTarget != null)
                return;

            _measureTarget?.Dispose();
            _measureTarget = new CanvasRenderTarget(sender, _lyricsWidth, Math.Max(_lyricsHeight, 1), 96);
        }

        #endregion

        #region Background (Blurred Album Art)

        private void OnScrollTick(object? sender, object e)
        {
            // 平滑滚动插值，仅驱动歌词画布
            var prev = _scrollOffset;
            _scrollOffset += (_targetScrollOffset - _scrollOffset) * 0.08f;
            if (Math.Abs(_scrollOffset - prev) > 0.05f)
                LyricsCanvas?.Invalidate();
        }

        private async void LoadBlurredBackgroundAsync()
        {
            if (_isClosed || _currentSong == null || string.IsNullOrEmpty(_currentSong.CoverImgUrl)) return;

            try
            {
                var httpClient = new System.Net.Http.HttpClient();
                var bytes = await httpClient.GetByteArrayAsync(_currentSong.CoverImgUrl);
                httpClient.Dispose();

                if (_isClosed) return;

                DispatcherQueue.TryEnqueue(async () =>
                {
                    if (_isClosed) return;

                    try
                    {
                        using var stream = new MemoryStream(bytes).AsRandomAccessStream();
                        var bitmap = await CanvasBitmap.LoadAsync(BackgroundCanvas.Device, stream);

                        if (_isClosed) { bitmap.Dispose(); return; }

                        var device = BackgroundCanvas.Device;
                        float bgW = (float)Math.Max(BackgroundCanvas.Size.Width, 1);
                        float bgH = (float)Math.Max(BackgroundCanvas.Size.Height, 1);

                        var oldBg = _blurredBg;
                        _blurredBg = new CanvasRenderTarget(device, bgW, bgH, 96);

                        using (var tempTarget = new CanvasRenderTarget(device, bgW, bgH, 96))
                        {
                            using (var tempDs = tempTarget.CreateDrawingSession())
                            {
                                var destRect = new Windows.Foundation.Rect(0, 0, bgW, bgH);
                                var srcRect = new Windows.Foundation.Rect(0, 0, bitmap.Size.Width, bitmap.Size.Height);
                                tempDs.DrawImage(bitmap, destRect, srcRect);
                            }

                            using var blurEffect = new GaussianBlurEffect
                            {
                                Source = tempTarget,
                                BlurAmount = 80f,
                                BorderMode = EffectBorderMode.Soft
                            };

                            using (var ds = _blurredBg.CreateDrawingSession())
                            {
                                var destRect = new Windows.Foundation.Rect(-30, -30, bgW + 60, bgH + 60);
                                var srcRect = new Windows.Foundation.Rect(0, 0, bgW, bgH);
                                ds.DrawImage(blurEffect, destRect, srcRect);
                            }
                        }

                        bitmap.Dispose();

                        // 延迟释放旧背景，避免 Draw 回调中使用
                        oldBg?.Dispose();

                        if (!_isClosed)
                            BackgroundCanvas?.Invalidate();
                    }
                    catch (ObjectDisposedException) { /* 窗口已关闭 */ }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[LyricsWindow] Blur bg error: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LyricsWindow] Load cover error: {ex.Message}");
            }
        }

        private void BackgroundCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            if (_isClosed) return;

            var ds = args.DrawingSession;
            var w = (float)sender.Size.Width;
            var h = (float)sender.Size.Height;

            ds.Clear(Color.FromArgb(255, 12, 12, 14));

            try
            {
                var bg = _blurredBg;
                if (bg != null)
                {
                    var srcRect = new Windows.Foundation.Rect(0, 0, bg.Size.Width, bg.Size.Height);
                    var scale = Math.Max(w / bg.Size.Width, h / bg.Size.Height) * 1.15f;
                    var drawW = bg.Size.Width * scale;
                    var drawH = bg.Size.Height * scale;
                    var drawX = (w - drawW) / 2f;
                    var drawY = (h - drawH) / 2f;

                    ds.DrawImage(bg, new Windows.Foundation.Rect(drawX, drawY, drawW, drawH), srcRect, 0.7f);
                }
            }
            catch (ObjectDisposedException) { /* 资源已释放，跳过绘制 */ }

            // 叠加暗色渐变，保证歌词可读性
            using (var overlay = new CanvasSolidColorBrush(ds, Color.FromArgb(160, 8, 8, 12)))
            {
                ds.FillRectangle(0, 0, w, h, overlay);
            }
        }

        #endregion

        #region Lyrics Canvas

        private void LyricsCanvas_CreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
        {
            _lyricsWidth = (float)sender.Size.Width;
            _lyricsHeight = (float)sender.Size.Height;
            _canvasReady = true;

            EnsureTextFormats(sender.Device);
            EnsureMeasureTarget(sender);
            RecalculateLayout();
        }

        private void LyricsCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            if (_isClosed) return;

            var ds = args.DrawingSession;
            _lyricsWidth = (float)sender.Size.Width;
            _lyricsHeight = (float)sender.Size.Height;

            EnsureTextFormats(sender.Device);

            if (_lyrics.Count == 0 || _fmtNoLyrics == null)
            {
                if (_fmtNoLyrics != null)
                {
                    using var b = new CanvasSolidColorBrush(ds, Color.FromArgb(80, 255, 255, 255));
                    ds.DrawText("暂无歌词", _lyricsWidth / 2, _lyricsHeight / 2, b, _fmtNoLyrics);
                }
                return;
            }

            // 当前行定位在窗口 35% 的高度处
            float anchorY = _lyricsHeight * 0.35f;
            float fadeZone = _lyricsHeight * 0.45f;
            float maxW = _lyricsWidth - LeftMargin - RightMargin;

            for (int i = 0; i < _lyrics.Count; i++)
            {
                if (i >= _lineOffsets.Count || i >= _lineHeights.Count) continue;

                // 行顶部的屏幕 Y 坐标
                float lineTop = _lineOffsets[i] + anchorY - _scrollOffset;
                float lineH = _lineHeights[i];
                // 超出可见区域则跳过
                if (lineTop + lineH < -50 || lineTop > _lyricsHeight + 50) continue;

                // 基于行中心到锚点的距离计算淡出
                float lineCenter = lineTop + lineH / 2f;
                float dist = Math.Abs(lineCenter - anchorY);
                float fade = 1f - Math.Clamp(dist / fadeZone, 0f, 1f);
                fade = fade * fade;  // 二次曲线，更自然的淡出

                bool isCurrent = (i == _currentLyricIndex);
                var fmt = isCurrent ? _fmtCurrent! : _fmtNear!;

                using var layout = new CanvasTextLayout(ds, _lyrics[i].Text, fmt, maxW, float.MaxValue);
                float textH = (float)layout.LayoutBounds.Height;

                // 文字在行内垂直居中
                float textY = lineTop + (lineH - textH) / 2f;

                if (isCurrent)
                {
                    // 当前行：纯白色，轻微发光效果
                    for (int g = 3; g >= 1; g--)
                    {
                        byte ga = (byte)Math.Min(g * 8, 255);
                        using var gb = new CanvasSolidColorBrush(ds, Color.FromArgb(ga, 255, 255, 255));
                        ds.DrawTextLayout(layout, LeftMargin - g, textY, gb);
                        ds.DrawTextLayout(layout, LeftMargin + g, textY, gb);
                        ds.DrawTextLayout(layout, LeftMargin, textY - g, gb);
                        ds.DrawTextLayout(layout, LeftMargin, textY + g, gb);
                    }
                    using var wb = new CanvasSolidColorBrush(ds, Color.FromArgb(255, 255, 255, 255));
                    ds.DrawTextLayout(layout, LeftMargin, textY, wb);
                }
                else
                {
                    // 非当前行：灰色，根据距离淡出
                    byte alpha = (byte)Math.Clamp(fade * 160, 15, 160);
                    using var tb = new CanvasSolidColorBrush(ds, Color.FromArgb(alpha, 210, 210, 215));
                    ds.DrawTextLayout(layout, LeftMargin, textY, tb);
                }
            }

            // 动态修正滚动目标（在 Draw 中更新，保证与渲染同步）
            if (_currentLyricIndex >= 0 && _currentLyricIndex < _lineOffsets.Count)
            {
                float lineCenter = _lineOffsets[_currentLyricIndex] + _lineHeights[_currentLyricIndex] / 2f;
                float newTarget = lineCenter - anchorY;
                float maxScroll = 0;
                if (_lineOffsets.Count > 0 && _lineHeights.Count == _lineOffsets.Count)
                    maxScroll = _lineOffsets[^1] + _lineHeights[^1] - _lyricsHeight;
                _targetScrollOffset = Math.Clamp(newTarget, 0, Math.Max(0, maxScroll));
            }
        }

        private void LyricsCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            _lyricsWidth = (float)e.NewSize.Width;
            _lyricsHeight = (float)e.NewSize.Height;

            // 尺寸变化时重建测量 RT 和文本格式（字号含 DPI）
            _fmtCurrent?.Dispose(); _fmtCurrent = null;
            _fmtNear?.Dispose(); _fmtNear = null;
            _fmtMeasure?.Dispose(); _fmtMeasure = null;
            _fmtNoLyrics?.Dispose(); _fmtNoLyrics = null;
            _measureTarget?.Dispose(); _measureTarget = null;

            if (_canvasReady && LyricsCanvas != null)
            {
                EnsureTextFormats(LyricsCanvas.Device);
                EnsureMeasureTarget(LyricsCanvas);
                RecalculateLayout();
            }
        }

        private void RecalculateLayout()
        {
            if (LyricsCanvas == null || _lyricsWidth <= 0 || !_canvasReady || _fmtMeasure == null) return;

            EnsureMeasureTarget(LyricsCanvas);
            if (_measureTarget == null) return;

            _lineHeights.Clear();
            _lineOffsets.Clear();

            using var ds = _measureTarget.CreateDrawingSession();
            float maxW = _lyricsWidth - LeftMargin - RightMargin;
            float total = 0;

            foreach (var lyric in _lyrics)
            {
                using var layout = new CanvasTextLayout(ds, lyric.Text, _fmtMeasure, maxW, float.MaxValue);
                float h = (float)layout.LayoutBounds.Height;
                // 行高 = 文字实际高度 + 固定间距，保证不会重叠
                float lineH = h + LineGap;
                _lineHeights.Add(lineH);
                _lineOffsets.Add(total);
                total += lineH;
            }

            UpdateScrollPosition();
        }

        private void UpdateScrollPosition()
        {
            if (_currentLyricIndex < 0 || _currentLyricIndex >= _lineOffsets.Count) return;
            float anchorY = _lyricsHeight * 0.35f;
            float lineCenter = _lineOffsets[_currentLyricIndex] + _lineHeights[_currentLyricIndex] / 2f;
            _targetScrollOffset = lineCenter - anchorY;

            float maxScroll = 0;
            if (_lineOffsets.Count > 0 && _lineHeights.Count == _lineOffsets.Count)
                maxScroll = _lineOffsets[^1] + _lineHeights[^1] - _lyricsHeight;
            _targetScrollOffset = Math.Clamp(_targetScrollOffset, 0, Math.Max(0, maxScroll));
        }

        #endregion

        #region Tap-to-Seek

        private void LyricsCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_lyrics.Count == 0 || _lineOffsets.Count == 0) return;

            var pos = e.GetCurrentPoint((UIElement)sender).Position;
            float tapY = (float)pos.Y;
            float anchorY = _lyricsHeight * 0.35f;

            for (int i = 0; i < _lyrics.Count; i++)
            {
                if (i >= _lineOffsets.Count || i >= _lineHeights.Count) continue;
                float lineTop = _lineOffsets[i] + anchorY - _scrollOffset;
                float lineBottom = lineTop + _lineHeights[i];

                if (tapY >= lineTop && tapY <= lineBottom)
                {
                    MainWindow.PlaybackService?.Seek(_lyrics[i].Time);
                    break;
                }
            }
        }

        #endregion

        #region Song & Lyrics Loading

        private void LoadCurrentSong()
        {
            var ps = MainWindow.PlaybackService;
            if (ps == null) return;

            _currentSong = ps.CurrentSong;
            if (_currentSong == null) return;

            SongTitleText.Text = _currentSong.Name;
            ArtistText.Text = _currentSong.ArtistNames;
            AlbumText.Text = _currentSong.Album.Name;

            if (!string.IsNullOrEmpty(_currentSong.CoverImgUrl))
            {
                AlbumCoverImage.Source = new BitmapImage(new Uri(_currentSong.CoverImgUrl));
                LoadBlurredBackgroundAsync();
            }

            PlayPauseIcon.Glyph = ps.IsPlaying ? "\uE769" : "\uE768";
            ShuffleIcon.Opacity = ps.IsShuffleEnabled ? 1.0 : 0.4;
            var mode = ps.GetRepeatMode();
            RepeatIcon.Glyph = mode == RepeatMode.One ? "\uE8ED" : "\uE8EE";
            RepeatIcon.Opacity = mode == RepeatMode.None ? 0.4 : 1.0;

            VolumeSlider.ValueChanged -= VolumeSlider_ValueChanged;
            VolumeSlider.Value = ps.Volume * 100;
            VolumeSlider.ValueChanged += VolumeSlider_ValueChanged;

            var duration = ps.GetDuration();
            if (duration.TotalSeconds > 0)
                TotalTimeText.Text = FormatTime(duration);
        }



        private async Task LoadLyricsAsync()
        {
            if (_currentSong == null) return;
            var lyricInfo = await App.ApiService.GetLyricsAsync(_currentSong.Id);
            if (lyricInfo == null || string.IsNullOrEmpty(lyricInfo.LrcLyric)) return;

            // 在后台线程解析，UI 线程仅做赋值和刷新
            var parsed = ParseLyrics(lyricInfo.LrcLyric);

            DispatcherQueue.TryEnqueue(() =>
            {
                _lyrics = parsed;
                _currentLyricIndex = -1;
                _scrollOffset = 0;
                _targetScrollOffset = 0;
                RecalculateLayout();
                LyricsCanvas?.Invalidate();
            });
        }

        private static List<LyricLine> ParseLyrics(string lrcContent)
        {
            var lines = new List<LyricLine>();
            if (string.IsNullOrEmpty(lrcContent)) return lines;

            foreach (var rawLine in lrcContent.Split('\n'))
            {
                var trimmed = rawLine.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                // 跳过元数据标签 [ti:] [ar:] [al:] [by:] [offset:] 等
                if (trimmed.StartsWith("[") && !char.IsDigit(trimmed.Length > 1 ? trimmed[1] : ' '))
                    continue;

                var matches = LrcTimeRegex.Matches(trimmed);
                if (matches.Count == 0) continue;

                // 提取最后一个时间戳之后的文本
                var lastMatch = matches[^1];
                var text = trimmed.Substring(lastMatch.Index + lastMatch.Length).Trim();
                if (string.IsNullOrEmpty(text)) continue;

                // 支持同一行多个时间戳
                foreach (Match match in matches)
                {
                    if (TryParseLrcTime(match, out var time))
                        lines.Add(new LyricLine { Time = time, Text = text });
                }
            }

            return lines.OrderBy(l => l.Time).ToList();
        }

        private static bool TryParseLrcTime(Match match, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            if (!match.Success) return false;

            try
            {
                int minutes = int.Parse(match.Groups[1].Value);
                int seconds = int.Parse(match.Groups[2].Value);
                int ms = 0;
                if (match.Groups[3].Success)
                {
                    var msStr = match.Groups[3].Value.PadRight(3, '0')[..3];
                    ms = int.Parse(msStr);
                }
                time = new TimeSpan(0, 0, minutes, seconds, ms);
                return true;
            }
            catch { return false; }
        }

        private void UpdateLyricsHighlight(TimeSpan position)
        {
            var ms = position.TotalMilliseconds;
            int newIndex = -1;

            for (int i = _lyrics.Count - 1; i >= 0; i--)
            {
                if (ms >= _lyrics[i].Time.TotalMilliseconds)
                {
                    newIndex = i;
                    break;
                }
            }

            if (newIndex != _currentLyricIndex)
            {
                _currentLyricIndex = newIndex;
                UpdateScrollPosition();
            }
        }

        #endregion

        #region Playback Events

        private void SetupPlaybackEvents()
        {
            var ps = MainWindow.PlaybackService;
            if (ps == null) return;

            ps.CurrentSongChanged += (_, song) =>
            {
                if (_isClosed) return;
                _currentSong = song;
                _lyrics.Clear();
                _currentLyricIndex = -1;
                _scrollOffset = 0;
                _targetScrollOffset = 0;
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (_isClosed) return;
                    SongTitleText.Text = "";
                    ArtistText.Text = "";
                    AlbumText.Text = "";
                    _lineHeights.Clear();
                    _lineOffsets.Clear();
                    LoadCurrentSong();
                    _ = LoadLyricsAsync();
                });
            };

            ps.PositionChanged += (_, position) =>
            {
                if (_isClosed) return;
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (_isClosed) return;
                    UpdateProgress(position);
                    UpdateLyricsHighlight(position);
                });
            };

            ps.DurationChanged += (_, duration) =>
            {
                if (_isClosed) return;
                DispatcherQueue.TryEnqueue(() => { if (!_isClosed) TotalTimeText.Text = FormatTime(duration); });
            };

            ps.PlaybackStateChanged += (_, isPlaying) =>
            {
                if (_isClosed) return;
                DispatcherQueue.TryEnqueue(() => { if (!_isClosed) PlayPauseIcon.Glyph = isPlaying ? "\uE769" : "\uE768"; });
            };
        }

        private void UpdateProgress(TimeSpan position)
        {
            // 拖拽期间不更新进度条，避免跳动
            if (_isProgressDragging) return;
            var duration = MainWindow.PlaybackService?.GetDuration() ?? TimeSpan.Zero;
            if (duration.TotalSeconds > 0)
                ProgressSlider.Value = (position.TotalSeconds / duration.TotalSeconds) * 100;
            CurrentTimeText.Text = FormatTime(position);
        }

        private static string FormatTime(TimeSpan time) => $"{(int)time.TotalMinutes:D2}:{time.Seconds:D2}";

        #endregion

        #region UI Handlers

        private void ProgressSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isProgressDragging && MainWindow.PlaybackService != null)
            {
                var duration = MainWindow.PlaybackService.GetDuration();
                if (duration.TotalSeconds > 0)
                    MainWindow.PlaybackService.Seek(TimeSpan.FromSeconds(e.NewValue / 100 * duration.TotalSeconds));
            }
        }

        private void ProgressSlider_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _isProgressDragging = true;
        }

        private void ProgressSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _isProgressDragging = false;
            if (MainWindow.PlaybackService == null) return;
            var duration = MainWindow.PlaybackService.GetDuration();
            if (duration.TotalSeconds > 0)
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
            if (MainWindow.PlaybackService != null) await MainWindow.PlaybackService.PreviousAsync();
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
            => MainWindow.PlaybackService?.TogglePlayPause();

        private async void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.PlaybackService != null) await MainWindow.PlaybackService.NextAsync();
        }

        private void RepeatButton_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.PlaybackService?.ToggleRepeat();
            var mode = MainWindow.PlaybackService?.GetRepeatMode();
            RepeatIcon.Glyph = mode == RepeatMode.One ? "\uE8ED" : "\uE8EE";
            RepeatIcon.Opacity = mode == RepeatMode.None ? 0.4 : 1.0;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        #endregion
    }
}