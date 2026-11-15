using Windows.Storage;

namespace music.Services
{
    /// <summary>
    /// App-level persistence bridge for <see cref="DownloadFolderPolicy"/> backed by ApplicationData.LocalSettings.
    /// </summary>
    internal static class DownloadFolderStore
    {
        public static string GetDefaultFolder() =>
            DownloadFolderPolicy.GetDefaultDownloadFolder(
                localAppFolder: ApplicationData.Current.LocalFolder.Path);

        public static string GetCurrentFolder() =>
            DownloadFolderPolicy.GetCurrentDownloadFolder(
                ApplicationData.Current.LocalSettings.Values,
                ApplicationData.Current.LocalFolder.Path);

        public static string SaveFolder(string? folderPath) =>
            DownloadFolderPolicy.SaveDownloadFolder(
                ApplicationData.Current.LocalSettings.Values,
                folderPath,
                ApplicationData.Current.LocalFolder.Path);

        public static string ResetFolder() =>
            DownloadFolderPolicy.ResetDownloadFolder(
                ApplicationData.Current.LocalSettings.Values,
                ApplicationData.Current.LocalFolder.Path);
    }
}
