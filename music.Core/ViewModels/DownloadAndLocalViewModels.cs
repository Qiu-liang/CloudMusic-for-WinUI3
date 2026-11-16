using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using music.Core.Abstractions;
using music.Core.Models;
using music.Core.Mvvm;
using music.Core.Services;
using music.Core.ViewModels.Items;

namespace music.Core.ViewModels
{
    public sealed class DownloadedViewModel : PageViewModelBase
    {
        private readonly IPageRequestFactory _requestFactory;

        public DownloadedViewModel(IPageRequestFactory requestFactory)
        {
            _requestFactory = requestFactory;
        }

        public NavigationRequest CreateChildRequest(PageKey key, object? parameter = null) =>
            _requestFactory.Create(key, parameter);
    }

    public sealed class DownloadViewModel : PageViewModelBase
    {
        private readonly IDownloadService _downloadService;
        private readonly IPlaybackCoordinator _playback;
        private readonly IDialogService _dialogs;
        private readonly IFilePickerService _filePicker;
        private readonly IApiLog _log;
        private string _countText = string.Empty;
        private string _downloadFolderPathText = string.Empty;

        public DownloadViewModel(
            IDownloadService downloadService,
            IPlaybackCoordinator playback,
            IDialogService dialogs,
            IFilePickerService filePicker,
            IApiLog log)
        {
            _downloadService = downloadService;
            _playback = playback;
            _dialogs = dialogs;
            _filePicker = filePicker;
            _log = log;
            Songs = new ObservableCollection<AudioFileItem>();
            OpenFolderCommand = new AsyncRelayCommand(OpenFolderAsync, log: log);
        }

        public ObservableCollection<AudioFileItem> Songs { get; }
        public AsyncRelayCommand OpenFolderCommand { get; }

        public string CountText
        {
            get => _countText;
            private set => SetProperty(ref _countText, value);
        }

        public string DownloadFolderPathText
        {
            get => _downloadFolderPathText;
            private set => SetProperty(ref _downloadFolderPathText, value);
        }

        public Task LoadAsync()
        {
            ShowLoading();
            try
            {
                var folder = _downloadService.GetCurrentFolder();
                DownloadFolderPathText = $"保存位置：{folder}";

                var entries = _downloadService.ScanDownloadedFiles();
                if (entries.Count == 0)
                {
                    Songs.Clear();
                    CountText = "共 0 首";
                    ShowEmpty();
                    return Task.CompletedTask;
                }

                Songs.Clear();
                foreach (var entry in entries)
                {
                    Songs.Add(new AudioFileItem
                    {
                        Id = entry.Id,
                        Name = entry.Name,
                        Artist = entry.Artist,
                        FilePath = entry.FilePath,
                        Folder = entry.Folder,
                        FileSize = entry.FileSize
                    });
                }

                CountText = $"共 {Songs.Count} 首";
                ShowContent();
            }
            catch (Exception ex)
            {
                _log.Write($"Download Load Error: {ex}");
                Songs.Clear();
                CountText = "共 0 首";
                ShowEmpty();
            }
            return Task.CompletedTask;
        }

        public Task PlaySongAsync(AudioFileItem? song)
        {
            if (song == null) return Task.CompletedTask;
            var songModels = Songs.Select(s => LocalSongFactory.Create(s.Name, s.Artist, s.FilePath)).ToList();
            var filePaths = Songs.Select(s => s.FilePath).ToList();
            var idx = Songs.IndexOf(song);
            return idx >= 0 ? _playback.PlayLocalFilesAsync(filePaths, songModels, idx) : Task.CompletedTask;
        }

        public async Task DeleteSongAsync(string? fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return;
            var confirmed = await _dialogs.ShowConfirmAsync("删除确认", $"确定要删除 {fileName} 吗？", "删除", "取消");
            if (!confirmed) return;

            try
            {
                _downloadService.DeleteDownloadedFile(fileName);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                _log.Write($"Download Delete Error: {ex}");
            }
        }

        private async Task OpenFolderAsync()
        {
            try
            {
                await _filePicker.LaunchFolderAsync(_downloadService.GetCurrentFolder());
            }
            catch (Exception ex)
            {
                _log.Write($"Download Open Folder Error: {ex}");
            }
        }
    }

    public sealed class LocalViewModel : PageViewModelBase
    {
        private readonly ILocalMusicService _localMusicService;
        private readonly IPlaybackCoordinator _playback;
        private readonly IFilePickerService _filePicker;
        private readonly IApiLog _log;
        private string _countText = string.Empty;
        private string _loadingText = "正在扫描...";

        public LocalViewModel(
            ILocalMusicService localMusicService,
            IPlaybackCoordinator playback,
            IFilePickerService filePicker,
            IApiLog log)
        {
            _localMusicService = localMusicService;
            _playback = playback;
            _filePicker = filePicker;
            _log = log;
            Songs = new ObservableCollection<AudioFileItem>();
            ScanCommand = new AsyncRelayCommand(ScanAsync, log: log);
        }

        public ObservableCollection<AudioFileItem> Songs { get; }
        public AsyncRelayCommand ScanCommand { get; }

        public string CountText
        {
            get => _countText;
            private set => SetProperty(ref _countText, value);
        }

        public string LoadingText
        {
            get => _loadingText;
            private set => SetProperty(ref _loadingText, value);
        }

        public Task LoadAsync()
        {
            try
            {
                var folders = _localMusicService.GetFolders();
                if (folders.Count == 0)
                {
                    Songs.Clear();
                    CountText = "共 0 首";
                    ShowEmpty();
                    return Task.CompletedTask;
                }

                ShowLoading();
                var entries = _localMusicService.ScanFolders();
                if (entries.Count == 0)
                {
                    Songs.Clear();
                    CountText = "共 0 首";
                    ShowEmpty();
                    return Task.CompletedTask;
                }

                Songs.Clear();
                foreach (var entry in entries)
                {
                    Songs.Add(new AudioFileItem
                    {
                        Id = entry.Id,
                        Name = entry.Name,
                        Artist = entry.Artist,
                        FilePath = entry.FilePath,
                        Folder = entry.Folder,
                        FileSize = entry.FileSize
                    });
                }

                CountText = $"共 {Songs.Count} 首";
                ShowContent();
            }
            catch (Exception ex)
            {
                _log.Write($"Local Load Error: {ex}");
                Songs.Clear();
                CountText = "共 0 首";
                ShowEmpty();
            }
            return Task.CompletedTask;
        }

        private async Task ScanAsync()
        {
            var folderPath = await _filePicker.PickFolderAsync();
            if (string.IsNullOrEmpty(folderPath)) return;

            _localMusicService.AddFolder(folderPath);
            LoadingText = $"正在扫描 {folderPath}...";
            await LoadAsync();
        }

        public Task PlaySongAsync(AudioFileItem? song)
        {
            if (song == null) return Task.CompletedTask;
            var songModels = Songs.Select(s => LocalSongFactory.Create(s.Name, s.Artist, s.FilePath)).ToList();
            var filePaths = Songs.Select(s => s.FilePath).ToList();
            var idx = Songs.IndexOf(song);
            return idx >= 0 ? _playback.PlayLocalFilesAsync(filePaths, songModels, idx) : Task.CompletedTask;
        }
    }
}
