using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using music.Core.Abstractions;
using music.Core.Mvvm;
using music.Core.ViewModels;
using music.Models;

namespace music.Pages
{
    public sealed partial class ArtistTopSongsPage : Page
    {
        public ArtistTopSongsViewModel? ViewModel { get; private set; }

        public ArtistTopSongsPage()
        {
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is NavigationRequest request)
            {
                DetachViewModel();
                ViewModel = (ArtistTopSongsViewModel)request.CreateViewModel();
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

        private void SongsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is music.Core.ViewModels.Items.SongItem songItem && ViewModel != null)
            {
                ViewModel.PlaySongAsync(songItem).Forget();
            }
        }

        public List<Song> GetSongs() => ViewModel?.SongModels.ToList() ?? new List<Song>();
    }
}
