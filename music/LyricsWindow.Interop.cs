using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace music
{
    public sealed partial class LyricsWindow
    {
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
