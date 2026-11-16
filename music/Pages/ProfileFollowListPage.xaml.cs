using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using music.Core.Abstractions;
using music.Core.ViewModels;

namespace music.Pages
{
    public sealed partial class ProfileFollowListPage : Page
    {
        public ProfileFollowListViewModel? ViewModel { get; private set; }

        public ProfileFollowListPage()
        {
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is NavigationRequest request)
            {
                DetachViewModel();
                ViewModel = (ProfileFollowListViewModel)request.CreateViewModel();
                Bindings.Update();

                var isFollows = (request.Parameter as string) == "follows";
                await ViewModel.LoadAsync(isFollows);
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

        private void User_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is Grid grid && grid.Tag is long userId && ViewModel != null)
            {
                ViewModel.OpenUser(userId);
            }
        }
    }
}
