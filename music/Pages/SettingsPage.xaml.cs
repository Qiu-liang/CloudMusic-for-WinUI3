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
            var serverAddress = settings.Values["ServerAddress"]?.ToString() ?? Services.MusicApiService.DefaultServerAddress;
            ServerAddressBox.Text = serverAddress;
            AllowInsecureLoginCheckBox.IsChecked = settings.Values["AllowInsecureLogin"] as bool? ?? false;

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

            if (App.m_window is MainWindow mainWindow)
                mainWindow.ApplyTheme(theme);
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

            if (!Uri.TryCreate(address.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? address : "http://" + address, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                ServerStatusText.Text = "请输入有效的 HTTP 或 HTTPS 地址";
                return;
            }

            address = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
            ServerAddressBox.Text = address;

            try
            {
                App.ApiService.UpdateBaseUrl(address);
            }
            catch (Exception ex)
            {
                // 保存失败只提示，不让异常冒到 UI 线程把进程带走
                ServerStatusText.Text = $"保存失败：{ex.Message}";
                ServerStatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
                return;
            }

            ServerStatusText.Text = uri.Scheme == Uri.UriSchemeHttps
                ? "已保存并立即生效，登录凭据将通过 HTTPS 传输。"
                : "已保存并立即生效。警告：HTTP 为明文传输，请勿用于不可信网络；手机号密码登录仅允许本机 HTTP。";
            ServerStatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                uri.Scheme == Uri.UriSchemeHttps ? "SystemFillColorSuccessBrush" : "SystemFillColorCautionBrush"];
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

        private void ClearLoginButton_Click(object sender, RoutedEventArgs e)
        {
            // 走主窗口的统一退出入口：清除 Cookie/UserId、侧边栏恢复未登录、
            // 清空歌单导航项；若正停留在失效的个人中心页会自动跳回首页
            if (App.m_window is MainWindow mainWindow)
            {
                mainWindow.PerformLogout();
            }
            else
            {
                App.ApiService.ResetLogin();
            }

            ClearLoginStatusText.Text = "已清除本地登录状态";
            ClearLoginStatusText.Visibility = Visibility.Visible;
        }

        private void AllowInsecureLoginCheckBox_Click(object sender, RoutedEventArgs e)
        {
            ApplicationData.Current.LocalSettings.Values["AllowInsecureLogin"] =
                AllowInsecureLoginCheckBox.IsChecked == true;
        }

        private void FancyLyricsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FancyLyricsComboBox == null) return;
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["FancyLyrics"] = FancyLyricsComboBox.SelectedIndex == 1;
        }
    }
}