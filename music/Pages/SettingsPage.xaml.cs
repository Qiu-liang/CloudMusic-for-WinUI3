using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage;

namespace music.Pages
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            this.InitializeComponent();
            LoadSettings();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
        }

        private void LoadSettings()
        {
            var settings = ApplicationData.Current.LocalSettings;

            // 加载主题设置
            var theme = settings.Values["Theme"]?.ToString() ?? "System";
            ThemeComboBox.SelectedIndex = theme switch
            {
                "Light" => 1,
                "Dark" => 2,
                _ => 0
            };

            // 加载服务器地址
            var serverAddress = settings.Values["ServerAddress"]?.ToString() ?? "http://192.168.31.205:3000";
            ServerAddressBox.Text = serverAddress;

            // 加载音质设置
            var quality = settings.Values["AudioQuality"]?.ToString() ?? "standard";
            QualityComboBox.SelectedIndex = quality switch
            {
                "standard" => 0,
                "higher" => 1,
                "exhigh" => 2,
                "lossless" => 3,
                "hires" => 4,
                "jyeffect" => 5,
                "sky" => 6,
                "dolby" => 7,
                "jymaster" => 8,
                _ => 0
            };

            // 加载实验性功能设置
            var fancyLyrics = settings.Values["FancyLyrics"] as bool? ?? false;
            FancyLyricsComboBox.SelectedIndex = fancyLyrics ? 1 : 0;
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeComboBox.SelectedIndex < 0) return;

            var settings = ApplicationData.Current.LocalSettings;
            var theme = ThemeComboBox.SelectedIndex switch
            {
                1 => "Light",
                2 => "Dark",
                _ => "System"
            };
            settings.Values["Theme"] = theme;

            // 应用主题到主窗口内容
            var root = App.m_window?.Content as FrameworkElement;
            if (root != null)
            {
                root.RequestedTheme = theme switch
                {
                    "Light" => ElementTheme.Light,
                    "Dark" => ElementTheme.Dark,
                    _ => ElementTheme.Default
                };
            }

            // 更新标题栏颜色
            UpdateTitleBarColor(theme);

            // 刷新播放控件背景
            if (App.m_window is MainWindow mainWindow)
            {
                mainWindow.RefreshPlayerBarBackground();
            }
        }

        private void UpdateTitleBarColor(string theme)
        {
            try
            {
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                
                if (appWindow?.TitleBar != null)
                {
                    var isDark = theme == "Dark" || 
                                 (theme == "System" && IsSystemDarkMode());
                    
                    var backgroundColor = isDark 
                        ? Windows.UI.Color.FromArgb(255, 32, 32, 32)  // 深色背景
                        : Windows.UI.Color.FromArgb(255, 243, 243, 243); // 浅色背景
                    
                    appWindow.TitleBar.BackgroundColor = backgroundColor;
                    appWindow.TitleBar.ButtonBackgroundColor = backgroundColor;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateTitleBarColor error: {ex.Message}");
            }
        }

        private bool IsSystemDarkMode()
        {
            // 默认返回浅色模式
            return false;
        }

        private void SaveServerButton_Click(object sender, RoutedEventArgs e)
        {
            var address = ServerAddressBox.Text.Trim();
            
            if (string.IsNullOrEmpty(address))
            {
                ServerStatusText.Text = "请输入服务器地址";
                ServerStatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
                return;
            }

            if (!address.StartsWith("http://") && !address.StartsWith("https://"))
            {
                address = "http://" + address;
                ServerAddressBox.Text = address;
            }

            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["ServerAddress"] = address;

            ServerStatusText.Text = "已保存，重启应用后生效";
            ServerStatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
        }

        private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            var address = ServerAddressBox.Text.Trim();
            
            if (string.IsNullOrEmpty(address))
            {
                ServerStatusText.Text = "请先输入服务器地址";
                ServerStatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
                return;
            }

            TestConnectionButton.IsEnabled = false;
            ServerStatusText.Text = "正在测试连接...";
            ServerStatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

            try
            {
                using var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                var response = await client.GetAsync(address);
                
                ServerStatusText.Text = $"连接成功 (状态码: {response.StatusCode})";
                ServerStatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
            }
            catch (Exception ex)
            {
                ServerStatusText.Text = $"连接失败: {ex.Message}";
                ServerStatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
            }
            finally
            {
                TestConnectionButton.IsEnabled = true;
            }
        }
        private void QualityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (QualityComboBox.SelectedItem is ComboBoxItem item && item.Tag is string quality)
            {
                // 播放时 GetSongUrlAsync 会读取该值并传给 /song/url/v1 的 level 参数
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values["AudioQuality"] = quality;
            }
        }

        private void FancyLyricsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FancyLyricsComboBox == null) return;
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["FancyLyrics"] = FancyLyricsComboBox.SelectedIndex == 1;
        }
    }
}