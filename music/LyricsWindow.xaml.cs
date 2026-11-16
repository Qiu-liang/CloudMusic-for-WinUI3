using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Windows.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Graphics.Display;
using Windows.UI;
using music.Core.Mvvm;
using music.Core.ViewModels;
using music.Models;
using music.Services;

namespace music
{
    public sealed partial class LyricsWindow : Window
    {
        private readonly LyricsWindowViewModel _viewModel;
        private IReadOnlyList<LyricLine> _lyrics => _viewModel.Lyrics;
        private int _currentLyricIndex => _viewModel.CurrentLyricIndex;
        private bool _isProgressDragging;
        private bool _syncingVolume;

        private float _scrollOffset;
        private float _targetScrollOffset;
        private float _scrollVelocity;            // 弹簧滚动的速度状态
        private float _lyricsWidth = 800;
        private float _lyricsHeight = 600;
        private readonly List<float> _lineHeights = new();   // 行总高（含译文与行距）
        private readonly List<float> _lineOffsets = new();    // 行内容顶部偏移
        private readonly List<float> _lineMainHeights = new();// 原文文字高度（用于定位译文）
        private bool _canvasReady;

        // 手动滚动状态：滚动时暂停自动滚动，空闲后恢复
        private bool _isUserScrolling;
        private DateTime _lastUserScrollAt;
        private DispatcherTimer? _userScrollIdleTimer;
        private static readonly TimeSpan UserScrollResumeDelay = TimeSpan.FromSeconds(4);

        // 长按检测：区分点击跳转与长按弹出菜单
        private DispatcherTimer? _longPressTimer;
        private Windows.Foundation.Point _pointerDownPos;
        private int _pointerDownLineIndex = -1;
        private bool _longPressFired;

        // 缓存的文本格式与复用画刷，避免在 Draw 中重复创建
        private CanvasTextFormat? _fmtCurrent;
        private CanvasTextFormat? _fmtNear;
        private CanvasTextFormat? _fmtMeasure;
        private CanvasTextFormat? _fmtTranslation;
        private CanvasTextFormat? _fmtNoLyrics;
        private CanvasSolidColorBrush? _reusableBrush;

        // 歌词行排版与双纹理（清晰 + 预模糊）LRU 缓存
        private sealed class LineRenderCache : IDisposable
        {
            public CanvasTextLayout? CurrentLayout;
            public CanvasTextLayout? TranslationLayout;
            public CanvasTextLayout? NearLayout;
            public CanvasRenderTarget? SharpBitmap;
            public CanvasRenderTarget? BlurredBitmap;
            public float OriginX;
            public float OriginY;
            public float ContentW;
            public float ContentH;
            public bool BitmapsAttempted;
            public LinkedListNode<int>? LruNode;

            public void DisposeBitmaps()
            {
                SharpBitmap?.Dispose();
                SharpBitmap = null;
                BlurredBitmap?.Dispose();
                BlurredBitmap = null;
                BitmapsAttempted = false;
            }

            public void Dispose()
            {
                DisposeBitmaps();
                CurrentLayout?.Dispose();
                CurrentLayout = null;
                TranslationLayout?.Dispose();
                TranslationLayout = null;
                NearLayout?.Dispose();
                NearLayout = null;
            }
        }

        private readonly List<LineRenderCache> _lineCache = new();
        private readonly LinkedList<int> _bitmapLru = new();
        private readonly HashSet<int> _pendingBakeIndices = new();
        private bool _isBakeScheduled;
        private int _layoutGeneration;
        private const int MaxCachedBitmapLines = 36;
        private const float BlurPad = 10f;
        private const float MaxNonCurrentBlur = 2.2f;
        private float _cachedLayoutWidth = -1f;

        // 模糊专辑封面背景缓存
        private CanvasRenderTarget? _blurredBg;
        private bool _isClosed = false;

        // VSync 对齐的按需动画循环（替代固定 16ms DispatcherTimer）
        private bool _isAnimationLoopActive;
        private long _lastRenderTimestamp;

        // 进度条文本缓存，避免秒数未变时重复触发文本排版
        private double _lastDurationSeconds = -1;
        private int _lastPosSeconds = -1;
        private int _lastRemainingSeconds = -1;

        // Win2D 不支持逗号分隔的 fallback，使用系统默认 + 手动检测
        private const string PrimaryFont = "Segoe UI Variable";
        private const string FallbackFont = "Microsoft YaHei UI";
        private string _activeFont = PrimaryFont;

        // 字号与间距 —— 随画布高度等比缩放，对齐参考图（同屏约 13~14 行、当前行略加粗/微大）
        private float _lyricFontSize = 41f;            // 非当前行
        private float _lyricFontSizeCurrent = 44f;     // 当前行
        private float _lyricFontSizeTranslation = 21f; // 当前行译文
        private float _lyricLineGap = 13f;             // 行间额外空白（px）
        private float _lyricTranslationGap = 10f;      // 原文与译文间距
        private float _cachedFormatFontSize = -1f;
        private const float LeftMargin = 0f;
        private const float RightMargin = 0f;          // 右侧留白由 LyricsPanel.Margin.Right 响应式控制
        private const float AnchorRatio = 0.45f;       // 当前行锚定在画布 45% 高度处

        private float _dpiScale = 1f;

        // 当前行切换时的弹性放大动画（Apple Music 式"浮现"效果）
        private float _currentLineScale = 1f;
        private float _currentLineScaleVel;
        private long _lineSwitchAt = -10000;   // 切行时刻（TickCount ms），用于浮现渐变

        private AppWindow? _appWindow;                 // 窗口 presenter，用于全屏切换

        // 从专辑封面提取的主色，用于背景染色
        private Color _dominantColor = Color.FromArgb(255, 96, 96, 118);

        internal LyricsWindow(LyricsWindowViewModel viewModel)
        {
            _viewModel = viewModel;
            InitializeComponent();

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            _appWindow.Resize(new SizeInt32 { Width = 1400, Height = 900 });
            _appWindow.Move(new PointInt32 { X = 100, Y = 50 });
            _appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            _appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;

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

            _viewModel.SongReset += ViewModel_SongReset;
            _viewModel.LyricsLoaded += ViewModel_LyricsLoaded;
            _viewModel.ActiveLineChanged += ViewModel_ActiveLineChanged;
            _viewModel.CoverBytesLoaded += ViewModel_CoverBytesLoaded;
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            _viewModel.Initialize();

            // Slider 内部会把指针按下/抬起标记为已处理，XAML 直接订阅不生效，
            // 必须 AddHandler(handledEventsToo: true) 才能收到，否则拖动状态永远无法建立
            ProgressSlider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(ProgressSlider_PointerPressed), true);
            ProgressSlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(ProgressSlider_PointerReleased), true);
            ProgressSlider.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(ProgressSlider_PointerCaptureLost), true);

            Closed += OnWindowClosed;
        }

        private void OnWindowClosed(object sender, WindowEventArgs args)
        {
            _isClosed = true;
            StopAnimationLoop();

            _viewModel.SongReset -= ViewModel_SongReset;
            _viewModel.LyricsLoaded -= ViewModel_LyricsLoaded;
            _viewModel.ActiveLineChanged -= ViewModel_ActiveLineChanged;
            _viewModel.CoverBytesLoaded -= ViewModel_CoverBytesLoaded;
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _viewModel.Dispose();

            _userScrollIdleTimer?.Stop();
            _userScrollIdleTimer = null;
            _longPressTimer?.Stop();
            _longPressTimer = null;

            // 显式释放所有缓存的 Win2D 资源
            ClearLineCache();
            DisposeTextFormatsAndBrush();
            _blurredBg?.Dispose(); _blurredBg = null;

            BackgroundCanvas?.RemoveFromVisualTree();
            LyricsCanvas?.RemoveFromVisualTree();
        }

        #region Text Format & Line Cache

        private void DisposeTextFormatsAndBrush()
        {
            _fmtCurrent?.Dispose(); _fmtCurrent = null;
            _fmtNear?.Dispose(); _fmtNear = null;
            _fmtMeasure?.Dispose(); _fmtMeasure = null;
            _fmtTranslation?.Dispose(); _fmtTranslation = null;
            _fmtNoLyrics?.Dispose(); _fmtNoLyrics = null;
            _reusableBrush?.Dispose(); _reusableBrush = null;
        }

        private void ClearLineCache()
        {
            for (int i = 0; i < _lineCache.Count; i++)
                _lineCache[i].Dispose();
            _lineCache.Clear();
            _bitmapLru.Clear();
            _pendingBakeIndices.Clear();
            _isBakeScheduled = false;
            _layoutGeneration++;
            _cachedLayoutWidth = -1f;
        }

        private bool UpdateLyricTypographyMetrics()
        {
            float h = Math.Max(300f, _lyricsHeight);
            float newFontSize = Math.Clamp(h * 0.046f, 22f, 64f);
            float newCurrent = Math.Clamp(h * 0.049f, 24f, 68f);
            float newTranslation = Math.Clamp(h * 0.023f, 13f, 32f);
            float newLineGap = Math.Clamp(h * 0.014f, 10f, 26f);
            float newTrGap = Math.Clamp(h * 0.011f, 8f, 18f);

            bool changed = Math.Abs(newFontSize - _cachedFormatFontSize) > 0.5f
                || Math.Abs(newLineGap - _lyricLineGap) > 0.5f;

            _lyricFontSize = newFontSize;
            _lyricFontSizeCurrent = newCurrent;
            _lyricFontSizeTranslation = newTranslation;
            _lyricLineGap = newLineGap;
            _lyricTranslationGap = newTrGap;
            return changed;
        }

        private void EnsureTextFormats(CanvasDevice device)
        {
            if (_fmtCurrent != null && _fmtNear != null && _fmtMeasure != null
                && _fmtTranslation != null && _fmtNoLyrics != null && _reusableBrush != null)
                return;

            DisposeTextFormatsAndBrush();

            // 当前行：放大、Bold 字重，避免字形边缘过厚
            _fmtCurrent = new CanvasTextFormat
            {
                FontFamily = _activeFont,
                FontSize = _lyricFontSizeCurrent * _dpiScale,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = CanvasHorizontalAlignment.Left,
                WordWrapping = CanvasWordWrapping.Wrap
            };

            // 非当前行：同样大字号、SemiBold，靠透明度区分主次
            _fmtNear = new CanvasTextFormat
            {
                FontFamily = _activeFont,
                FontSize = _lyricFontSize * _dpiScale,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = CanvasHorizontalAlignment.Left,
                WordWrapping = CanvasWordWrapping.Wrap
            };

            // 测量用：与当前行一致（保证字号放大时几何稳定、不跳动）
            _fmtMeasure = new CanvasTextFormat
            {
                FontFamily = _activeFont,
                FontSize = _lyricFontSizeCurrent * _dpiScale,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = CanvasHorizontalAlignment.Left,
                WordWrapping = CanvasWordWrapping.Wrap
            };

            // 译文：更小、Regular
            _fmtTranslation = new CanvasTextFormat
            {
                FontFamily = _activeFont,
                FontSize = _lyricFontSizeTranslation * _dpiScale,
                FontWeight = FontWeights.Normal,
                HorizontalAlignment = CanvasHorizontalAlignment.Left,
                WordWrapping = CanvasWordWrapping.Wrap
            };

            _fmtNoLyrics = new CanvasTextFormat
            {
                FontFamily = _activeFont,
                FontSize = 18 * _dpiScale,
                HorizontalAlignment = CanvasHorizontalAlignment.Center
            };

            _reusableBrush = new CanvasSolidColorBrush(device, Colors.White);
            _cachedFormatFontSize = _lyricFontSize;
        }

        #endregion

        #region Background (Blurred Album Art)

        private void EnsureAnimationLoop()
        {
            if (_isClosed || _isAnimationLoopActive) return;
            _isAnimationLoopActive = true;
            _lastRenderTimestamp = Stopwatch.GetTimestamp();
            CompositionTarget.Rendering += OnCompositionRendering;
        }

        private void StopAnimationLoop()
        {
            if (!_isAnimationLoopActive) return;
            _isAnimationLoopActive = false;
            CompositionTarget.Rendering -= OnCompositionRendering;
        }

        private void OnCompositionRendering(object? sender, object e)
        {
            if (_isClosed)
            {
                StopAnimationLoop();
                return;
            }

            long nowTs = Stopwatch.GetTimestamp();
            float dt = _lastRenderTimestamp <= 0
                ? 0.016f
                : Math.Clamp((float)(nowTs - _lastRenderTimestamp) / Stopwatch.Frequency, 0.001f, 0.05f);
            _lastRenderTimestamp = nowTs;

            // 自愈：每帧重申滚动目标，防止事件驱动的目标更新遗漏导致偏离锚点
            // 目标 = 行中心的布局坐标（绘制公式 lineTop = offset + anchorY - scroll
            // 已含 + anchorY，目标不能再减一次，否则高亮会停在 2 倍锚点处）
            if (!_isUserScrolling && _currentLyricIndex >= 0 && _currentLyricIndex < _lineOffsets.Count)
            {
                int idx = _currentLyricIndex;
                float centerH = (idx < _lineMainHeights.Count) ? _lineMainHeights[idx] : _lineHeights[idx] * 0.5f;
                float lineCenter = _lineOffsets[idx] + centerH * 0.5f;
                _targetScrollOffset = Math.Clamp(lineCenter, GetMinScrollTarget(), GetMaxScrollTarget());
            }

            float prevScroll = _scrollOffset;
            float prevScale = _currentLineScale;

            // 临界阻尼弹簧（闭式解，无过冲、对任意 dt 稳定），营造 Apple Music 式呼吸感滚动
            bool scrollSettled;
            (_scrollOffset, _scrollVelocity, scrollSettled) = LyricWindowHelpers.StepCriticallyDampedSpring(
                _scrollOffset, _targetScrollOffset, _scrollVelocity,
                omega: 16f, dt: dt, posEpsilon: 0.3f, velEpsilon: 2f);

            // 当前行弹性放大动画：切行时从 0.96 弹回 1.0
            bool scaleSettled;
            (_currentLineScale, _currentLineScaleVel, scaleSettled) = LyricWindowHelpers.StepCriticallyDampedSpring(
                _currentLineScale, 1f, _currentLineScaleVel,
                omega: 18f, dt: dt, posEpsilon: 0.0005f, velEpsilon: 0.01f);

            bool appearActive = Environment.TickCount64 - _lineSwitchAt < 300;
            bool changed = Math.Abs(_scrollOffset - prevScroll) > 0.01f
                || Math.Abs(_currentLineScale - prevScale) > 0.0001f;

            if (changed || !scrollSettled || !scaleSettled || appearActive)
                LyricsCanvas?.Invalidate();

            if (scrollSettled && scaleSettled && !appearActive)
                StopAnimationLoop();
        }

        private async Task BuildBlurredBackgroundAsync(byte[] bytes, string songId)
        {
            if (_isClosed || _viewModel.CurrentSong?.Id != songId || BackgroundCanvas?.Device == null)
                return;

            try
            {
                using var stream = new MemoryStream(bytes).AsRandomAccessStream();
                var bitmap = await CanvasBitmap.LoadAsync(BackgroundCanvas.Device, stream);

                if (_isClosed || _viewModel.CurrentSong?.Id != songId) { bitmap.Dispose(); return; }

                var device = BackgroundCanvas.Device;

                // 256×256 足以呈现极柔和的渐变氛围，比 512×512 减少 75% 切歌瞬间像素计算量
                const int BgSize = 256;

                var oldBg = _blurredBg;
                _blurredBg = new CanvasRenderTarget(device, BgSize, BgSize, 96);

                using (var tempTarget = new CanvasRenderTarget(device, BgSize, BgSize, 96))
                {
                    using (var tempDs = tempTarget.CreateDrawingSession())
                    {
                        var destRect = new Windows.Foundation.Rect(0, 0, BgSize, BgSize);
                        var srcRect = new Windows.Foundation.Rect(0, 0, bitmap.Size.Width, bitmap.Size.Height);
                        tempDs.DrawImage(bitmap, destRect, srcRect);
                    }

                    using var blurEffect = new GaussianBlurEffect
                    {
                        Source = tempTarget,
                        BlurAmount = 24f,
                        BorderMode = EffectBorderMode.Soft,
                        Optimization = EffectOptimization.Speed
                    };

                    // 提升饱和度，让模糊背景像 Apple Music 一样鲜艳
                    using var saturateEffect = new SaturationEffect
                    {
                        Source = blurEffect,
                        Saturation = 1.65f
                    };

                    using (var ds = _blurredBg.CreateDrawingSession())
                    {
                        var drawRect = new Windows.Foundation.Rect(-15, -15, BgSize + 30, BgSize + 30);
                        var srcRect = new Windows.Foundation.Rect(0, 0, BgSize, BgSize);
                        ds.DrawImage(saturateEffect, drawRect, srcRect);
                    }
                }

                // 提取封面主色：缩小到 4x4 求平均，用于背景染色
                ExtractDominantColor(bitmap);
                bitmap.Dispose();

                // 延迟释放旧背景，避免 Draw 回调中使用
                oldBg?.Dispose();

                if (!_isClosed)
                    BackgroundCanvas?.Invalidate();
            }
            catch (ObjectDisposedException) { /* 窗口已关闭 */ }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LyricsWindow] Blur bg error: {ex.Message}");
            }
        }

        // 将封面缩小采样求平均色，作为整窗的染色基调
        private void ExtractDominantColor(CanvasBitmap bitmap)
        {
            try
            {
                var device = BackgroundCanvas.Device;
                using var tiny = new CanvasRenderTarget(device, 4, 4, 96);
                using (var tds = tiny.CreateDrawingSession())
                {
                    tds.DrawImage(bitmap, new Windows.Foundation.Rect(0, 0, 4, 4),
                        new Windows.Foundation.Rect(0, 0, bitmap.Size.Width, bitmap.Size.Height));
                }

                var px = tiny.GetPixelColors();
                if (px.Length == 0) return;

                float r = 0, g = 0, b = 0;
                foreach (var c in px) { r += c.R; g += c.G; b += c.B; }
                int n = px.Length;
                _dominantColor = Color.FromArgb(255, (byte)(r / n), (byte)(g / n), (byte)(b / n));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LyricsWindow] Dominant color error: {ex.Message}");
            }
        }

        private void BackgroundCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            if (_isClosed) return;

            var ds = args.DrawingSession;
            var w = (float)sender.Size.Width;
            var h = (float)sender.Size.Height;

            // 窗口底色 #0C0C0E，再直接以 0.9 透明度叠合背景，与原 CanvasControl Opacity="0.9" 一致
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

                    ds.DrawImage(bg, new Windows.Foundation.Rect(drawX, drawY, drawW, drawH), srcRect, 0.9f);
                }
                else
                {
                    // 模糊封面未就绪时，用主色渐变兜底，避免黑底
                    using (var baseBrush = new CanvasSolidColorBrush(ds, Color.FromArgb(230,
                        (byte)(_dominantColor.R * 0.45f),
                        (byte)(_dominantColor.G * 0.45f),
                        (byte)(_dominantColor.B * 0.55f))))
                    {
                        ds.FillRectangle(0, 0, w, h, baseBrush);
                    }

                    var bright = Color.FromArgb(230,
                        (byte)Math.Min(255, _dominantColor.R + 60),
                        (byte)Math.Min(255, _dominantColor.G + 60),
                        (byte)Math.Min(255, _dominantColor.B + 70));
                    var dark = Color.FromArgb(230,
                        (byte)(_dominantColor.R * 0.30f),
                        (byte)(_dominantColor.G * 0.30f),
                        (byte)(_dominantColor.B * 0.40f));
                    using var fallback = new CanvasLinearGradientBrush(ds, new[]
                    {
                        new CanvasGradientStop { Position = 0f, Color = bright },
                        new CanvasGradientStop { Position = 1f, Color = dark }
                    });
                    fallback.StartPoint = new System.Numerics.Vector2(0, 0);
                    fallback.EndPoint = new System.Numerics.Vector2(w, h);
                    ds.FillRectangle(0, 0, w, h, fallback);
                }
            }
            catch (ObjectDisposedException) { /* 资源已释放，跳过绘制 */ }

            // 冷色主调叠加，避免浅色封面让整页泛白
            using (var overlay = new CanvasSolidColorBrush(ds, Color.FromArgb(68,
                (byte)(_dominantColor.R * 0.20f),
                (byte)(_dominantColor.G * 0.24f),
                (byte)(_dominantColor.B * 0.34f))))
            {
                ds.FillRectangle(0, 0, w, h, overlay);
            }

            // 将原 XAML 中的两层全屏渐变暗角合并至此，仅在切歌/缩放时绘制一次
            using (var vVignette = new CanvasLinearGradientBrush(ds, new[]
            {
                new CanvasGradientStop { Position = 0f, Color = Color.FromArgb(0x14, 0, 0, 0) },
                new CanvasGradientStop { Position = 0.55f, Color = Color.FromArgb(0x1C, 0, 0, 0) },
                new CanvasGradientStop { Position = 1f, Color = Color.FromArgb(0x47, 0, 0, 0) }
            }))
            {
                vVignette.StartPoint = new System.Numerics.Vector2(0, 0);
                vVignette.EndPoint = new System.Numerics.Vector2(0, h);
                ds.FillRectangle(0, 0, w, h, vVignette);
            }

            using (var hVignette = new CanvasLinearGradientBrush(ds, new[]
            {
                new CanvasGradientStop { Position = 0f, Color = Color.FromArgb(0x38, 0, 0, 0) },
                new CanvasGradientStop { Position = 0.45f, Color = Color.FromArgb(0x10, 0, 0, 0) },
                new CanvasGradientStop { Position = 0.75f, Color = Color.FromArgb(0x00, 0, 0, 0) },
                new CanvasGradientStop { Position = 1f, Color = Color.FromArgb(0x08, 0, 0, 0) }
            }))
            {
                hVignette.StartPoint = new System.Numerics.Vector2(0, 0);
                hVignette.EndPoint = new System.Numerics.Vector2(w, 0);
                ds.FillRectangle(0, 0, w, h, hVignette);
            }
        }

        #endregion

        #region Lyrics Canvas

        private void LyricsCanvas_CreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
        {
            _lyricsWidth = (float)sender.Size.Width;
            _lyricsHeight = (float)sender.Size.Height;
            _canvasReady = true;

            bool metricsChanged = UpdateLyricTypographyMetrics();
            if (args.Reason != CanvasCreateResourcesReason.FirstTime || metricsChanged)
            {
                DisposeTextFormatsAndBrush();
                ClearLineCache();
            }

            EnsureTextFormats(sender.Device);
            RecalculateLayout();
        }

        private void TouchBitmapLru(int index, LineRenderCache entry)
        {
            if (entry.LruNode != null)
            {
                _bitmapLru.Remove(entry.LruNode);
                _bitmapLru.AddLast(entry.LruNode);
                return;
            }

            entry.LruNode = _bitmapLru.AddLast(index);
            while (_bitmapLru.Count > MaxCachedBitmapLines)
            {
                var oldest = _bitmapLru.First;
                if (oldest == null) break;
                int evictIndex = oldest.Value;
                _bitmapLru.RemoveFirst();
                if (evictIndex >= 0 && evictIndex < _lineCache.Count)
                {
                    _lineCache[evictIndex].LruNode = null;
                    _lineCache[evictIndex].DisposeBitmaps();
                }
            }
        }

        private void QueueBakeRange(int centerIndex, int radius = 4)
        {
            if (_isClosed || _lineCache.Count == 0) return;

            int start = Math.Max(0, centerIndex - radius);
            int end = Math.Min(_lineCache.Count - 1, centerIndex + radius);
            for (int i = start; i <= end; i++)
            {
                if (!_lineCache[i].BitmapsAttempted)
                    _pendingBakeIndices.Add(i);
            }

            if (_pendingBakeIndices.Count > 0 && !_isBakeScheduled)
            {
                _isBakeScheduled = true;
                int gen = _layoutGeneration;
                float maxW = _cachedLayoutWidth;
                DispatcherQueue.TryEnqueue(() => BakePendingBitmaps(gen, maxW));
            }
        }

        // 在 Draw 回调之外的 UI 帧间隙安全烘焙离屏清晰/模糊纹理，避免与 CanvasControl.Draw 的 DrawingSession 冲突
        private void BakePendingBitmaps(int generation, float maxW)
        {
            if (_isClosed || generation != _layoutGeneration) return;
            _isBakeScheduled = false;

            if (LyricsCanvas == null || !_canvasReady || _fmtNear == null || _reusableBrush == null || maxW <= 0)
            {
                _pendingBakeIndices.Clear();
                return;
            }

            if (_pendingBakeIndices.Count == 0) return;

            var indices = _pendingBakeIndices.ToArray();
            _pendingBakeIndices.Clear();

            var device = LyricsCanvas.Device;
            float dpi = LyricsCanvas.Dpi > 0 ? LyricsCanvas.Dpi : 96f;
            bool bakedAny = false;

            foreach (int index in indices)
            {
                if (_isClosed || generation != _layoutGeneration) break;
                if (index < 0 || index >= _lineCache.Count || index >= _lyrics.Count) continue;

                var entry = _lineCache[index];
                if (entry.BitmapsAttempted) continue;
                entry.BitmapsAttempted = true;

                try
                {
                    entry.NearLayout ??= new CanvasTextLayout(device, _lyrics[index].Text, _fmtNear, maxW, float.MaxValue);
                    var drawBounds = entry.NearLayout.DrawBounds;
                    if (drawBounds.Width <= 0 || drawBounds.Height <= 0) continue;

                    float originX = (float)drawBounds.X - 2f;
                    float originY = (float)drawBounds.Y - 2f;
                    float contentW = MathF.Ceiling((float)drawBounds.Width + 4f);
                    float contentH = MathF.Ceiling((float)drawBounds.Height + 4f);

                    var sharp = new CanvasRenderTarget(device, contentW, contentH, dpi);
                    using (var sds = sharp.CreateDrawingSession())
                    {
                        sds.Clear(Colors.Transparent);
                        _reusableBrush.Color = Colors.White;
                        sds.DrawTextLayout(entry.NearLayout, -originX, -originY, _reusableBrush);
                    }

                    var blurred = new CanvasRenderTarget(device, contentW + BlurPad * 2f, contentH + BlurPad * 2f, dpi);
                    using (var bds = blurred.CreateDrawingSession())
                    {
                        bds.Clear(Colors.Transparent);
                        using var blur = new GaussianBlurEffect
                        {
                            Source = sharp,
                            BlurAmount = MaxNonCurrentBlur * _dpiScale,
                            BorderMode = EffectBorderMode.Soft,
                            Optimization = EffectOptimization.Speed
                        };
                        bds.DrawImage(blur, BlurPad, BlurPad);
                    }

                    entry.OriginX = originX;
                    entry.OriginY = originY;
                    entry.ContentW = contentW;
                    entry.ContentH = contentH;
                    entry.SharpBitmap = sharp;
                    entry.BlurredBitmap = blurred;

                    TouchBitmapLru(index, entry);
                    bakedAny = true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LyricsWindow] Bake bitmap error at line {index}: {ex.Message}");
                }
            }

            if (bakedAny && !_isClosed && generation == _layoutGeneration)
                LyricsCanvas?.Invalidate();
        }

        private void LyricsCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            if (_isClosed) return;

            var ds = args.DrawingSession;
            _lyricsWidth = (float)sender.Size.Width;
            _lyricsHeight = (float)sender.Size.Height;

            EnsureTextFormats(sender.Device);

            if (_lyrics.Count == 0 || _fmtNoLyrics == null || _reusableBrush == null)
            {
                if (_fmtNoLyrics != null && _reusableBrush != null)
                {
                    _reusableBrush.Color = Color.FromArgb(80, 255, 255, 255);
                    ds.DrawText("暂无歌词", _lyricsWidth / 2, _lyricsHeight / 2, _reusableBrush, _fmtNoLyrics);
                }
                return;
            }

            // 当前行锚定位置
            float anchorY = _lyricsHeight * AnchorRatio;
            float fadeZone = _lyricsHeight * 0.5f;
            float maxW = Math.Max(1, _lyricsWidth - LeftMargin - RightMargin);
            float edgeFade = _lyricsHeight * 0.16f;   // 上下边缘渐隐区高度
            var device = sender.Device;

            for (int i = 0; i < _lyrics.Count; i++)
            {
                if (i >= _lineOffsets.Count || i >= _lineHeights.Count || i >= _lineCache.Count) continue;

                // 行顶部的屏幕 Y 坐标
                float lineTop = _lineOffsets[i] + anchorY - _scrollOffset;
                float lineH = _lineHeights[i];
                // 超出可见区域则跳过，超过底边后直接结束循环
                if (lineTop + lineH < -50) continue;
                if (lineTop > _lyricsHeight + 50) break;

                // 基于行中心到锚点的距离计算焦点淡出
                float lineCenter = lineTop + lineH / 2f;
                float dist = Math.Abs(lineCenter - anchorY);
                float fade = 1f - Math.Clamp(dist / fadeZone, 0f, 1f);
                fade = fade * fade;  // 二次曲线，更自然的淡出

                // 上下边缘渐变蒙版：靠近画布上/下缘的行整体淡出，自然隐藏未完全显示的歌词
                float edge = 1f;
                if (lineCenter < edgeFade)
                    edge = Math.Clamp(lineCenter / edgeFade, 0f, 1f);
                else if (lineCenter > _lyricsHeight - edgeFade)
                    edge = Math.Clamp((_lyricsHeight - lineCenter) / edgeFade, 0f, 1f);
                edge = edge * edge;

                bool isCurrent = (i == _currentLyricIndex);
                float mainH = (i < _lineMainHeights.Count) ? _lineMainHeights[i] : lineH;
                float textY = lineTop;
                var cacheEntry = _lineCache[i];

                if (isCurrent)
                {
                    var currentLayout = cacheEntry.CurrentLayout ??=
                        new CanvasTextLayout(device, _lyrics[i].Text, _fmtCurrent!, maxW, float.MaxValue);

                    // 浮现进度：切行后 250ms 内从"非当前行外观"平滑过渡到完全高亮，
                    // 起始 alpha 与锚点处非当前行一致（150），消除切换瞬间的跳变
                    float appear = Math.Clamp((Environment.TickCount64 - _lineSwitchAt) / 250f, 0f, 1f);
                    appear = appear * appear * (3f - 2f * appear); // smoothstep

                    // 切行时当前行从 0.96 弹性放大到 1.0，缩放锚点在行块左侧垂直中心
                    ds.Transform = System.Numerics.Matrix3x2.CreateScale(
                        _currentLineScale, new System.Numerics.Vector2(LeftMargin, textY + mainH * 0.5f));

                    byte shadowA = (byte)Math.Clamp(72f * appear * edge, 0f, 255f);
                    if (shadowA > 0)
                    {
                        _reusableBrush.Color = Color.FromArgb(shadowA, 0, 0, 0);
                        ds.DrawTextLayout(currentLayout, LeftMargin, textY + 2, _reusableBrush);
                    }

                    byte mainA = (byte)Math.Clamp((150f + 105f * appear) * edge, 0f, 255f);
                    if (mainA > 0)
                    {
                        _reusableBrush.Color = Color.FromArgb(mainA, 255, 255, 255);
                        ds.DrawTextLayout(currentLayout, LeftMargin, textY, _reusableBrush);
                    }

                    // 译文：当前行下方，半透明白，随浮现进度渐入
                    var tr = _lyrics[i].Translation;
                    if (!string.IsNullOrEmpty(tr) && _fmtTranslation != null)
                    {
                        var trLayout = cacheEntry.TranslationLayout ??=
                            new CanvasTextLayout(device, tr, _fmtTranslation, maxW, float.MaxValue);
                        float trY = textY + mainH + _lyricTranslationGap;
                        byte trA = (byte)Math.Clamp((140f + 55f * appear) * edge, 0f, 255f);
                        if (trA > 0)
                        {
                            _reusableBrush.Color = Color.FromArgb(trA, 255, 255, 255);
                            ds.DrawTextLayout(trLayout, LeftMargin, trY, _reusableBrush);
                        }
                    }

                    ds.Transform = System.Numerics.Matrix3x2.Identity;
                }
                else
                {
                    byte alpha = (byte)Math.Clamp(fade * edge * 165f, 0f, 165f);
                    if (alpha == 0) continue;

                    float blurAmount = Math.Clamp((dist / fadeZone - 0.04f) * 3.2f, 0f, MaxNonCurrentBlur) * _dpiScale;

                    if (cacheEntry.SharpBitmap != null && cacheEntry.BlurredBitmap != null)
                    {
                        TouchBitmapLru(i, cacheEntry);

                        float baseOpacity = alpha / 255f;
                        float maxBlur = MaxNonCurrentBlur * _dpiScale;
                        float blurT = (blurAmount <= 0.2f || maxBlur <= 0.2f)
                            ? 0f
                            : Math.Clamp((blurAmount - 0.2f) / (maxBlur - 0.2f), 0f, 1f);

                        if (blurT < 0.99f)
                        {
                            float sharpOpacity = baseOpacity * (1f - blurT);
                            if (sharpOpacity > 0.005f)
                            {
                                var dest = new Windows.Foundation.Rect(
                                    LeftMargin + cacheEntry.OriginX,
                                    textY + cacheEntry.OriginY,
                                    cacheEntry.ContentW,
                                    cacheEntry.ContentH);
                                ds.DrawImage(cacheEntry.SharpBitmap, dest, cacheEntry.SharpBitmap.Bounds, sharpOpacity);
                            }
                        }

                        if (blurT > 0.01f)
                        {
                            float blurOpacity = baseOpacity * blurT;
                            if (blurOpacity > 0.005f)
                            {
                                var dest = new Windows.Foundation.Rect(
                                    LeftMargin + cacheEntry.OriginX - BlurPad,
                                    textY + cacheEntry.OriginY - BlurPad,
                                    cacheEntry.ContentW + BlurPad * 2f,
                                    cacheEntry.ContentH + BlurPad * 2f);
                                ds.DrawImage(cacheEntry.BlurredBitmap, dest, cacheEntry.BlurredBitmap.Bounds, blurOpacity);
                            }
                        }
                    }
                    else
                    {
                        // 纹理尚未烘焙完成时，立即回退为缓存的矢量排版，确保非当前行任何时刻都不会消失
                        var nearLayout = cacheEntry.NearLayout ??=
                            new CanvasTextLayout(device, _lyrics[i].Text, _fmtNear!, maxW, float.MaxValue);
                        _reusableBrush.Color = Color.FromArgb(alpha, 255, 255, 255);
                        ds.DrawTextLayout(nearLayout, LeftMargin, textY, _reusableBrush);

                        if (!cacheEntry.BitmapsAttempted)
                            QueueBakeRange(i);
                    }
                }
            }

            // 滚动目标完全由事件驱动（UpdateScrollPosition），Draw 不回写目标，
            // 避免与弹簧形成每帧互动
        }

        private void LyricsCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            _lyricsWidth = (float)e.NewSize.Width;
            _lyricsHeight = (float)e.NewSize.Height;

            if (_canvasReady && LyricsCanvas != null)
            {
                bool typographyChanged = UpdateLyricTypographyMetrics();
                if (typographyChanged)
                    DisposeTextFormatsAndBrush();

                EnsureTextFormats(LyricsCanvas.Device);
                float maxW = Math.Max(1, _lyricsWidth - LeftMargin - RightMargin);
                if (typographyChanged || Math.Abs(maxW - _cachedLayoutWidth) > 0.5f || _lineCache.Count != _lyrics.Count)
                {
                    RecalculateLayout();
                }
                else
                {
                    UpdateScrollPosition();
                }
                LyricsCanvas.Invalidate();
            }
        }

        private void RecalculateLayout()
        {
            if (LyricsCanvas == null || _lyricsWidth <= 0 || !_canvasReady || _fmtMeasure == null) return;

            var device = LyricsCanvas.Device;
            float maxW = Math.Max(1, _lyricsWidth - LeftMargin - RightMargin);

            _lineHeights.Clear();
            _lineOffsets.Clear();
            _lineMainHeights.Clear();
            ClearLineCache();
            _cachedLayoutWidth = maxW;

            float total = 0;

            foreach (var lyric in _lyrics)
            {
                var entry = new LineRenderCache();
                var layout = new CanvasTextLayout(device, lyric.Text, _fmtMeasure, maxW, float.MaxValue);
                float mainH = (float)layout.LayoutBounds.Height;
                entry.CurrentLayout = layout;

                // 为含译文的行预留译文高度，保证当前行切换时几何稳定、不跳动
                float extra = 0f;
                if (!string.IsNullOrEmpty(lyric.Translation) && _fmtTranslation != null)
                {
                    var trLayout = new CanvasTextLayout(device, lyric.Translation, _fmtTranslation, maxW, float.MaxValue);
                    extra = _lyricTranslationGap + (float)trLayout.LayoutBounds.Height;
                    entry.TranslationLayout = trLayout;
                }

                // 行高 = 原文高度 + 译文预留 + 动态行距，保证不会重叠
                float lineH = mainH + extra + _lyricLineGap;
                _lineMainHeights.Add(mainH);
                _lineHeights.Add(lineH);
                _lineOffsets.Add(total);
                _lineCache.Add(entry);
                total += lineH;
            }

            UpdateScrollPosition();
            QueueBakeRange(_currentLyricIndex >= 0 ? _currentLyricIndex : 0, radius: 10);
        }

        // 允许首/末行也能滚动到锚点（两端留白），贴近 Apple Music 行为
        // 锚定基准：原文文字中心（不含译文），保证高亮文字本身落在锚点上
        private float GetLineCenterY(int index)
        {
            float mainH = (index < _lineMainHeights.Count) ? _lineMainHeights[index] : _lineHeights[index] * 0.5f;
            return _lineOffsets[index] + mainH * 0.5f;
        }

        private float GetMinScrollTarget()
        {
            if (_lineOffsets.Count == 0 || _lineHeights.Count != _lineOffsets.Count) return 0;
            return GetLineCenterY(0);
        }

        private float GetMaxScrollTarget()
        {
            if (_lineOffsets.Count == 0 || _lineHeights.Count != _lineOffsets.Count) return 0;
            int last = _lineOffsets.Count - 1;
            return GetLineCenterY(last);
        }

        private void UpdateScrollPosition()
        {
            if (_currentLyricIndex < 0 || _currentLyricIndex >= _lineOffsets.Count) return;
            if (_isUserScrolling) return;  // 手动滚动期间不抢回视图
            _targetScrollOffset = Math.Clamp(GetLineCenterY(_currentLyricIndex), GetMinScrollTarget(), GetMaxScrollTarget());
            EnsureAnimationLoop();
        }

        #endregion

        #region Interactions (Tap / Long-press / Manual scroll)

        // 命中测试：返回屏幕 Y 对应的歌词行索引，未命中返回 -1
        private int HitTestLine(float y)
        {
            float anchorY = _lyricsHeight * AnchorRatio;
            float contentY = y - anchorY + _scrollOffset;
            return LyricWindowHelpers.HitTestLyricLine(_lineOffsets, _lineHeights, contentY);
        }

        private void LyricsCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_lyrics.Count == 0 || _lineOffsets.Count == 0) return;

            // 仅左键触发点按/长按；右键由 ContextRequested 处理
            var point = e.GetCurrentPoint((UIElement)sender);
            if (point.Properties.IsRightButtonPressed) return;

            _pointerDownPos = point.Position;
            _pointerDownLineIndex = HitTestLine((float)point.Position.Y);
            _longPressFired = false;

            if (_pointerDownLineIndex < 0) return;

            _longPressTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _longPressTimer.Tick -= LongPressTimer_Tick;
            _longPressTimer.Tick += LongPressTimer_Tick;
            _longPressTimer.Start();
        }

        private void LongPressTimer_Tick(object? sender, object e)
        {
            _longPressTimer?.Stop();
            _longPressFired = true;
            if (_pointerDownLineIndex >= 0 && _pointerDownLineIndex < _lyrics.Count)
                ShowLineMenu(_pointerDownLineIndex, _pointerDownPos);
        }

        private void LyricsCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _longPressTimer?.Stop();
            if (_longPressFired) { _longPressFired = false; return; }

            if (_pointerDownLineIndex < 0) return;

            // 释放点与按下点偏移过大视为拖动，不触发跳转
            var pos = e.GetCurrentPoint((UIElement)sender).Position;
            double dx = pos.X - _pointerDownPos.X;
            double dy = pos.Y - _pointerDownPos.Y;
            if (dx * dx + dy * dy > 100) { _pointerDownLineIndex = -1; return; }

            // 命中行仍以当前指针位置为准，避免滚动后错位
            int idx = HitTestLine((float)pos.Y);
            _viewModel.SeekToLine(idx);
            _pointerDownLineIndex = -1;
        }

        private void LyricsCanvas_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
        {
            if (_lyrics.Count == 0) return;
            _longPressTimer?.Stop();
            if (!e.TryGetPosition(sender, out var pos)) return;
            int idx = HitTestLine((float)pos.Y);
            if (idx >= 0 && idx < _lyrics.Count)
            {
                ShowLineMenu(idx, pos);
                e.Handled = true;
            }
        }

        private void ShowLineMenu(int index, Windows.Foundation.Point position)
        {
            string text = _lyrics[index].Text;
            var translation = _lyrics[index].Translation;
            string shareText = string.IsNullOrEmpty(translation) ? text : $"{text}\n{translation}";

            var flyout = new MenuFlyout();

            var copyItem = new MenuFlyoutItem { Text = "复制歌词", Icon = new FontIcon { Glyph = "" } };
            copyItem.Click += (_, __) => CopyText(shareText);
            flyout.Items.Add(copyItem);

            var shareItem = new MenuFlyoutItem { Text = "分享", Icon = new FontIcon { Glyph = "" } };
            shareItem.Click += (_, __) => ShareText(shareText);
            flyout.Items.Add(shareItem);

            flyout.ShowAt(LyricsCanvas, new FlyoutShowOptions { Position = position });
        }

        private void CopyText(string text)
        {
            try
            {
                var pkg = new Windows.ApplicationModel.DataTransfer.DataPackage();
                pkg.SetText(text);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LyricsWindow] Copy error: {ex.Message}");
            }
        }

        private void ShareText(string text)
        {
            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                var interop = Windows.ApplicationModel.DataTransfer.DataTransferManager.As<IDataTransferManagerInterop>();
                Guid dtmIid = new Guid("a5caee9b-8708-49d1-8d36-67d25a8da00c");
                IntPtr opHandle = interop.GetForWindow(hwnd, ref dtmIid);
                var dtm = WinRT.MarshalInterface<Windows.ApplicationModel.DataTransfer.DataTransferManager>.FromAbi(opHandle);

                void Handler(Windows.ApplicationModel.DataTransfer.DataTransferManager s,
                             Windows.ApplicationModel.DataTransfer.DataRequestedEventArgs args)
                {
                    args.Request.Data.SetText(text);
                    args.Request.Data.Properties.Title = "分享歌词";
                    dtm.DataRequested -= Handler;
                }
                dtm.DataRequested += Handler;
                interop.ShowShareUIForWindow(hwnd);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LyricsWindow] Share error: {ex.Message}");
                CopyText(text); // 分享不可用时回退为复制
            }
        }

        // 鼠标滚轮手动滚动：暂停自动滚动并显示"回到当前"
        private void LyricsCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (_lyrics.Count == 0 || _lineOffsets.Count == 0) return;

            int delta = e.GetCurrentPoint((UIElement)sender).Properties.MouseWheelDelta;
            BeginUserScroll();
            float target = _targetScrollOffset - delta;   // 向上滚（delta>0）显示更早歌词
            _targetScrollOffset = Math.Clamp(target, GetMinScrollTarget(), GetMaxScrollTarget());
            EnsureAnimationLoop();
            e.Handled = true;
        }

        private void BeginUserScroll()
        {
            _isUserScrolling = true;
            _lastUserScrollAt = DateTime.Now;
            ShowBackToCurrentButton(true);

            _userScrollIdleTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _userScrollIdleTimer.Tick -= UserScrollIdleTimer_Tick;
            _userScrollIdleTimer.Tick += UserScrollIdleTimer_Tick;
            _userScrollIdleTimer.Start();
        }

        private void UserScrollIdleTimer_Tick(object? sender, object e)
        {
            if (DateTime.Now - _lastUserScrollAt >= UserScrollResumeDelay)
                ResumeAutoScroll();
        }

        private void ResumeAutoScroll()
        {
            _userScrollIdleTimer?.Stop();
            _isUserScrolling = false;
            ShowBackToCurrentButton(false);
            UpdateScrollPosition();   // 弹回当前行
            LyricsCanvas?.Invalidate();
        }

        private void BackToCurrentButton_Click(object sender, RoutedEventArgs e) => ResumeAutoScroll();

        private void ShowBackToCurrentButton(bool show)
        {
            if (BackToCurrentButton == null) return;
            if (show)
            {
                BackToCurrentButton.Visibility = Visibility.Visible;
                BackToCurrentButton.Opacity = 1;
            }
            else
            {
                BackToCurrentButton.Opacity = 0;
                BackToCurrentButton.Visibility = Visibility.Collapsed;
            }
        }

        #endregion

        #region ViewModel Event Sync

        private void ViewModel_SongReset(object? sender, Song? song)
        {
            if (_isClosed) return;

            _scrollOffset = 0;
            _targetScrollOffset = 0;
            _scrollVelocity = 0;
            _lastDurationSeconds = -1;
            _lastPosSeconds = -1;
            _lastRemainingSeconds = -1;
            _lineHeights.Clear();
            _lineOffsets.Clear();
            _lineMainHeights.Clear();
            ClearLineCache();
            StopAnimationLoop();

            SongTitleText.Text = _viewModel.SongTitle;
            ArtistAlbumText.Text = _viewModel.ArtistAlbumText;
            AlbumCoverImage.Source = !string.IsNullOrEmpty(_viewModel.CoverUrl)
                ? new BitmapImage(new Uri(_viewModel.CoverUrl))
                : null;

            SyncPlaybackControlsFromVm();
            UpdateProgress(_viewModel.Position);
            LyricsCanvas?.Invalidate();
        }

        private void ViewModel_LyricsLoaded(object? sender, EventArgs e)
        {
            if (_isClosed) return;
            _scrollOffset = 0;
            _targetScrollOffset = 0;
            RecalculateLayout();
            LyricsCanvas?.Invalidate();
        }

        private void ViewModel_ActiveLineChanged(object? sender, int newIndex)
        {
            if (_isClosed) return;
            UpdateScrollPosition();

            // 浮现动画节流：密集歌词（时间戳相邻很近）时不逐行重新触发，
            // 否则当前行会连续收缩弹跳（抽搐）。索引与滚动目标始终即时更新
            long now = Environment.TickCount64;
            if (now - _lineSwitchAt >= 300)
            {
                _currentLineScale = 0.96f;
                _currentLineScaleVel = 0f;
                _lineSwitchAt = now;
            }
            EnsureAnimationLoop();
        }

        private void ViewModel_CoverBytesLoaded(object? sender, CoverLoadedEventArgs e)
        {
            if (_isClosed) return;
            BuildBlurredBackgroundAsync(e.Bytes, e.SongId).Forget();
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_isClosed) return;
            switch (e.PropertyName)
            {
                case nameof(LyricsWindowViewModel.Position):
                    UpdateProgress(_viewModel.Position);
                    break;
                case nameof(LyricsWindowViewModel.Duration):
                    double totalSeconds = _viewModel.Duration.TotalSeconds;
                    if (Math.Abs(totalSeconds - _lastDurationSeconds) >= 0.5)
                        UpdateProgress(_viewModel.Position);
                    break;
                case nameof(LyricsWindowViewModel.IsPlaying):
                    PlayPauseIcon.Glyph = _viewModel.IsPlaying ? "\uE769" : "\uE768";
                    break;
                case nameof(LyricsWindowViewModel.IsShuffleEnabled):
                    ShuffleIcon.Opacity = _viewModel.IsShuffleEnabled ? 1.0 : 0.4;
                    break;
                case nameof(LyricsWindowViewModel.RepeatMode):
                    RepeatIcon.Glyph = _viewModel.RepeatMode == RepeatMode.One ? "\uE8ED" : "\uE8EE";
                    RepeatIcon.Opacity = _viewModel.RepeatMode == RepeatMode.None ? 0.4 : 1.0;
                    break;
                case nameof(LyricsWindowViewModel.VolumePercent):
                    _syncingVolume = true;
                    try
                    {
                        VolumeSlider.Value = _viewModel.VolumePercent;
                    }
                    finally
                    {
                        _syncingVolume = false;
                    }
                    break;
            }
        }

        private void SyncPlaybackControlsFromVm()
        {
            PlayPauseIcon.Glyph = _viewModel.IsPlaying ? "\uE769" : "\uE768";
            ShuffleIcon.Opacity = _viewModel.IsShuffleEnabled ? 1.0 : 0.4;
            RepeatIcon.Glyph = _viewModel.RepeatMode == RepeatMode.One ? "\uE8ED" : "\uE8EE";
            RepeatIcon.Opacity = _viewModel.RepeatMode == RepeatMode.None ? 0.4 : 1.0;

            _syncingVolume = true;
            try
            {
                VolumeSlider.Value = _viewModel.VolumePercent;
            }
            finally
            {
                _syncingVolume = false;
            }
        }

        private void UpdateProgress(TimeSpan position)
        {
            // 拖拽期间不更新进度条，避免跳动
            if (_isProgressDragging) return;
            var duration = _viewModel.Duration;
            double durationSeconds = duration.TotalSeconds;
            _lastDurationSeconds = durationSeconds;

            if (durationSeconds > 0)
            {
                ProgressSlider.Value = Math.Clamp(position.TotalSeconds / durationSeconds * 100, 0, 100);
                var remaining = duration > position ? duration - position : TimeSpan.Zero;
                int remSec = (int)remaining.TotalSeconds;
                if (remSec != _lastRemainingSeconds)
                {
                    _lastRemainingSeconds = remSec;
                    TotalTimeText.Text = FormatRemainingTime(remaining);
                }
            }
            else if (_lastRemainingSeconds != -2)
            {
                _lastRemainingSeconds = -2;
                TotalTimeText.Text = "-0:00";
            }

            int posSec = (int)Math.Max(0, position.TotalSeconds);
            if (posSec != _lastPosSeconds)
            {
                _lastPosSeconds = posSec;
                CurrentTimeText.Text = FormatTime(position);
            }
        }

        private static string FormatTime(TimeSpan time) => $"{(int)time.TotalMinutes}:{time.Seconds:D2}";

        private static string FormatRemainingTime(TimeSpan time) => $"-{FormatTime(time)}";

        #endregion

        #region UI Handlers

        private void ProgressSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            // 程序性赋值不处理；拖动中只预览，松手后在 PointerReleased 里统一 Seek，
            // 避免对网络音频流每帧 Seek 造成卡顿、位置回跳（表现为"拖不动"）
        }

        private void ProgressSlider_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _isProgressDragging = true;
        }

        private void ProgressSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_isProgressDragging) return;
            _isProgressDragging = false;
            SeekFromSlider();
        }

        private void ProgressSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            // 拖动中被系统抢走指针（如切窗口）也要结束拖拽并落位
            if (!_isProgressDragging) return;
            _isProgressDragging = false;
            SeekFromSlider();
        }

        private void SeekFromSlider()
        {
            var duration = _viewModel.Duration;
            if (duration.TotalSeconds > 0)
                _viewModel.Seek(TimeSpan.FromSeconds(ProgressSlider.Value / 100 * duration.TotalSeconds));
        }

        private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_syncingVolume) return;
            _viewModel.SetVolumePercent(e.NewValue);
        }

        private void ShuffleButton_Click(object sender, RoutedEventArgs e) =>
            _viewModel.ToggleShuffle();

        private void PrevButton_Click(object sender, RoutedEventArgs e) =>
            _viewModel.PreviousAsync().Forget();

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e) =>
            _viewModel.TogglePlayPause();

        private void NextButton_Click(object sender, RoutedEventArgs e) =>
            _viewModel.NextAsync().Forget();

        private void RepeatButton_Click(object sender, RoutedEventArgs e) =>
            _viewModel.ToggleRepeat();

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void LyricsButton_Click(object sender, RoutedEventArgs e) => Close();

        private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateResponsiveLayout(e.NewSize);

        private void UpdateResponsiveLayout() => UpdateResponsiveLayout(
            new Windows.Foundation.Size(RootGrid.ActualWidth, RootGrid.ActualHeight));

        private void UpdateResponsiveLayout(Windows.Foundation.Size size)
        {
            if (size.Width <= 0 || size.Height <= 0) return;

            double coverSize = Math.Max(140, Math.Min(size.Width * 0.3125, size.Height * 0.556));
            double uiScale = Math.Clamp(coverSize / 400.0, 0.72, 1.60);

            LeftContentPanel.Width = coverSize;
            LeftContentPanel.Spacing = Math.Round(22 * uiScale);
            CoverBorder.Width = coverSize;
            CoverBorder.Height = coverSize;
            CoverBorder.CornerRadius = new CornerRadius(Math.Round(6 * uiScale));

            SongInfoPanel.Spacing = Math.Round(6 * uiScale);
            SongTitleText.FontSize = Math.Round(16 * uiScale);
            ArtistAlbumText.FontSize = Math.Round(13 * uiScale);

            ControlsPanel.Width = Math.Max(190, coverSize * 0.765);
            ControlsPanel.Spacing = Math.Round(8 * uiScale);
            CurrentTimeText.FontSize = Math.Round(11 * uiScale);
            TotalTimeText.FontSize = Math.Round(11 * uiScale);
            ControlsRowGrid.Height = Math.Round(40 * uiScale);

            double smallBtnSize = Math.Round(30 * uiScale);
            double smallIconSize = Math.Round(14 * uiScale);
            VolumeButton.Width = smallBtnSize;
            VolumeButton.Height = smallBtnSize;
            VolumeIcon.FontSize = smallIconSize;
            MoreButton.Width = smallBtnSize;
            MoreButton.Height = smallBtnSize;
            MoreIcon.FontSize = smallIconSize;

            CenterButtonsPanel.Spacing = Math.Round(8 * uiScale);
            double skipBtnW = Math.Round(34 * uiScale);
            double skipBtnH = Math.Round(36 * uiScale);
            double skipIconSize = Math.Round(18 * uiScale);
            PrevButton.Width = skipBtnW;
            PrevButton.Height = skipBtnH;
            PrevIcon.FontSize = skipIconSize;
            NextButton.Width = skipBtnW;
            NextButton.Height = skipBtnH;
            NextIcon.FontSize = skipIconSize;

            double playBtnSize = Math.Round(36 * uiScale);
            PlayPauseButton.Width = playBtnSize;
            PlayPauseButton.Height = playBtnSize;
            PlayPauseIcon.FontSize = Math.Round(20 * uiScale);

            double lyricsBtnSize = Math.Round(32 * uiScale);
            double lyricsIconSize = Math.Round(18 * uiScale);
            LyricsButton.Width = lyricsBtnSize;
            LyricsButton.Height = lyricsBtnSize;
            LyricsButtonIcon.Width = lyricsIconSize;
            LyricsButtonIcon.Height = lyricsIconSize;

            LeftPanel.Margin = new Thickness(0, size.Height * 0.035, size.Width * 0.0125, size.Height * 0.02);
            LyricsPanel.Margin = new Thickness(size.Width * 0.068, 0, size.Width * 0.055, 0);
        }

        // 空白背景处按下左键即拖动窗口（窗口隐藏了标题栏，需要手动提供拖动能力）
        private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // 仅当点击落在最外层背景（未命中任何子控件）时才拖动，
            // 歌词点击跳转、按钮、滑块等不受影响
            if (!ReferenceEquals(e.OriginalSource, RootGrid)) return;
            if (!e.GetCurrentPoint(RootGrid).Properties.IsLeftButtonPressed) return;

            e.Handled = true;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        }

        private void FullscreenButton_Click(object sender, RoutedEventArgs e)
        {
            if (_appWindow == null) return;

            if (_appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
            {
                _appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
                FullscreenIcon.Glyph = "\uE740";
            }
            else
            {
                _appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
                FullscreenIcon.Glyph = "\uE73F";
            }
            UpdateResponsiveLayout();
        }

        #endregion

        // 隐藏标题栏后用于实现空白处拖动窗口
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int WM_NCLBUTTONDOWN = 0x00A1;   // 注意：0x0201 是 WM_LBUTTONDOWN，不是这个
        private const int HTCAPTION = 0x0002;
    }

    // 桌面应用调起系统”分享”面板所需的互操作接口
    [ComImport]
    [Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDataTransferManagerInterop
    {
        IntPtr GetForWindow([In] IntPtr appWindow, [In] ref Guid riid);
        void ShowShareUIForWindow(IntPtr appWindow);
    }
}