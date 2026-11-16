using System;
using System.IO;
using System.Threading.Tasks;

namespace music.Core.Abstractions
{
    public enum PageKey
    {
        Home,
        Recommend,
        Liked,
        Downloaded,
        Cloud,
        Playlist,
        Collection,
        PlaylistDetail,
        ArtistDetail,
        ArtistTopSongs,
        ArtistAlbums,
        ArtistInfo,
        AllPlaylists,
        Search,
        SearchAllResults,
        SearchAllSongs,
        RecommendDailySongs,
        RecommendRadar,
        Profile,
        ProfilePlaylists,
        ProfileFollowList,
        Recent,
        Lyrics,
        Settings,
        Download,
        Local
    }

    public sealed record NavigationRequest(object? Parameter, Func<object> CreateViewModel);

    public interface IPageRequestFactory
    {
        NavigationRequest Create(PageKey key, object? parameter = null);
    }

    public interface INavigationService
    {
        PageKey? CurrentPage { get; }
        bool CanGoBack { get; }
        event EventHandler<PageKey?>? Navigated;

        void Navigate(PageKey key, object? parameter = null);
        void GoBack();
        void ClearBackStack();
        void RefreshIfCurrent(params PageKey[] pages);
    }

    public interface IDialogService
    {
        Task ShowMessageAsync(string title, string message);
        Task<bool> ShowConfirmAsync(string title, string content, string primaryButtonText = "确定", string closeButtonText = "取消");
        Task<string?> ShowInputAsync(string title, string placeholder, string primaryButtonText = "确定", int maxLength = 40);
        Task<bool> ShowLoginAsync();
    }

    public interface IFilePickerService
    {
        Task<(Stream Stream, string FileName)?> PickAudioFileAsync();
        Task<string?> PickFolderAsync();
        Task<bool> LaunchFolderAsync(string folderPath);
    }

    public interface IThemeService
    {
        string CurrentTheme { get; }
        void ApplySavedTheme();
        void ApplyTheme(string theme);
    }

    public interface ILyricsWindowLauncher
    {
        bool IsFancyLyricsEnabled { get; }
        void OpenOrActivate();
    }
}
