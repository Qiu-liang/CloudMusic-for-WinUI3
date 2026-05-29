using System;
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
            var quality = settings.Values["AudioQuality"]?.ToString() ?? "exhigh";
            QualityComboBox.SelectedIndex = quality switch
            {
                "standard" => 0,
                "higher" => 1,
                "exhigh" => 2,
                "lossless" => 3,
                _ => 2
            };
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

            // 应用主题
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
    }
}