using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace music.Pages
{
    public sealed partial class DownloadedPage : Page
    {
        public DownloadedPage()
        {
            this.InitializeComponent();
            this.Loaded += DownloadedPage_Loaded;
        }

        private void DownloadedPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (ContentFrame.Content == null)
            {
                ContentFrame.Navigate(typeof(DownloadPage));
            }
        }

        private void DownloadedNavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItemContainer != null)
            {
                var tag = args.SelectedItemContainer.Tag.ToString();
                switch (tag)
                {
                    case "download":
                        ContentFrame.Navigate(typeof(DownloadPage));
                        break;
                    case "local":
                        ContentFrame.Navigate(typeof(LocalPage));
                        break;
                }
            }
        }
    }
}