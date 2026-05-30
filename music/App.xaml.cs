using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI;
using music.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace music
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;
        public static Window? m_window;

        public static MusicApiService ApiService { get; private set; } = new MusicApiService();

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();
            ApplyThemeColor();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();
            m_window = _window;
            _window.Activate();

            _ = InitializeApiAsync();
        }

        private async System.Threading.Tasks.Task InitializeApiAsync()
        {
            await ApiService.InitializeAsync();
        }

        public static void ApplyThemeColor()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                var hexColor = settings.Values["ThemeColor"]?.ToString();

                if (!string.IsNullOrEmpty(hexColor))
                {
                    var color = HexToColor(hexColor);
                    var resources = Current.Resources;
                    
                    // 更新所有强调色资源
                    resources["AccentFillColorDefaultBrush"] = new SolidColorBrush(color);
                    resources["AccentFillColorSecondaryBrush"] = new SolidColorBrush(Color.FromArgb(204, color.R, color.G, color.B));
                    resources["AccentFillColorTertiaryBrush"] = new SolidColorBrush(Color.FromArgb(153, color.R, color.G, color.B));
                    resources["AccentFillColorDisabledBrush"] = new SolidColorBrush(Color.FromArgb(102, color.R, color.G, color.B));
                    
                    // 更新按钮相关颜色
                    resources["AccentButtonBackground"] = new SolidColorBrush(color);
                    resources["AccentButtonBackgroundPointerOver"] = new SolidColorBrush(Color.FromArgb(230, color.R, color.G, color.B));
                    resources["AccentButtonBackgroundPressed"] = new SolidColorBrush(Color.FromArgb(204, color.R, color.G, color.B));
                    
                    // 更新选中状态颜色
                    resources["NavigationViewSelectionIndicatorForeground"] = new SolidColorBrush(color);
                    resources["TabViewItemHeaderSelectedBackground"] = new SolidColorBrush(Color.FromArgb(26, color.R, color.G, color.B));
                    resources["TabViewItemHeaderSelectedForeground"] = new SolidColorBrush(color);
                    
                    // 更新进度条
                    resources["ProgressBarForeground"] = new SolidColorBrush(color);
                    resources["SliderTrackValueFill"] = new SolidColorBrush(color);
                    resources["SliderThumbBackground"] = new SolidColorBrush(color);
                    
                    // 更新复选框和单选按钮
                    resources["CheckBoxCheckBackgroundFillChecked"] = new SolidColorBrush(color);
                    resources["RadioButtonBackgroundChecked"] = new SolidColorBrush(color);
                    
                    // 更新超链接
                    resources["TextFillColorPrimaryBrush"] = new SolidColorBrush(color);
                    
                    System.Diagnostics.Debug.WriteLine($"[App] Theme color applied: {hexColor}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] ApplyThemeColor Error: {ex.Message}");
            }
        }

        private static Windows.UI.Color HexToColor(string hex)
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
            {
                var r = Convert.ToByte(hex.Substring(0, 2), 16);
                var g = Convert.ToByte(hex.Substring(2, 2), 16);
                var b = Convert.ToByte(hex.Substring(4, 2), 16);
                return Windows.UI.Color.FromArgb(255, r, g, b);
            }
            return Windows.UI.Color.FromArgb(255, 0, 120, 212); // 默认蓝色
        }
    }
}
