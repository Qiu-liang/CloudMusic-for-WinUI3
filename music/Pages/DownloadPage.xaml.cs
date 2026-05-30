using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;

namespace music.Pages
{
    public class DownloadedSongItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
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

    public sealed partial class DownloadPage : Page
    {
        private readonly ObservableCollection<DownloadedSongItem> _songs = new();
        private readonly string _downloadFolder;

        public DownloadPage()
        {
            InitializeComponent();
            SongsListView.ItemsSource = _songs;
            _downloadFolder = Path.Combine(ApplicationData.Current.LocalFolder.Path, "Downloads");
            Loaded += DownloadPage_Loaded;
        }

        private async void DownloadPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadDownloadedSongsAsync();
        }

        private async System.Threading.Tasks.Task LoadDownloadedSongsAsync()
        {
            ShowLoading();

            try
            {
                if (!Directory.Exists(_downloadFolder))
                {
                    Directory.CreateDirectory(_downloadFolder);
                    ShowEmpty();
                    return;
                }

                var files = Directory.GetFiles(_downloadFolder, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                               f.EndsWith(".flac", StringComparison.OrdinalIgnoreCase) ||
                               f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                               f.EndsWith(".aac", StringComparison.OrdinalIgnoreCase) ||
                               f.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
                               f.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (files.Count == 0)
                {
                    ShowEmpty();
                    return;
                }

                _songs.Clear();
                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    
                    // 尝试解析文件名格式: 歌手 - 歌名
                    var parts = fileName.Split(" - ", 2);
                    var artist = parts.Length > 1 ? parts[0].Trim() : "未知艺术家";
                    var name = parts.Length > 1 ? parts[1].Trim() : fileName;

                    _songs.Add(new DownloadedSongItem
                    {
                        Id = fileInfo.Name,
                        Name = name,
                        Artist = artist,
                        FilePath = file,
                        FileSize = fileInfo.Length
                    });
                }

                CountText.Text = $"共 {_songs.Count} 首";
                ShowContent();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Download] Load Error: {ex.Message}");
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

        private void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: 打开下载对话框
        }

        private async void SongsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is DownloadedSongItem song)
            {
                // 播放本地文件
                try
                {
                    var mediaPlayer = MainWindow.PlaybackService;
                    // 创建一个临时的Song对象用于播放
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
                    System.Diagnostics.Debug.WriteLine($"[Download] Play Error: {ex.Message}");
                }
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string fileName)
            {
                var filePath = Path.Combine(_downloadFolder, fileName);
                
                var dialog = new ContentDialog
                {
                    Title = "删除确认",
                    Content = $"确定要删除 {fileName} 吗？",
                    PrimaryButtonText = "删除",
                    SecondaryButtonText = "取消",
                    XamlRoot = this.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    try
                    {
                        if (File.Exists(filePath))
                        {
                            File.Delete(filePath);
                            await LoadDownloadedSongsAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Download] Delete Error: {ex.Message}");
                    }
                }
            }
        }
    }
}