using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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

        // 缓存的文本格式，避免在 Draw 中重复创建
        private CanvasTextFormat? _fmtCurrent;
        private CanvasTextFormat? _fmtNear;
        private CanvasTextFormat? _fmtMeasure;
        private CanvasTextFormat? _fmtTranslation;
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

        // 字号与间距 —— 对齐 Apple Music：大字号 ExtraBold 排版，非当前行同样醒目
        private const float FontSize = 28f;            // 非当前行
        private const float FontSizeCurrent = 40f;     // 当前行
        private const float FontSizeTranslation = 20f; // 当前行译文
        private const float LineGap = 42f;             // 行间空白（px）
        private const float TranslationGap = 12f;      // 原文与译文间距
        private const float LeftMargin = 56f;
        private const float RightMargin = 64f;
        private const float AnchorRatio = 0.32f;       // 当前行锚定在画布 32% 高度处

        private float _dpiScale = 1f;

        // 当前行切换时的弹性放大动画（Apple Music 式"浮现"效果）
        private float _currentLineScale = 1f;
        private float _currentLineScaleVel;
        private long _lineSwitchAt = -10000;   // 切行时刻（TickCount ms），用于浮现渐变
        private double _lastRawMs = -1;        // 上一帧的原始播放位置（ms），用于单调过滤
        private long _lastTickAt = -1;         // 上次滚动定时器触发时刻，用于计算真实 dt

        private AppWindow? _appWindow;                 // 窗口 presenter，用于全屏切换

        // 从专辑封面提取的主色，用于背景染色与当前行辉光
        private Color _dominantColor = Color.FromArgb(255, 96, 96, 118);

        // LRC 解析正则：支持 [mm:ss.xx] / [mm:ss.xxx] / 多时间戳
        private static readonly Regex LrcTimeRegex = new(@"\[(\d{1,3}):(\d{2})(?:\.(\d{1,3}))?\]", RegexOptions.Compiled);

        public LyricsWindow()
        {
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

            SetupPlaybackEvents();
            LoadCurrentSong();
            _ = LoadLyricsAsync();

            // Slider 内部会把指针按下/抬起标记为已处理，XAML 直接订阅不生效，
            // 必须 AddHandler(handledEventsToo: true) 才能收到，否则拖动状态永远无法建立
            ProgressSlider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(ProgressSlider_PointerPressed), true);
            ProgressSlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(ProgressSlider_PointerReleased), true);
            ProgressSlider.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(ProgressSlider_PointerCaptureLost), true);

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
            _userScrollIdleTimer?.Stop();
            _userScrollIdleTimer = null;
            _longPressTimer?.Stop();
            _longPressTimer = null;

            // 显式释放所有缓存的 Win2D 资源
            _fmtCurrent?.Dispose(); _fmtCurrent = null;
            _fmtNear?.Dispose(); _fmtNear = null;
            _fmtMeasure?.Dispose(); _fmtMeasure = null;
            _fmtTranslation?.Dispose(); _fmtTranslation = null;
            _fmtNoLyrics?.Dispose(); _fmtNoLyrics = null;
            _measureTarget?.Dispose(); _measureTarget = null;
            _blurredBg?.Dispose(); _blurredBg = null;

            BackgroundCanvas?.RemoveFromVisualTree();
            LyricsCanvas?.RemoveFromVisualTree();
        }

        #region Text Format Cache

        private void EnsureTextFormats(CanvasDevice device)
        {
            if (_fmtCurrent != null && _fmtNear != null && _fmtMeasure != null
                && _fmtTranslation != null && _fmtNoLyrics != null)
                return;

            _fmtCurrent?.Dispose();
            _fmtNear?.Dispose();
            _fmtMeasure?.Dispose();
            _fmtTranslation?.Dispose();
            _fmtNoLyrics?.Dispose();

            // 当前行：放大、ExtraBold 字重（Apple Music 的厚重排版）
            _fmtCurrent = new CanvasTextFormat
            {
                FontFamily = _activeFont,
                FontSize = FontSizeCurrent * _dpiScale,
                FontWeight = FontWeights.ExtraBold,
                HorizontalAlignment = CanvasHorizontalAlignment.Left,
                WordWrapping = CanvasWordWrapping.Wrap
            };

            // 非当前行：同样大字号、SemiBold，靠透明度区分主次
            _fmtNear = new CanvasTextFormat
            {
                FontFamily = _activeFont,
                FontSize = FontSize * _dpiScale,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = CanvasHorizontalAlignment.Left,
                WordWrapping = CanvasWordWrapping.Wrap
            };

            // 测量用：与当前行一致（保证字号放大时几何稳定、不跳动）
            _fmtMeasure = new CanvasTextFormat
            {
                FontFamily = _activeFont,
                FontSize = FontSizeCurrent * _dpiScale,
                FontWeight = FontWeights.ExtraBold,
                HorizontalAlignment = CanvasHorizontalAlignment.Left,
                WordWrapping = CanvasWordWrapping.Wrap
            };

            // 译文：更小、Regular
            _fmtTranslation = new CanvasTextFormat
            {
                FontFamily = _activeFont,
                FontSize = FontSizeTranslation * _dpiScale,
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
            // 真实经过时间：UI 线程繁忙时定时器会延迟触发，
            // 用固定 dt 会让弹簧按"假时间"推进、收敛严重滞后
            long now = Environment.TickCount64;
            float dt = _lastTickAt < 0
                ? 0.016f
                : Math.Clamp((now - _lastTickAt) / 1000f, 0.001f, 0.05f);
            _lastTickAt = now;

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

            float prev = _scrollOffset;

            // 临界阻尼弹簧（闭式解，无过冲、对任意 dt 稳定），营造 Apple Music 式呼吸感滚动
            const float omega = 16f;            // 角频率，收敛时间≈0.4s（300-500ms 区间）
            float x = _scrollOffset - _targetScrollOffset;
            float v = _scrollVelocity;
            float exp = (float)Math.Exp(-omega * dt);
            float nx = (x + (v + omega * x) * dt) * exp;
            float nv = (v - (v + omega * x) * omega * dt) * exp;

            if (Math.Abs(nx) < 0.3f && Math.Abs(nv) < 2f)
            {
                _scrollOffset = _targetScrollOffset;
                _scrollVelocity = 0f;
            }
            else
            {
                _scrollOffset = _targetScrollOffset + nx;
                _scrollVelocity = nv;
            }

            if (Math.Abs(_scrollOffset - prev) > 0.05f)
                LyricsCanvas?.Invalidate();

            // 当前行弹性放大动画：切行时从 0.96 弹回 1.0
            {
                const float sOmega = 18f;
                // x = 当前值 - 目标值（与滚动弹簧的符号约定一致）
                float sx = _currentLineScale - 1f;
                float sv = _currentLineScaleVel;
                float sexp = (float)Math.Exp(-sOmega * dt);
                float nsx = (sx + (sv + sOmega * sx) * dt) * sexp;
                float nsv = (sv - (sv + sOmega * sx) * sOmega * dt) * sexp;

                if (Math.Abs(nsx) < 0.0005f && Math.Abs(nsv) < 0.01f)
                {
                    _currentLineScale = 1f;
                    _currentLineScaleVel = 0f;
                }
                else
                {
                    _currentLineScale = 1f + nsx;
                    _currentLineScaleVel = nsv;
                }

                // 浮现渐变（250ms）或缩放动画未完成期间持续重绘
                if (Math.Abs(_currentLineScale - 1f) > 0.0005f
                    || Environment.TickCount64 - _lineSwitchAt < 300)
                    LyricsCanvas?.Invalidate();
            }
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

                        // 渲染目标用固定尺寸：窗口构造期画布 Size 还是 0，
                        // 用它建 RT 会让"模糊背景"退化成单像素纯色（黑底）。
                        // 绘制时 BackgroundCanvas_Draw 会把这张图拉伸铺满窗口
                        const int BgSize = 512;

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
                                BlurAmount = 80f,
                                BorderMode = EffectBorderMode.Soft
                            };

                            // 提升饱和度，让模糊背景像 Apple Music 一样鲜艳
                            using var saturateEffect = new SaturationEffect
                            {
                                Source = blurEffect,
                                Saturation = 1.45f
                            };

                            using (var ds = _blurredBg.CreateDrawingSession())
                            {
                                var drawRect = new Windows.Foundation.Rect(-30, -30, BgSize + 60, BgSize + 60);
                                var srcRect = new Windows.Foundation.Rect(0, 0, BgSize, BgSize);
                                ds.DrawImage(saturateEffect, drawRect, srcRect);
                            }
                        }

                        // 提取封面主色：缩小到 4x4 求平均，用于背景染色与辉光
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
                        System.Diagnostics.Debug.WriteLine($"[LyricsWindow] Blur bg error: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LyricsWindow] Load cover error: {ex.Message}");
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
                System.Diagnostics.Debug.WriteLine($"[LyricsWindow] Dominant color error: {ex.Message}");
            }
        }

        private void BackgroundCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            if (_isClosed) return;

            var ds = args.DrawingSession;
            var w = (float)sender.Size.Width;
            var h = (float)sender.Size.Height;

            // 底色取主色调，背景未加载完成时也保持色彩氛围
            ds.Clear(Color.FromArgb(255,
                (byte)(_dominantColor.R * 0.45f),
                (byte)(_dominantColor.G * 0.45f),
                (byte)(_dominantColor.B * 0.55f)));

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

                    ds.DrawImage(bg, new Windows.Foundation.Rect(drawX, drawY, drawW, drawH), srcRect, 1f);
                }
                else
                {
                    // 模糊封面未就绪时，用主色渐变兜底，避免黑底
                    var bright = Color.FromArgb(255,
                        (byte)Math.Min(255, _dominantColor.R + 60),
                        (byte)Math.Min(255, _dominantColor.G + 60),
                        (byte)Math.Min(255, _dominantColor.B + 70));
                    var dark = Color.FromArgb(255,
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

            // 主色调轻叠加，保留鲜艳氛围同时保证歌词可读性
            using (var overlay = new CanvasSolidColorBrush(ds, Color.FromArgb(55,
                (byte)(_dominantColor.R * 0.35f),
                (byte)(_dominantColor.G * 0.35f),
                (byte)(_dominantColor.B * 0.45f))))
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

            // 当前行锚定位置
            float anchorY = _lyricsHeight * AnchorRatio;
            float fadeZone = _lyricsHeight * 0.5f;
            float maxW = _lyricsWidth - LeftMargin - RightMargin;
            float edgeFade = _lyricsHeight * 0.16f;   // 上下边缘渐隐区高度

            for (int i = 0; i < _lyrics.Count; i++)
            {
                if (i >= _lineOffsets.Count || i >= _lineHeights.Count) continue;

                // 行顶部的屏幕 Y 坐标
                float lineTop = _lineOffsets[i] + anchorY - _scrollOffset;
                float lineH = _lineHeights[i];
                // 超出可见区域则跳过
                if (lineTop + lineH < -50 || lineTop > _lyricsHeight + 50) continue;

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
                var fmt = isCurrent ? _fmtCurrent! : _fmtNear!;

                using var layout = new CanvasTextLayout(ds, _lyrics[i].Text, fmt, maxW, float.MaxValue);
                float mainH = (i < _lineMainHeights.Count) ? _lineMainHeights[i] : (float)layout.LayoutBounds.Height;

                // 原文顶部对齐到行块顶部
                float textY = lineTop;

                if (isCurrent)
                {
                    // 浮现进度：切行后 250ms 内从"非当前行外观"平滑过渡到完全高亮，
                    // 起始 alpha 与锚点处非当前行一致（150），消除切换瞬间的跳变
                    float appear = Math.Clamp((Environment.TickCount64 - _lineSwitchAt) / 250f, 0f, 1f);
                    appear = appear * appear * (3f - 2f * appear); // smoothstep

                    // 切行时当前行从 0.96 弹性放大到 1.0，缩放锚点在行块左侧垂直中心
                    ds.Transform = System.Numerics.Matrix3x2.CreateScale(
                        _currentLineScale, new System.Numerics.Vector2(LeftMargin, textY + mainH * 0.5f));

                    // 辉光：取封面主色的提亮色，多圈扩散，随浮现进度渐入
                    byte r = (byte)Math.Min(255, _dominantColor.R + 100);
                    byte g = (byte)Math.Min(255, _dominantColor.G + 100);
                    byte b = (byte)Math.Min(255, _dominantColor.B + 100);
                    byte glowA = (byte)Math.Clamp(26f * appear * edge, 0f, 255f);
                    if (glowA > 0)
                    {
                        for (int gl = 4; gl >= 1; gl--)
                        {
                            using var gb = new CanvasSolidColorBrush(ds, Color.FromArgb(glowA, r, g, b));
                            ds.DrawTextLayout(layout, LeftMargin - gl, textY, gb);
                            ds.DrawTextLayout(layout, LeftMargin + gl, textY, gb);
                            ds.DrawTextLayout(layout, LeftMargin, textY - gl, gb);
                            ds.DrawTextLayout(layout, LeftMargin, textY + gl, gb);
                        }
                    }

                    // 当前行主体：白色渐变填充，自上而下微微渐隐；alpha 随浮现进度从 150 升到 255
                    float mainA = Math.Clamp((150f + 105f * appear) * edge, 0f, 255f);
                    var grad = new CanvasLinearGradientBrush(ds, new[]
                    {
                        new CanvasGradientStop { Position = 0f, Color = Color.FromArgb((byte)mainA, 255, 255, 255) },
                        new CanvasGradientStop { Position = 1f, Color = Color.FromArgb((byte)(mainA * 0.82f), 235, 238, 255) }
                    });
                    grad.StartPoint = new System.Numerics.Vector2(LeftMargin, textY);
                    grad.EndPoint = new System.Numerics.Vector2(LeftMargin, textY + mainH);
                    ds.DrawTextLayout(layout, LeftMargin, textY, grad);
                    grad.Dispose();

                    // 译文：当前行下方，半透明白，随浮现进度渐入
                    var tr = _lyrics[i].Translation;
                    if (!string.IsNullOrEmpty(tr) && _fmtTranslation != null)
                    {
                        using var trLayout = new CanvasTextLayout(ds, tr, _fmtTranslation, maxW, float.MaxValue);
                        float trY = textY + mainH + TranslationGap;
                        byte trA = (byte)Math.Clamp((140f + 55f * appear) * edge, 0f, 255f);
                        using var trb = new CanvasSolidColorBrush(ds, Color.FromArgb(trA, 255, 255, 255));
                        ds.DrawTextLayout(trLayout, LeftMargin, trY, trb);
                    }

                    ds.Transform = System.Numerics.Matrix3x2.Identity;
                }
                else
                {
                    // 非当前行：白色 ~55%，按焦点距离与边缘叠加淡出
                    byte alpha = (byte)Math.Clamp(fade * edge * 150f, 10f, 150f);
                    using var tb = new CanvasSolidColorBrush(ds, Color.FromArgb(alpha, 255, 255, 255));
                    ds.DrawTextLayout(layout, LeftMargin, textY, tb);
                }
            }

            // 滚动目标完全由事件驱动（UpdateScrollPosition），Draw 不回写目标，
            // 避免与弹簧形成每帧互动
        }

        private void LyricsCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            _lyricsWidth = (float)e.NewSize.Width;
            _lyricsHeight = (float)e.NewSize.Height;

            // 尺寸变化时重建测量 RT 和文本格式（字号含 DPI）
            _fmtCurrent?.Dispose(); _fmtCurrent = null;
            _fmtNear?.Dispose(); _fmtNear = null;
            _fmtMeasure?.Dispose(); _fmtMeasure = null;
            _fmtTranslation?.Dispose(); _fmtTranslation = null;
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
            _lineMainHeights.Clear();

            using var ds = _measureTarget.CreateDrawingSession();
            float maxW = _lyricsWidth - LeftMargin - RightMargin;
            float total = 0;

            foreach (var lyric in _lyrics)
            {
                using var layout = new CanvasTextLayout(ds, lyric.Text, _fmtMeasure, maxW, float.MaxValue);
                float mainH = (float)layout.LayoutBounds.Height;

                // 为含译文的行预留译文高度，保证当前行切换时几何稳定、不跳动
                float extra = 0f;
                if (!string.IsNullOrEmpty(lyric.Translation) && _fmtTranslation != null)
                {
                    using var trLayout = new CanvasTextLayout(ds, lyric.Translation, _fmtTranslation, maxW, float.MaxValue);
                    extra = TranslationGap + (float)trLayout.LayoutBounds.Height;
                }

                // 行高 = 原文高度 + 译文预留 + 固定间距，保证不会重叠
                float lineH = mainH + extra + LineGap;
                _lineMainHeights.Add(mainH);
                _lineHeights.Add(lineH);
                _lineOffsets.Add(total);
                total += lineH;
            }

            UpdateScrollPosition();
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
        }

        #endregion

        #region Interactions (Tap / Long-press / Manual scroll)

        // 命中测试：返回屏幕 Y 对应的歌词行索引，未命中返回 -1
        private int HitTestLine(float y)
        {
            float anchorY = _lyricsHeight * AnchorRatio;
            for (int i = 0; i < _lyrics.Count; i++)
            {
                if (i >= _lineOffsets.Count || i >= _lineHeights.Count) continue;
                float lineTop = _lineOffsets[i] + anchorY - _scrollOffset;
                float lineBottom = lineTop + _lineHeights[i];
                if (y >= lineTop && y <= lineBottom) return i;
            }
            return -1;
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
            if (idx >= 0 && idx < _lyrics.Count)
                MainWindow.PlaybackService?.Seek(_lyrics[idx].Time);
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
            var parsed = ParseLyrics(lyricInfo.LrcLyric, lyricInfo.TranslatedLyric);

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

        private static List<LyricLine> ParseLyrics(string lrcContent, string? tlyricContent = null)
        {
            var lines = new List<LyricLine>();
            if (string.IsNullOrEmpty(lrcContent)) return lines;

            // 先解析译文，建立时间戳→译文映射
            var translations = new Dictionary<TimeSpan, string>();
            if (!string.IsNullOrEmpty(tlyricContent))
            {
                foreach (var rawLine in tlyricContent.Split('\n'))
                {
                    var trimmed = rawLine.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;
                    var matches = LrcTimeRegex.Matches(trimmed);
                    if (matches.Count == 0) continue;
                    var lastMatch = matches[^1];
                    var text = trimmed.Substring(lastMatch.Index + lastMatch.Length).Trim();
                    if (string.IsNullOrEmpty(text)) continue;
                    if (TryParseLrcTime(lastMatch, out var time))
                        translations[time] = text;
                }
            }

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
                    {
                        translations.TryGetValue(time, out var translation);
                        lines.Add(new LyricLine { Time = time, Text = text, Translation = translation });
                    }
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
            // 位置单调过滤：播放器逐帧报告的位置可能存在小幅回退抖动，
            // 回退量小于 800ms 时按上次位置处理，保证行索引单调、不会在行边界来回翻转；
            // 大幅回退视为用户 seek，直接采用新位置
            double raw = position.TotalMilliseconds;
            double ms;
            if (_lastRawMs >= 0 && raw < _lastRawMs - 800)
            {
                ms = raw;
                _lastRawMs = raw;
            }
            else
            {
                ms = Math.Max(raw, _lastRawMs);
                _lastRawMs = ms;
            }

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

                // 浮现动画节流：密集歌词（时间戳相邻很近）时不逐行重新触发，
                // 否则当前行会连续收缩弹跳（抽搐）。索引与滚动目标始终即时更新
                long now = Environment.TickCount64;
                if (now - _lineSwitchAt >= 300)
                {
                    _currentLineScale = 0.96f;
                    _currentLineScaleVel = 0f;
                    _lineSwitchAt = now;
                }
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
                _lastRawMs = -1;
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
                FullscreenIcon.Glyph = "\uE740";   // 进入全屏
                ApplyFullScreenLayout(false);
            }
            else
            {
                _appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
                FullscreenIcon.Glyph = "\uE73F";   // 退出全屏
                ApplyFullScreenLayout(true);
            }
        }

        // 全屏时放大封面与播放控件，窗口化时恢复
        private void ApplyFullScreenLayout(bool fullScreen)
        {
            if (fullScreen)
            {
                LeftPanel.Margin = new Thickness(80, 88, 48, 72);
                CoverBorder.MaxWidth = 520;
                CoverBorder.MaxHeight = 520;
                ControlsPanel.Width = 460;
                SongTitleText.FontSize = 24;
            }
            else
            {
                LeftPanel.Margin = new Thickness(48, 56, 24, 40);
                CoverBorder.MaxWidth = 380;
                CoverBorder.MaxHeight = 380;
                ControlsPanel.Width = 380;
                SongTitleText.FontSize = 20;
            }
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