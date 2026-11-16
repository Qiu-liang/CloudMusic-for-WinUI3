using System;
using System.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using music.Core.Abstractions;
using music.Core.ViewModels;

namespace music.Pages
{
    public sealed partial class ProfilePage : Page
    {
        public ProfileViewModel? ViewModel { get; private set; }

        public ProfilePage()
        {
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is NavigationRequest request)
            {
                DetachViewModel();
                ViewModel = (ProfileViewModel)request.CreateViewModel();
                ViewModel.PropertyChanged += ViewModel_PropertyChanged;
                Bindings.Update();

                if (ContentFrame.Content == null)
                {
                    ContentFrame.Navigate(typeof(ProfilePlaylistsPage), ViewModel.CreateChildRequest(PageKey.ProfilePlaylists));
                }

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
            if (ViewModel != null)
            {
                ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
                ViewModel.Dispose();
                ViewModel = null;
            }
            AvatarImage.Source = null;
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (ViewModel == null || e.PropertyName != nameof(ProfileViewModel.AvatarUrl)) return;
            AvatarImage.Source = string.IsNullOrEmpty(ViewModel.AvatarUrl)
                ? null
                : new BitmapImage(new Uri(ViewModel.AvatarUrl));
        }

        private void ProfileNavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (ViewModel == null || args.SelectedItemContainer == null) return;

            var tag = args.SelectedItemContainer.Tag?.ToString();
            switch (tag)
            {
                case "playlists":
                    ContentFrame.Navigate(typeof(ProfilePlaylistsPage), ViewModel.CreateChildRequest(PageKey.ProfilePlaylists));
                    break;
                case "recent":
                    ContentFrame.Navigate(typeof(RecentPage), ViewModel.CreateChildRequest(PageKey.Recent));
                    break;
                case "follows":
                    ContentFrame.Navigate(typeof(ProfileFollowListPage), ViewModel.CreateChildRequest(PageKey.ProfileFollowList, "follows"));
                    break;
                case "followeds":
                    ContentFrame.Navigate(typeof(ProfileFollowListPage), ViewModel.CreateChildRequest(PageKey.ProfileFollowList, "followeds"));
                    break;
            }
        }
    }
}
