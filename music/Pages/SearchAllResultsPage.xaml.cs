using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using music.Core.Abstractions;
using music.Core.ViewModels;

namespace music.Pages
{
    public sealed partial class SearchAllResultsPage : Page
    {
        public SearchAllResultsViewModel? ViewModel { get; private set; }

        public SearchAllResultsPage()
        {
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is NavigationRequest request)
            {
                DetachViewModel();
                ViewModel = (SearchAllResultsViewModel)request.CreateViewModel();
                Bindings.Update();

                if (request.Parameter is music.Core.ViewModels.SearchAllResultsParams coreParam)
                {
                    await ViewModel.LoadAsync(coreParam.Type, coreParam.Keywords);
                }
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

        private void ResultsGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is music.Core.ViewModels.Items.PlaylistItem item && ViewModel != null)
            {
                ViewModel.OpenItem(item);
            }
        }
    }
}
