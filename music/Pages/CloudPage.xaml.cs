using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using music.Services;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace music.Pages
{
    public sealed partial class CloudPage : Page
    {
        private readonly ObservableCollection<CloudSongItem> _songs = new();
        private List<CloudSongInfo> _songModels = new();

        public CloudPage()
        {
            this.InitializeComponent();
            SongsListView.ItemsSource = _songs;
            Loaded += CloudPage_Loaded;
        }

        private async void CloudPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadCloudSongsAsync();
        }

        private async System.Threading.Tasks.Task LoadCloudSongsAsync()
        {
            if (!App.ApiService.IsLoggedIn)
            {
                ShowError("请先登录");
                return;
            }

            ShowLoading();

            var songs = await App.ApiService.GetCloudSongsAsync();

            if (songs == null || songs.Count == 0)
            {
                if (App.ApiService.LastError != ApiErrorKind.None)
                    ShowError(App.ApiService.LastErrorMessage);
                else
                    ShowEmptyState();
                return;
            }

            _songModels = songs;
            _songs.Clear();
            foreach (var song in songs)
            {
                _songs.Add(new CloudSongItem
                {
                    SongId = song.SongId,
                    SongName = song.SongName,
                    Artist = song.Artist,
                    Album = song.Album,
                    FileName = song.FileName,
                    CoverUrl = song.CoverUrl,
                    FileSizeFormatted = song.FileSizeFormatted,
                    DurationFormatted = song.DurationFormatted,
                    AddTimeFormatted = song.AddTimeFormatted
                });
            }

            SongsCountText.Text = $"共 {_songs.Count} 首";
            ShowContent();
        }

        private void ShowLoading()
        {
            LoadingPanel.Visibility = Visibility.Visible;
            EmptyState.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;
            SongsListView.Visibility = Visibility.Collapsed;
        }

        private void ShowEmptyState()
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            ErrorPanel.Visibility = Visibility.Collapsed;
            SongsListView.Visibility = Visibility.Collapsed;
        }

        private void ShowError(string message)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
            SongsListView.Visibility = Visibility.Collapsed;
            ErrorText.Text = message;
        }

        private void ShowContent()
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;
            SongsListView.Visibility = Visibility.Visible;
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadCloudSongsAsync();
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Dialogs.LoginDialog { XamlRoot = XamlRoot };
            await dialog.ShowAsync();
            if (App.ApiService.IsLoggedIn) await LoadCloudSongsAsync();
        }

        private async void UploadButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".mp3");
            picker.FileTypeFilter.Add(".flac");
            picker.FileTypeFilter.Add(".wav");
            picker.FileTypeFilter.Add(".aac");
            picker.FileTypeFilter.Add(".ogg");
            picker.FileTypeFilter.Add(".m4a");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                await UploadFileAsync(file);
            }
        }

        private async System.Threading.Tasks.Task UploadFileAsync(Windows.Storage.StorageFile file)
        {
            try
            {
                ShowLoading();
                var result = await App.ApiService.UploadCloudSongAsync(file);
                if (result.Success) await LoadCloudSongsAsync();
                else ShowError($"上传失败：{result.DisplayMessage}");
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Cloud] Upload Error: {ex.Message}");
                ShowError($"上传出错: {ex.Message}");
            }
        }

        private async void SongsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is CloudSongItem songItem)
            {
                var songs = _songModels.Select(item => new Models.Song
                {
                    Id = item.SongId,
                    Name = item.SongName,
                    Artists = new List<Models.Artist> { new Models.Artist { Name = item.Artist } },
                    Album = new Models.Album { Name = item.Album },
                    CoverImgUrl = item.CoverUrl
                }).ToList();
                var index = _songModels.FindIndex(item => item.SongId == songItem.SongId);
                if (index >= 0) await MainWindow.PlaybackService.PlayAsync(songs, index);
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string songId)
            {
                var dialog = new ContentDialog
                {
                    Title = "删除确认",
                    Content = "确定要从云盘删除这首歌吗？",
                    PrimaryButtonText = "删除",
                    SecondaryButtonText = "取消",
                    XamlRoot = this.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    var success = await App.ApiService.DeleteCloudSongAsync(songId);
                    if (success)
                    {
                        await LoadCloudSongsAsync();
                    }
                }
            }
        }
    }
}