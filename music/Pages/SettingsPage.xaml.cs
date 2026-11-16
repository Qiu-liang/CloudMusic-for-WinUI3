using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using music.Core.Abstractions;
using music.Core.Mvvm;
using music.Core.ViewModels;

namespace music.Pages
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsViewModel? ViewModel { get; private set; }
        private bool _isSyncingUi;

        public SettingsPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is NavigationRequest request)
            {
                DetachViewModel();
                ViewModel = (SettingsViewModel)request.CreateViewModel();
                ViewModel.PropertyChanged += ViewModel_PropertyChanged;
                ViewModel.Load();
                SyncAllFromViewModel();
            }
            else
            {
                ViewModel?.SyncDownloadFolderState();
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            DetachViewModel();
        }

        private void DetachViewModel()
        {
            if (ViewModel != null)
            {
                ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
                ViewModel.Dispose();
                ViewModel = null;
            }
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            SyncAllFromViewModel();
        }

        private void SyncAllFromViewModel()
        {
            if (ViewModel == null) return;
            _isSyncingUi = true;
            try
            {
                ThemeComboBox.SelectedIndex = ViewModel.ThemeIndex;
                if (ServerAddressBox.Text != ViewModel.ServerAddress)
                    ServerAddressBox.Text = ViewModel.ServerAddress;
                AllowInsecureLoginCheckBox.IsChecked = ViewModel.AllowInsecureLogin;

                ServerStatusText.Text = ViewModel.ServerStatusText;
                ServerStatusText.Foreground = GetToneBrush(ViewModel.ServerStatusTone);
                TestConnectionButton.IsEnabled = ViewModel.CanTestConnection;

                for (int i = 0; i < ViewModel.QualityOptions.Count && i < QualityComboBox.Items.Count; i++)
                {
                    if (QualityComboBox.Items[i] is ComboBoxItem item)
                    {
                        item.Content = ViewModel.QualityOptions[i].Text;
                        item.Opacity = ViewModel.QualityOptions[i].Opacity;
                    }
                }
                QualityComboBox.SelectedIndex = ViewModel.QualityIndex;

                DownloadFolderBox.Text = ViewModel.DownloadFolder;
                ResetDownloadFolderButton.IsEnabled = ViewModel.CanResetDownloadFolder;
                DownloadFolderStatusText.Text = ViewModel.DownloadFolderStatusText;
                DownloadFolderStatusText.Foreground = GetToneBrush(ViewModel.DownloadFolderStatusTone);
                DownloadFolderStatusText.Visibility = ViewModel.ShowDownloadFolderStatus ? Visibility.Visible : Visibility.Collapsed;

                ClearLoginStatusText.Text = ViewModel.ClearLoginStatusText;
                ClearLoginStatusText.Visibility = ViewModel.ShowClearLoginStatus ? Visibility.Visible : Visibility.Collapsed;

                FancyLyricsComboBox.SelectedIndex = ViewModel.FancyLyricsIndex;
            }
            finally
            {
                _isSyncingUi = false;
            }
        }

        private static Brush GetToneBrush(StatusTone tone)
        {
            var key = tone switch
            {
                StatusTone.Success => "SystemFillColorSuccessBrush",
                StatusTone.Caution => "SystemFillColorCautionBrush",
                StatusTone.Critical => "SystemFillColorCriticalBrush",
                _ => "TextFillColorSecondaryBrush"
            };
            return (Brush)Application.Current.Resources[key];
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingUi || ViewModel == null || ThemeComboBox.SelectedIndex < 0) return;
            ViewModel.SelectTheme(ThemeComboBox.SelectedIndex);
        }

        private void SaveServerButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;
            ViewModel.ServerAddress = ServerAddressBox.Text;
            ViewModel.SaveServerAddress();
        }

        private void TestConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;
            ViewModel.ServerAddress = ServerAddressBox.Text;
            ViewModel.TestConnectionAsync().Forget();
        }

        public void SyncQualitySelection(string quality)
        {
            ViewModel?.SyncQualitySelection(quality);
        }

        private void QualityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingUi || ViewModel == null || QualityComboBox.SelectedIndex < 0) return;
            ViewModel.SelectQualityByIndexAsync(QualityComboBox.SelectedIndex).Forget();
        }

        private void ClearLoginButton_Click(object sender, RoutedEventArgs e) => ViewModel?.ClearLogin();

        private void AllowInsecureLoginCheckBox_Click(object sender, RoutedEventArgs e) =>
            ViewModel?.SetAllowInsecureLogin(AllowInsecureLoginCheckBox.IsChecked == true);

        private void FancyLyricsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingUi || ViewModel == null || FancyLyricsComboBox == null) return;
            ViewModel.SetFancyLyrics(FancyLyricsComboBox.SelectedIndex);
        }

        private void SelectDownloadFolderButton_Click(object sender, RoutedEventArgs e) =>
            ViewModel?.SelectDownloadFolderAsync().Forget();

        private void OpenDownloadFolderButton_Click(object sender, RoutedEventArgs e) =>
            ViewModel?.OpenDownloadFolderAsync().Forget();

        private void ResetDownloadFolderButton_Click(object sender, RoutedEventArgs e) =>
            ViewModel?.ResetDownloadFolder();
    }
}
