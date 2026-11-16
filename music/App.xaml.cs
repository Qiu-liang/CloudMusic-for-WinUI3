using Microsoft.UI.Xaml;
using music.Composition;
using music.Core.Mvvm;

namespace music
{
    public partial class App : Application
    {
        private readonly AppCompositionRoot _root;
        private Window? _window;

        public App()
        {
            InitializeComponent();
            _root = new AppCompositionRoot();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            var mainWindow = _root.CreateMainWindow();
            _window = mainWindow;

            _root.ThemeService?.ApplySavedTheme();
            _window.Activate();

            _root.SessionService.InitializeAsync().Forget(_root.ApiLog, "App.InitializeAsync");
        }
    }
}
