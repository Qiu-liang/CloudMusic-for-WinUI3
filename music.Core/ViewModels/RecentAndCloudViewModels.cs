using System;
using System.Collections.Generic;
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
    public sealed class RecentViewModel : PageViewModelBase
    {
        private readonly IProfileService _profileService;
        private readonly ISessionService _sessionService;
        private readonly IPlaybackCoordinator _playback;
        private List<RecentSong> _songModels = new();

        public RecentViewModel(
            IProfileService profileService,
            ISessionService sessionService,
            IPlaybackCoordinator playback,
            IApiLog log)
        {
            _profileService = profileService;
            _sessionService = sessionService;
            _playback = playback;
            Songs = new ObservableCollection<RecentSongItem>();
            RefreshCommand = new AsyncRelayCommand(LoadAsync, log: log);
        }

        public ObservableCollection<RecentSongItem> Songs { get; }
        public AsyncRelayCommand RefreshCommand { get; }

        public async Task LoadAsync()
        {
            var (version, token) = BeginLoad();

            if (!_sessionService.IsLoggedIn)
            {
                ShowError("请先登录");
                return;
            }

            ShowLoading();

            try
            {
                var result = await _profileService.GetRecentSongsAsync(100, token);
                if (!IsCurrentLoad(version, token)) return;

                if (!result.Success)
                {
                    _songModels.Clear();
                    Songs.Clear();
                    ShowError(result.Status.DisplayMessage);
                    return;
                }

                var songs = result.Value;
                if (songs.Count == 0)
                {
                    _songModels.Clear();
                    Songs.Clear();
                    ShowEmpty();
                    return;
                }

                _songModels = songs;
                Songs.Clear();
                foreach (var song in songs)
                {
                    Songs.Add(new RecentSongItem
                    {
                        Id = song.Id,
                        Name = song.Name,
                        Artist = song.Artist,
                        AlbumName = song.AlbumName,
                        DurationFormatted = song.DurationFormatted,
                        PlayTimeFormatted = song.PlayTimeFormatted,
                        PlatformName = song.PlatformName,
                        IsVip = song.IsVip
                    });
                }

                ShowContent();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested || IsDisposed)
            {
            }
        }

        public Task PlaySongAsync(RecentSongItem? item)
        {
            if (item == null) return Task.CompletedTask;
            var index = Songs.IndexOf(item);
            if (index < 0 || index >= _songModels.Count) return Task.CompletedTask;

            var song = _songModels[index];
            var songModel = new Song
            {
                Id = song.Id,
                Name = song.Name,
                Artists = new List<Artist> { new Artist { Name = song.Artist } },
                Album = new Album { Name = song.AlbumName },
                Fee = song.Fee,
                IsVip = song.IsVip
            };
            return _playback.PlayAsync(songModel);
        }
    }

    public sealed class CloudViewModel : PageViewModelBase
    {
        private readonly ICloudService _cloudService;
        private readonly ISessionService _sessionService;
        private readonly IPlaybackCoordinator _playback;
        private readonly IDialogService _dialogs;
        private readonly IFilePickerService _filePicker;
        private List<CloudSongInfo> _songModels = new();
        private string _songsCountText = string.Empty;

        public CloudViewModel(
            ICloudService cloudService,
            ISessionService sessionService,
            IPlaybackCoordinator playback,
            IDialogService dialogs,
            IFilePickerService filePicker,
            IApiLog log)
        {
            _cloudService = cloudService;
            _sessionService = sessionService;
            _playback = playback;
            _dialogs = dialogs;
            _filePicker = filePicker;

            Songs = new ObservableCollection<CloudSongItem>();
            RefreshCommand = new AsyncRelayCommand(LoadAsync, log: log);
            LoginCommand = new AsyncRelayCommand(LoginAndReloadAsync, log: log);
            UploadCommand = new AsyncRelayCommand(UploadAsync, log: log);
        }

        public ObservableCollection<CloudSongItem> Songs { get; }
        public AsyncRelayCommand RefreshCommand { get; }
        public AsyncRelayCommand LoginCommand { get; }
        public AsyncRelayCommand UploadCommand { get; }

        public string SongsCountText
        {
            get => _songsCountText;
            private set => SetProperty(ref _songsCountText, value);
        }

        public async Task LoadAsync()
        {
            var (version, token) = BeginLoad();

            if (!_sessionService.IsLoggedIn)
            {
                SongsCountText = string.Empty;
                ShowError("请先登录");
                return;
            }

            ShowLoading();

            try
            {
                var result = await _cloudService.GetCloudSongsAsync(cancellationToken: token);
                if (!IsCurrentLoad(version, token)) return;

                if (!result.Success)
                {
                    _songModels.Clear();
                    Songs.Clear();
                    SongsCountText = string.Empty;
                    ShowError(result.Status.DisplayMessage);
                    return;
                }

                if (result.Value.Count == 0)
                {
                    _songModels.Clear();
                    Songs.Clear();
                    SongsCountText = "共 0 首";
                    ShowEmpty();
                    return;
                }

                _songModels = result.Value;
                Songs.Clear();
                foreach (var song in result.Value)
                {
                    Songs.Add(new CloudSongItem
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

                SongsCountText = $"共 {Songs.Count} 首";
                ShowContent();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested || IsDisposed)
            {
            }
        }

        private async Task LoginAndReloadAsync()
        {
            if (await _dialogs.ShowLoginAsync())
                await LoadAsync();
        }

        private async Task UploadAsync()
        {
            var picked = await _filePicker.PickAudioFileAsync();
            if (picked == null) return;

            try
            {
                ShowLoading();
                await using (picked.Value.Stream)
                {
                    var result = await _cloudService.UploadCloudSongAsync(picked.Value.Stream, picked.Value.FileName);
                    if (result.Success)
                        await LoadAsync();
                    else
                        ShowError($"上传失败：{result.DisplayMessage}");
                }
            }
            catch (System.Exception ex)
            {
                ShowError($"上传出错: {ex.Message}");
            }
        }

        public Task PlaySongAsync(CloudSongItem? songItem)
        {
            if (songItem == null) return Task.CompletedTask;
            var songs = _songModels.Select(item => new Song
            {
                Id = item.SongId,
                Name = item.SongName,
                Artists = new List<Artist> { new Artist { Name = item.Artist } },
                Album = new Album { Name = item.Album },
                CoverImgUrl = item.CoverUrl
            }).ToList();
            var index = _songModels.FindIndex(item => item.SongId == songItem.SongId);
            return index >= 0 ? _playback.PlayAsync(songs, index) : Task.CompletedTask;
        }

        public async Task DeleteSongAsync(string? songId)
        {
            if (string.IsNullOrEmpty(songId)) return;
            var confirmed = await _dialogs.ShowConfirmAsync("删除确认", "确定要从云盘删除这首歌吗？", "删除", "取消");
            if (!confirmed) return;

            var result = await _cloudService.DeleteCloudSongAsync(songId);
            if (result.Success)
            {
                await LoadAsync();
            }
        }
    }
}
