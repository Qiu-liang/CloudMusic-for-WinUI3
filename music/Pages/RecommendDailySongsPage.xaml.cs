using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using music.Core.Abstractions;
using music.Core.Mvvm;
using music.Core.ViewModels;

namespace music.Pages
{
    public sealed partial class RecommendDailySongsPage : Page
    {
        public RecommendDailySongsViewModel? ViewModel { get; private set; }

        public RecommendDailySongsPage()
        {
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is NavigationRequest request)
            {
                DetachViewModel();
                ViewModel = (RecommendDailySongsViewModel)request.CreateViewModel();
                Bindings.Update();

                if (request.Parameter is string pageType)
                {
                    await ViewModel.LoadAsync(pageType);
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

        private void SongsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is music.Core.ViewModels.Items.SongItem songItem && ViewModel != null)
            {
                ViewModel.PlaySongAsync(songItem).Forget();
            }
        }
    }
}
