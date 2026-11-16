using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using music.Core.Abstractions;
using music.Core.Services;
using music.Dialogs;

namespace music.Infrastructure.Platform
{
    public sealed class DialogService : IDialogService
    {
        private readonly WindowHost _host;
        private readonly Func<LoginDialog> _loginDialogFactory;
        private readonly ISessionService _sessionService;

        internal DialogService(WindowHost host, ISessionService sessionService, Func<LoginDialog> loginDialogFactory)
        {
            _host = host;
            _sessionService = sessionService;
            _loginDialogFactory = loginDialogFactory;
        }

        public async Task ShowMessageAsync(string title, string message)
        {
            var xamlRoot = _host.XamlRoot;
            if (xamlRoot == null) return;

            var dialog = new ContentDialog
            {
                Title = title,
                Content = string.IsNullOrWhiteSpace(message) ? "请稍后重试。" : message,
                CloseButtonText = "确定",
                XamlRoot = xamlRoot
            };
            await dialog.ShowAsync();
        }

        public async Task<bool> ShowConfirmAsync(
            string title,
            string content,
            string primaryButtonText = "确定",
            string closeButtonText = "取消")
        {
            var xamlRoot = _host.XamlRoot;
            if (xamlRoot == null) return false;

            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                PrimaryButtonText = primaryButtonText,
                CloseButtonText = closeButtonText,
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = xamlRoot
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        public async Task<string?> ShowInputAsync(
            string title,
            string placeholder,
            string primaryButtonText = "确定",
            int maxLength = 40)
        {
            var xamlRoot = _host.XamlRoot;
            if (xamlRoot == null) return null;

            var inputBox = new TextBox
            {
                PlaceholderText = placeholder,
                MaxLength = maxLength,
                Width = 300
            };

            var dialog = new ContentDialog
            {
                Title = title,
                Content = inputBox,
                PrimaryButtonText = primaryButtonText,
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(inputBox.Text)
                ? inputBox.Text.Trim()
                : null;
        }

        public async Task<bool> ShowLoginAsync()
        {
            var xamlRoot = _host.XamlRoot;
            if (xamlRoot == null) return false;

            var dialog = _loginDialogFactory();
            dialog.XamlRoot = xamlRoot;
            await dialog.ShowAsync();
            return _sessionService.IsLoggedIn;
        }
    }
}
