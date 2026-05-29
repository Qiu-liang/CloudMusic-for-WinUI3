using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;

namespace music.Dialogs
{
    public sealed partial class LoginDialog : ContentDialog
    {
        private string _qrKey = string.Empty;
        private bool _isQRLoginActive = false;

        public LoginDialog()
        {
            this.InitializeComponent();
        }

        private void PhoneTabButton_Click(object sender, RoutedEventArgs e)
        {
            PhoneLoginPanel.Visibility = Visibility.Visible;
            QRLoginPanel.Visibility = Visibility.Collapsed;
            PhoneTabButton.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
            QRTabButton.Style = (Style)Application.Current.Resources["DefaultButtonStyle"];
            _isQRLoginActive = false;
        }

        private void QRTabButton_Click(object sender, RoutedEventArgs e)
        {
            PhoneLoginPanel.Visibility = Visibility.Collapsed;
            QRLoginPanel.Visibility = Visibility.Visible;
            PhoneTabButton.Style = (Style)Application.Current.Resources["DefaultButtonStyle"];
            QRTabButton.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
            _ = GenerateQRCodeAsync();
        }

        private async void LoginButton_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            var deferral = args.GetDeferral();

            try
            {
                var phone = PhoneBox.Text.Trim();
                var password = PasswordBox.Password;

                if (string.IsNullOrEmpty(phone))
                {
                    ShowPhoneError("请输入手机号码");
                    deferral.Complete();
                    return;
                }

                if (string.IsNullOrEmpty(password))
                {
                    ShowPhoneError("请输入密码");
                    deferral.Complete();
                    return;
                }

                PrimaryButtonText = "登录中...";
                IsPrimaryButtonEnabled = false;

                var (success, message, code, rawResponse) = await App.ApiService.LoginWithPhoneAsync(phone, password);

                if (success)
                {
                    // 登录成功，关闭对话框
                    deferral.Complete();
                    Hide();
                    return;
                }
                else
                {
                    // 登录失败，显示错误信息在对话框内
                    ShowPhoneError($"{message} (错误代码: {code})");
                    IsPrimaryButtonEnabled = true;
                    PrimaryButtonText = "登录";
                    deferral.Complete();
                }
            }
            catch (Exception ex)
            {
                ShowPhoneError($"登录出错: {ex.Message}");
                IsPrimaryButtonEnabled = true;
                PrimaryButtonText = "登录";
                deferral.Complete();
            }
        }

        private void CancelButton_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            _isQRLoginActive = false;
        }

        private void ShowPhoneError(string message)
        {
            PhoneErrorText.Text = message;
            PhoneErrorBorder.Visibility = Visibility.Visible;
        }

        private async Task GenerateQRCodeAsync()
        {
            try
            {
                QRLoadingRing.IsActive = true;
                QRStatusText.Text = "正在生成二维码...";
                RefreshQRButton.Visibility = Visibility.Collapsed;

                var keyJson = await App.ApiService.GetWithoutCookieAsync("/login/qr/key");
                var keyResult = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(keyJson);

                if (keyResult.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("unikey", out var unikey))
                {
                    _qrKey = unikey.GetString() ?? string.Empty;

                    var qrJson = await App.ApiService.GetWithoutCookieAsync($"/login/qr/create?key={_qrKey}&qrimg=true");
                    var qrResult = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(qrJson);

                    if (qrResult.TryGetProperty("data", out var qrData) &&
                        qrData.TryGetProperty("qrimg", out var qrimg))
                    {
                        var base64 = qrimg.GetString() ?? string.Empty;
                        if (!string.IsNullOrEmpty(base64))
                        {
                            // Remove data:image/png;base64, prefix if present
                            if (base64.Contains(","))
                            {
                                base64 = base64.Substring(base64.IndexOf(",") + 1);
                            }
                            
                            var bytes = Convert.FromBase64String(base64);
                            var bitmap = new BitmapImage();
                            using (var stream = new MemoryStream(bytes))
                            {
                                await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
                            }
                            QRCodeImage.Source = bitmap;
                            QRStatusText.Text = "请使用网易云音乐APP扫码登录";
                            QRLoadingRing.IsActive = false;

                            _isQRLoginActive = true;
                            _ = CheckQRStatusAsync();
                            return;
                        }
                    }
                }

                QRStatusText.Text = "生成二维码失败";
                QRLoadingRing.IsActive = false;
                RefreshQRButton.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                QRStatusText.Text = $"生成二维码出错: {ex.Message}";
                QRLoadingRing.IsActive = false;
                RefreshQRButton.Visibility = Visibility.Visible;
            }
        }

        private async Task CheckQRStatusAsync()
        {
            while (_isQRLoginActive)
            {
                try
                {
                    await Task.Delay(2000);

                    if (!_isQRLoginActive) break;

                    var checkJson = await App.ApiService.GetWithoutCookieAsync($"/login/qr/check?key={_qrKey}");
                    var checkResult = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(checkJson);

                    if (checkResult.TryGetProperty("code", out var code))
                    {
                        var statusCode = code.GetInt32();

                        switch (statusCode)
                        {
                            case 800:
                                QRStatusText.Text = "二维码已过期，请刷新";
                                RefreshQRButton.Visibility = Visibility.Visible;
                                _isQRLoginActive = false;
                                break;
                            case 801:
                                QRStatusText.Text = "等待扫码...";
                                break;
                            case 802:
                                QRStatusText.Text = "已扫码，等待确认...";
                                break;
                            case 803:
                                QRStatusText.Text = "登录成功！";
                                _isQRLoginActive = false;

                                if (checkResult.TryGetProperty("cookie", out var cookie))
                                {
                                    var cookieStr = cookie.GetString() ?? string.Empty;
                                    App.ApiService.SetLoginCookie(cookieStr);
                                }

                                await Task.Delay(500);
                                Hide();
                                break;
                        }
                    }
                }
                catch
                {
                    if (!_isQRLoginActive) break;
                }
            }
        }

        private async void RefreshQRButton_Click(object sender, RoutedEventArgs e)
        {
            await GenerateQRCodeAsync();
        }
    }
}