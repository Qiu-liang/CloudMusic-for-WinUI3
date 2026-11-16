using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using music.Core.Abstractions;
using music.Core.Mvvm;
using music.Core.ViewModels;

namespace music.Pages
{
    public sealed partial class DownloadPage : Page
    {
        public DownloadViewModel? ViewModel { get; private set; }

        public DownloadPage()
        {
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is NavigationRequest request)
            {
                DetachViewModel();
                ViewModel = (DownloadViewModel)request.CreateViewModel();
                Bindings.Update();
                await ViewModel.LoadAsync();
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            DetachViewModel();
        }

        private void DetachViewModel()
        {
            ViewModel?.Dispose();
            ViewModel = null;
        }

        private void SongsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is music.Core.ViewModels.Items.AudioFileItem song && ViewModel != null)
            {
                ViewModel.PlaySongAsync(song).Forget();
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string fileName && ViewModel != null)
            {
                ViewModel.DeleteSongAsync(fileName).Forget();
            }
        }
    }
}
