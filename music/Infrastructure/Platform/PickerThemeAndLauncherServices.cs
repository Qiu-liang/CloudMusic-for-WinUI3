using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using Windows.System;
using music.Core.Abstractions;

namespace music.Infrastructure.Platform
{
    public sealed class FilePickerService : IFilePickerService
    {
        private readonly WindowHost _host;

        internal FilePickerService(WindowHost host)
        {
            _host = host;
        }

        public async Task<(Stream Stream, string FileName)?> PickAudioFileAsync()
        {
            if (_host.Window == null) return null;

            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.MusicLibrary
            };
            picker.FileTypeFilter.Add(".mp3");
            picker.FileTypeFilter.Add(".flac");
            picker.FileTypeFilter.Add(".wav");
            picker.FileTypeFilter.Add(".m4a");
            picker.FileTypeFilter.Add(".aac");
            picker.FileTypeFilter.Add(".ogg");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_host.Window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return null;

            var stream = await file.OpenStreamForReadAsync();
            return (stream, file.Name);
        }

        public async Task<string?> PickFolderAsync()
        {
            if (_host.Window == null) return null;

            var folderPicker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.MusicLibrary
            };
            folderPicker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_host.Window);
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

            var folder = await folderPicker.PickSingleFolderAsync();
            return folder?.Path;
        }

        public async Task<bool> LaunchFolderAsync(string folderPath)
        {
            Directory.CreateDirectory(folderPath);
            return await Launcher.LaunchFolderPathAsync(folderPath);
        }
    }

    public sealed class ThemeService : IThemeService
    {
        private const string ThemeKey = "Theme";
        private readonly ISettingsStore _settings;
        private readonly WindowHost _host;

        internal ThemeService(ISettingsStore settings, WindowHost host)
        {
            _settings = settings;
            _host = host;
        }

        public string CurrentTheme => _settings.GetString(ThemeKey) ?? "System";

        public void ApplySavedTheme()
        {
            ApplyTheme(CurrentTheme);
        }

        public void ApplyTheme(string theme)
        {
            _settings.SetString(ThemeKey, theme);
            _host.ApplyThemeCallback?.Invoke(theme);
        }
    }

    public sealed class LyricsWindowLauncher : ILyricsWindowLauncher
    {
        private readonly ISettingsStore _settings;
        private readonly Func<LyricsWindow> _windowFactory;

        public LyricsWindowLauncher(ISettingsStore settings, Func<LyricsWindow> windowFactory)
        {
            _settings = settings;
            _windowFactory = windowFactory;
        }

        public bool IsFancyLyricsEnabled => _settings.GetBoolean("FancyLyrics");

        public void OpenOrActivate()
        {
            var window = _windowFactory();
            window.Activate();
        }
    }
}
