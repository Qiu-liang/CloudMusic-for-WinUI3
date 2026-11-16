using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using music.Core.Abstractions;
using music.Core.Mvvm;
using music.Core.ViewModels;

namespace music.Pages
{
    public sealed partial class ArtistDetailPage : Page
    {
        private string _artistId = string.Empty;
        public ArtistDetailViewModel? ViewModel { get; private set; }

        public ArtistDetailPage()
        {
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is NavigationRequest request)
            {
                DetachViewModel();
                ViewModel = (ArtistDetailViewModel)request.CreateViewModel();
                ViewModel.PropertyChanged += ViewModel_PropertyChanged;
                Bindings.Update();

                if (request.Parameter is string artistId)
                {
                    _artistId = artistId;
                    if (ContentFrame.Content == null)
                    {
                        ContentFrame.Navigate(typeof(ArtistTopSongsPage), ViewModel.CreateChildRequest(PageKey.ArtistTopSongs, _artistId));
                    }
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
            if (ViewModel != null)
            {
                ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
                ViewModel.Dispose();
                ViewModel = null;
            }
            ArtistImage.Source = null;
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (ViewModel == null || e.PropertyName != nameof(ArtistDetailViewModel.ImageUrl)) return;
            ArtistImage.Source = string.IsNullOrEmpty(ViewModel.ImageUrl)
                ? null
                : new BitmapImage(new Uri(ViewModel.ImageUrl));
        }

        private void ArtistNavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (ViewModel == null || args.SelectedItemContainer == null || string.IsNullOrEmpty(_artistId)) return;

            var tag = args.SelectedItemContainer.Tag?.ToString();
            switch (tag)
            {
                case "songs":
                    ContentFrame.Navigate(typeof(ArtistTopSongsPage), ViewModel.CreateChildRequest(PageKey.ArtistTopSongs, _artistId));
                    ViewModel.ShowPlayAll = true;
                    break;
                case "albums":
                    ContentFrame.Navigate(typeof(ArtistAlbumsPage), ViewModel.CreateChildRequest(PageKey.ArtistAlbums, _artistId));
                    ViewModel.ShowPlayAll = false;
                    break;
            }
        }

        private void PlayAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null && ContentFrame.Content is ArtistTopSongsPage songsPage)
            {
                ViewModel.PlaySongsAsync(songsPage.GetSongs()).Forget();
            }
        }
    }
}
