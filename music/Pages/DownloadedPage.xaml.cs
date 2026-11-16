using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using music.Core.Abstractions;
using music.Core.ViewModels;

namespace music.Pages
{
    public sealed partial class DownloadedPage : Page
    {
        public DownloadedViewModel? ViewModel { get; private set; }

        public DownloadedPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is NavigationRequest request)
            {
                ViewModel?.Dispose();
                ViewModel = (DownloadedViewModel)request.CreateViewModel();

                if (ContentFrame.Content == null)
                {
                    ContentFrame.Navigate(typeof(DownloadPage), ViewModel.CreateChildRequest(PageKey.Download));
                }
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            ViewModel?.Dispose();
            ViewModel = null;
        }

        private void DownloadedNavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (ViewModel == null || args.SelectedItemContainer == null) return;

            var tag = args.SelectedItemContainer.Tag?.ToString();
            switch (tag)
            {
                case "download":
                    ContentFrame.Navigate(typeof(DownloadPage), ViewModel.CreateChildRequest(PageKey.Download));
                    break;
                case "local":
                    ContentFrame.Navigate(typeof(LocalPage), ViewModel.CreateChildRequest(PageKey.Local));
                    break;
            }
        }
    }
}
