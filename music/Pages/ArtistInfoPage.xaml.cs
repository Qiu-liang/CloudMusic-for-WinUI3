using System;
using System.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using music.Core.Abstractions;
using music.Core.ViewModels;

namespace music.Pages
{
    public sealed partial class ArtistInfoPage : Page
    {
        public ArtistInfoViewModel? ViewModel { get; private set; }

        public ArtistInfoPage()
        {
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is NavigationRequest request)
            {
                DetachViewModel();
                ViewModel = (ArtistInfoViewModel)request.CreateViewModel();
                ViewModel.PropertyChanged += ViewModel_PropertyChanged;
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
            if (ViewModel == null || e.PropertyName != nameof(ArtistInfoViewModel.ImageUrl)) return;
            ArtistImage.Source = string.IsNullOrEmpty(ViewModel.ImageUrl)
                ? null
                : new BitmapImage(new Uri(ViewModel.ImageUrl));
        }
    }
}
