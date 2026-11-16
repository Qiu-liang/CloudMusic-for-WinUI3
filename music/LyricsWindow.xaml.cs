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
using music.Core.Models;
using music.Core.Mvvm;
using music.Core.Services;
using music.Core.ViewModels;

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

        #endregion
    }
}