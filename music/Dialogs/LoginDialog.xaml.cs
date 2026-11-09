using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace music.Dialogs
{
    public sealed partial class LoginDialog : ContentDialog
    {
        private string _qrKey = string.Empty;
        private bool _isQRLoginActive = false;
        private bool _isDialogClosed = false;
        private int _qrGeneration = 0;

        public LoginDialog()
        {
            this.InitializeComponent();
            // 无论用户点取消、按 Esc 还是被代码关闭，都停止轮询，避免后台继续请求
            this.Closed += (_, _) =>
            {
                _isDialogClosed = true;
                _isQRLoginActive = false;
            };
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

            if (!_isQRLoginActive)
            {
                _ = GenerateQRCodeAsync();
            }
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
            // 每次生成二维码都开启新一代轮询，旧循环会在下一次检查时自行退出
            var generation = ++_qrGeneration;

            try
            {
                QRLoadingRing.IsActive = true;
                QRStatusText.Text = "正在生成二维码...";
                RefreshQRButton.Visibility = Visibility.Collapsed;

                // 二维码相关接口必须带时间戳，避免服务端/代理返回缓存的旧状态
                var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var keyJson = await App.ApiService.GetWithoutCookieAsync($"/login/qr/key?timestamp={stamp}");
                var keyResult = JsonSerializer.Deserialize<JsonElement>(keyJson);

                if (generation != _qrGeneration) return;

                if (keyResult.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("unikey", out var unikey))
                {
                    _qrKey = unikey.GetString() ?? string.Empty;

                    var qrJson = await App.ApiService.GetWithoutCookieAsync(
                        $"/login/qr/create?key={_qrKey}&qrimg=true&timestamp={stamp}");
                    var qrResult = JsonSerializer.Deserialize<JsonElement>(qrJson);

                    if (generation != _qrGeneration) return;

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
                            _ = CheckQRStatusAsync(generation);
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

        private async Task CheckQRStatusAsync(int generation)
        {
            var consecutiveFailures = 0;

            while (_isQRLoginActive && generation == _qrGeneration)
            {
                try
                {
                    await Task.Delay(2000);

                    if (!_isQRLoginActive || generation != _qrGeneration) break;

                    var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    // noCookie：不携带本机已有的匿名/过期 Cookie，否则扫码后服务端可能返回 502
                    var checkJson = await App.ApiService.GetWithoutCookieAsync(
                        $"/login/qr/check?key={_qrKey}&noCookie=true&timestamp={stamp}");

                    if (!_isQRLoginActive || generation != _qrGeneration) break;

                    var checkResult = JsonSerializer.Deserialize<JsonElement>(checkJson);
                    consecutiveFailures = 0;

                    if (!checkResult.TryGetProperty("code", out var code) || !code.TryGetInt32(out var statusCode))
                    {
                        QRStatusText.Text = "等待扫码...";
                        continue;
                    }

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
                            _isQRLoginActive = false;
                            await CompleteQRLoginAsync(checkResult);
                            break;
                        default:
                            QRStatusText.Text = $"登录校验失败（{statusCode}），请点击刷新重试";
                            RefreshQRButton.Visibility = Visibility.Visible;
                            _isQRLoginActive = false;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    if (!_isQRLoginActive || generation != _qrGeneration) break;

                    consecutiveFailures++;
                    System.Diagnostics.Debug.WriteLine($"[QR] 状态轮询失败({consecutiveFailures}): {ex.Message}");

                    if (consecutiveFailures >= 5)
                    {
                        QRStatusText.Text = "无法连接服务器，请检查 API 地址和网络";
                        RefreshQRButton.Visibility = Visibility.Visible;
                        _isQRLoginActive = false;
                    }
                    else
                    {
                        QRStatusText.Text = "网络异常，正在重试...";
                    }
                }
            }
        }

        private async Task CompleteQRLoginAsync(JsonElement checkResult)
        {
            QRStatusText.Text = "登录成功，正在获取用户信息...";

            if (!TryExtractCookie(checkResult, out var cookieStr))
            {
                QRStatusText.Text = "授权成功，但服务器未返回登录 Cookie，请刷新重试";
                RefreshQRButton.Visibility = Visibility.Visible;
                return;
            }

            var loggedIn = await App.ApiService.SetLoginCookieAsync(cookieStr);
            if (_isDialogClosed) return;
            if (!loggedIn)
            {
                QRStatusText.Text = "授权成功，但获取用户信息失败，请点击刷新重试";
                RefreshQRButton.Visibility = Visibility.Visible;
                return;
            }

            QRStatusText.Text = "登录成功！";
            await Task.Delay(300);
            if (!_isDialogClosed) Hide();
        }

        // 803 响应的 cookie 通常是字符串；部分 API 版本返回 Set-Cookie 数组
        private static bool TryExtractCookie(JsonElement checkResult, out string cookie)
        {
            cookie = string.Empty;
            if (!checkResult.TryGetProperty("cookie", out var element)) return false;

            if (element.ValueKind == JsonValueKind.String)
            {
                cookie = element.GetString() ?? string.Empty;
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                var pairs = new List<string>();
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) continue;

                    var raw = item.GetString();
                    if (string.IsNullOrWhiteSpace(raw)) continue;

                    // 数组元素可能是完整的 Set-Cookie，只取 name=value 部分
                    pairs.Add(raw.Split(';')[0].Trim());
                }

                cookie = string.Join("; ", pairs);
            }

            return !string.IsNullOrEmpty(cookie);
        }

        private async void RefreshQRButton_Click(object sender, RoutedEventArgs e)
        {
            await GenerateQRCodeAsync();
        }
    }
}
