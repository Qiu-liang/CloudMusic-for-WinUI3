using System;
using System.ComponentModel;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using music.Core.Mvvm;
using music.Core.ViewModels;

namespace music.Dialogs
{
    public sealed partial class LoginDialog : ContentDialog
    {
        public LoginViewModel ViewModel { get; }
        private byte[]? _lastRenderedQrBytes;

        public LoginDialog(LoginViewModel viewModel)
        {
            ViewModel = viewModel;
            InitializeComponent();

            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            ViewModel.LoginSucceeded += ViewModel_LoginSucceeded;

            Closed += (_, _) =>
            {
                ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
                ViewModel.LoginSucceeded -= ViewModel_LoginSucceeded;
                ViewModel.Dispose();
            };
        }

        private void ViewModel_LoginSucceeded(object? sender, EventArgs e)
        {
            DispatcherQueue.TryEnqueue(Hide);
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            DispatcherQueue.TryEnqueue(SyncFromViewModel);
        }

        private async void SyncFromViewModel()
        {
            PhoneErrorText.Text = ViewModel.PhoneError;
            PhoneErrorBorder.Visibility = ViewModel.HasPhoneError ? Visibility.Visible : Visibility.Collapsed;
            QRLoadingRing.IsActive = ViewModel.IsQrLoading;
            QRStatusText.Text = ViewModel.QrStatusText;
            RefreshQRButton.Visibility = ViewModel.ShowRefreshQr ? Visibility.Visible : Visibility.Collapsed;

            var qrBytes = ViewModel.QrImageBytes;
            if (qrBytes != null && !ReferenceEquals(qrBytes, _lastRenderedQrBytes))
            {
                _lastRenderedQrBytes = qrBytes;
                var bitmap = new BitmapImage();
                using var stream = new MemoryStream(qrBytes);
                await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
                QRCodeImage.Source = bitmap;
            }
        }

        private void PhoneTabButton_Click(object sender, RoutedEventArgs e)
        {
            PhoneLoginPanel.Visibility = Visibility.Visible;
            QRLoginPanel.Visibility = Visibility.Collapsed;
            PhoneTabButton.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
            QRTabButton.Style = (Style)Application.Current.Resources["DefaultButtonStyle"];
            ViewModel.StopQrPolling();
        }

        private void QRTabButton_Click(object sender, RoutedEventArgs e)
        {
            PhoneLoginPanel.Visibility = Visibility.Collapsed;
            QRLoginPanel.Visibility = Visibility.Visible;
            PhoneTabButton.Style = (Style)Application.Current.Resources["DefaultButtonStyle"];
            QRTabButton.Style = (Style)Application.Current.Resources["AccentButtonStyle"];

            if (!ViewModel.IsQrLoginActive)
            {
                ViewModel.GenerateQrCodeAsync().Forget();
            }
        }

        private async void LoginButton_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            var deferral = args.GetDeferral();
            try
            {
                PrimaryButtonText = "登录中...";
                IsPrimaryButtonEnabled = false;

                var (success, _) = await ViewModel.LoginWithPhoneAsync(PhoneBox.Text, PasswordBox.Password);
                if (success)
                {
                    deferral.Complete();
                    Hide();
                    return;
                }

                IsPrimaryButtonEnabled = true;
                PrimaryButtonText = "登录";
                deferral.Complete();
            }
            catch
            {
                IsPrimaryButtonEnabled = true;
                PrimaryButtonText = "登录";
                deferral.Complete();
            }
        }

        private void CancelButton_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            ViewModel.StopQrPolling();
        }

        private void RefreshQRButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.GenerateQrCodeAsync().Forget();
        }
    }
}
