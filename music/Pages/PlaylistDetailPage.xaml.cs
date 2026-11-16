using System;
using System.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using music.Core.Abstractions;
using music.Core.Mvvm;
using music.Core.ViewModels;
using music.Core.ViewModels.Items;

namespace music.Pages
{
    public sealed partial class PlaylistDetailPage : Page
    {
        public PlaylistDetailViewModel? ViewModel { get; private set; }

        public PlaylistDetailPage()
        {
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is NavigationRequest request)
            {
                DetachViewModel();
                ViewModel = (PlaylistDetailViewModel)request.CreateViewModel();
                ViewModel.PropertyChanged += ViewModel_PropertyChanged;
                Bindings.Update();

                if (request.Parameter is string playlistId)
                {
                    await ViewModel.LoadAsync(playlistId);
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
            if (ViewModel != null)
            {
                ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
                ViewModel.Dispose();
                ViewModel = null;
            }
            CoverImage.Source = null;
            CreatorAvatarBrush.ImageSource = null;
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (ViewModel == null) return;

            if (e.PropertyName == nameof(PlaylistDetailViewModel.CoverImageUrl))
            {
                CoverImage.Source = string.IsNullOrEmpty(ViewModel.CoverImageUrl)
                    ? null
                    : new BitmapImage(new Uri(ViewModel.CoverImageUrl));
            }
            else if (e.PropertyName == nameof(PlaylistDetailViewModel.CreatorAvatarUrl))
            {
                CreatorAvatarBrush.ImageSource = string.IsNullOrEmpty(ViewModel.CreatorAvatarUrl)
                    ? null
                    : new BitmapImage(new Uri(ViewModel.CreatorAvatarUrl));
            }
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
