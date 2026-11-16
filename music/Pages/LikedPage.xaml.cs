using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using music.Core.Abstractions;
using music.Core.Mvvm;
using music.Core.ViewModels;

namespace music.Pages
{
    public sealed partial class LikedPage : Page
    {
        public LikedViewModel? ViewModel { get; private set; }

        public LikedPage()
        {
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is NavigationRequest request)
            {
                DetachViewModel();
                ViewModel = (LikedViewModel)request.CreateViewModel();
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
            if (e.ClickedItem is music.Core.ViewModels.Items.SongItem songItem && ViewModel != null)
            {
                ViewModel.PlaySongAsync(songItem).Forget();
            }
        }
    }
}
