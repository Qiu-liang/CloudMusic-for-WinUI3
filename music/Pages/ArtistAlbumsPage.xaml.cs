using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using music.Core.Abstractions;
using music.Core.ViewModels;

namespace music.Pages
{
    public sealed partial class ArtistAlbumsPage : Page
    {
        public ArtistAlbumsViewModel? ViewModel { get; private set; }

        public ArtistAlbumsPage()
        {
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is NavigationRequest request)
            {
                DetachViewModel();
                ViewModel = (ArtistAlbumsViewModel)request.CreateViewModel();
                Bindings.Update();

                if (request.Parameter is string artistId)
                {
                    await ViewModel.LoadAsync(artistId);
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

        private void AlbumsGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is music.Core.ViewModels.Items.AlbumItem album && ViewModel != null)
            {
                ViewModel.OpenAlbum(album);
            }
        }
    }
}
