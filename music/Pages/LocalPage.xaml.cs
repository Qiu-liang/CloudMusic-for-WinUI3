using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace music.Pages
{
    public class LocalSongItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string Folder { get; set; } = string.Empty;
        public long FileSize { get; set; }

        public string FileSizeFormatted
        {
            get
            {
                if (FileSize >= 1073741824)
                    return $"{FileSize / 1073741824.0:F2} GB";
                if (FileSize >= 1048576)
                    return $"{FileSize / 1048576.0:F2} MB";
                if (FileSize >= 1024)
                    return $"{FileSize / 1024.0:F2} KB";
                return $"{FileSize} B";
            }
        }
    }

    public sealed partial class LocalPage : Page
    {
        private readonly ObservableCollection<LocalSongItem> _songs = new();
        private readonly string _settingsKey = "LocalMusicFolders";

        public LocalPage()
        {
            InitializeComponent();
            SongsListView.ItemsSource = _songs;
            Loaded += LocalPage_Loaded;
        }

        private async void LocalPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadLocalMusicAsync();
        }

        private async System.Threading.Tasks.Task LoadLocalMusicAsync()
        {
            var settings = ApplicationData.Current.LocalSettings;
            var foldersJson = settings.Values[_settingsKey]?.ToString();

            if (string.IsNullOrEmpty(foldersJson))
            {
                ShowEmpty();
                return;
            }

            var folders = JsonSerializer.Deserialize<List<string>>(foldersJson);
            if (folders == null || folders.Count == 0)
            {
                ShowEmpty();
                return;
            }

            ShowLoading();

            try
            {
                var allFiles = new List<string>();
                foreach (var folder in folders)
                {
                    if (Directory.Exists(folder))
                    {
                        var files = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)
                            .Where(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                                       f.EndsWith(".flac", StringComparison.OrdinalIgnoreCase) ||
                                       f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                                       f.EndsWith(".aac", StringComparison.OrdinalIgnoreCase) ||
                                       f.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
                                       f.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase));
                        allFiles.AddRange(files);
                    }
                }

                if (allFiles.Count == 0)
                {
                    ShowEmpty();
                    return;
                }

                _songs.Clear();
                foreach (var file in allFiles)
                {
                    var fileInfo = new FileInfo(file);
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    var folderName = Path.GetDirectoryName(file) ?? "";

                    // 尝试解析文件名格式: 歌手 - 歌名
                    var parts = fileName.Split(" - ", 2);
                    var artist = parts.Length > 1 ? parts[0].Trim() : "未知艺术家";
                    var name = parts.Length > 1 ? parts[1].Trim() : fileName;

                    _songs.Add(new LocalSongItem
                    {
                        Id = fileInfo.Name,
                        Name = name,
                        Artist = artist,
                        FilePath = file,
                        Folder = Path.GetFileName(folderName),
                        FileSize = fileInfo.Length
                    });
                }

                CountText.Text = $"共 {_songs.Count} 首";
                ShowContent();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Local] Load Error: {ex.Message}");
                ShowEmpty();
            }
        }

        private void ShowLoading()
        {
            LoadingPanel.Visibility = Visibility.Visible;
            EmptyState.Visibility = Visibility.Collapsed;
            SongsListView.Visibility = Visibility.Collapsed;
        }

        private void ShowEmpty()
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            SongsListView.Visibility = Visibility.Collapsed;
            CountText.Text = "共 0 首";
        }

        private void ShowContent()
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Collapsed;
            SongsListView.Visibility = Visibility.Visible;
        }

        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            var folderPicker = new FolderPicker();
            folderPicker.SuggestedStartLocation = PickerLocationId.MusicLibrary;
            folderPicker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                var settings = ApplicationData.Current.LocalSettings;
                var foldersJson = settings.Values[_settingsKey]?.ToString();
                var folders = string.IsNullOrEmpty(foldersJson)
                    ? new List<string>()
                    : JsonSerializer.Deserialize<List<string>>(foldersJson) ?? new List<string>();

                if (!folders.Contains(folder.Path))
                {
                    folders.Add(folder.Path);
                    settings.Values[_settingsKey] = JsonSerializer.Serialize(folders);
                }

                LoadingText.Text = $"正在扫描 {folder.Path}...";
                await LoadLocalMusicAsync();
            }
        }

        private async void SongsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is LocalSongItem song)
            {
                try
                {
                    var mediaPlayer = MainWindow.PlaybackService;
                    var songModel = new Models.Song
                    {
                        Id = song.Id,
                        Name = song.Name,
                        Artists = new List<Models.Artist> { new Models.Artist { Name = song.Artist } }
                    };
                    await mediaPlayer.PlayAsync(songModel);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Local] Play Error: {ex.Message}");
                }
            }
        }
    }
}