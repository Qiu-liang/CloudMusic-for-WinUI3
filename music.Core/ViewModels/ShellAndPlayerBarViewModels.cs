using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using music.Core.Abstractions;
using music.Core.Models;
using music.Core.Mvvm;
using music.Core.Netease;
using music.Core.Results;
using music.Core.Services;

namespace music.Core.ViewModels
{
    public sealed class ShellViewModel : ObservableObject, IDisposable
    {
        private readonly ISessionService _sessionService;
        private readonly IPlaylistService _playlistService;
        private readonly INavigationService _navigation;
        private readonly IDialogService _dialogs;
        private readonly IUiDispatcher _dispatcher;
        private readonly IApiLog _log;

        private string _loginStatusText = "未登录";
        private string _loginSubText = "点击登录";
        private bool _showLogoutButton;
        private bool _showVipBadge;
        private bool _showBackBar;
        private bool _disposed;

        public ShellViewModel(
            ISessionService sessionService,
            IPlaylistService playlistService,
            INavigationService navigation,
            IDialogService dialogs,
            IUiDispatcher dispatcher,
            IApiLog log)
        {
            _sessionService = sessionService;
            _playlistService = playlistService;
            _navigation = navigation;
            _dialogs = dialogs;
            _dispatcher = dispatcher;
            _log = log;

            CreatedPlaylists = new List<PlaylistInfo>();
            CollectedPlaylists = new List<PlaylistInfo>();

            _sessionService.SessionChanged += OnSessionChanged;
            _playlistService.UserPlaylistsChanged += OnUserPlaylistsChanged;
            _navigation.Navigated += OnNavigated;

            UpdateLoginStatus();
        }

        public event EventHandler? PlaylistsMenuUpdated;

        public IReadOnlyList<PlaylistInfo> CreatedPlaylists { get; private set; }
        public IReadOnlyList<PlaylistInfo> CollectedPlaylists { get; private set; }

        public string LoginStatusText { get => _loginStatusText; private set => SetProperty(ref _loginStatusText, value); }
        public string LoginSubText { get => _loginSubText; private set => SetProperty(ref _loginSubText, value); }
        public bool ShowLogoutButton { get => _showLogoutButton; private set => SetProperty(ref _showLogoutButton, value); }
        public bool ShowVipBadge { get => _showVipBadge; private set => SetProperty(ref _showVipBadge, value); }
        public bool ShowBackBar { get => _showBackBar; private set => SetProperty(ref _showBackBar, value); }

        public void InitializeSessionRestore()
        {
            if (_sessionService.IsLoggedIn)
            {
                ValidateAndRestoreLoginAsync().Forget(_log, "Shell.RestoreLogin");
            }
        }

        public void UpdateLoginStatus()
        {
            _dispatcher.TryEnqueue(() =>
            {
                if (_disposed) return;
                var cachedUser = _sessionService.GetCachedUserInfo();
                if (cachedUser != null && _sessionService.IsLoggedIn)
                {
                    LoginStatusText = cachedUser.Nickname;
                    LoginSubText = "已登录";
                    ShowLogoutButton = true;
                    ShowVipBadge = false;
                }
                else if (_sessionService.IsLoggedIn)
                {
                    LoginStatusText = "已登录";
                    LoginSubText = $"ID: {_sessionService.UserId}";
                    ShowLogoutButton = true;
                    ShowVipBadge = false;
                }
                else
                {
                    LoginStatusText = "未登录";
                    LoginSubText = "点击登录";
                    ShowLogoutButton = false;
                    ShowVipBadge = false;
                }
            });
        }

        public async Task HandleLoginItemTappedAsync()
        {
            if (_sessionService.IsLoggedIn)
            {
                _navigation.Navigate(PageKey.Profile);
                return;
            }

            if (await _dialogs.ShowLoginAsync() && _sessionService.IsLoggedIn)
            {
                UpdateLoginStatus();
                await LoadUserInfoAndPlaylistsAsync();
                _navigation.RefreshIfCurrent(PageKey.Recommend, PageKey.Liked);
            }
        }

        public async Task LoadUserInfoAndPlaylistsAsync()
        {
            var userInfoResult = await _sessionService.GetUserInfoAsync();
            if (userInfoResult.Value != null)
            {
                var userInfo = userInfoResult.Value;
                var isVip = await _sessionService.CheckVipStatusAsync();
                _dispatcher.TryEnqueue(() =>
                {
                    if (_disposed) return;
                    LoginStatusText = userInfo.Nickname;
                    LoginSubText = "已登录";
                    ShowLogoutButton = true;
                    ShowVipBadge = isVip;
                });

                await LoadPlaylistsAsync(bypassCache: false);
            }
        }

        private async Task ValidateAndRestoreLoginAsync()
        {
            var userInfoResult = await _sessionService.GetUserInfoAsync();
            if (userInfoResult.Value == null && userInfoResult.Status.Error != ApiErrorKind.Network)
            {
                _dispatcher.TryEnqueue(() =>
                {
                    if (_disposed) return;
                    LoginStatusText = "登录已过期";
                    LoginSubText = "点击登录";
                });
                _sessionService.ResetLogin();
                UpdateLoginStatus();

                if (_navigation.CurrentPage == PageKey.Profile)
                {
                    _navigation.Navigate(PageKey.Home);
                }
                return;
            }

            if (userInfoResult.Value != null)
            {
                await LoadUserInfoAndPlaylistsAsync();
            }
        }

        public Task RefreshPlaylistsAsync() => LoadPlaylistsAsync(bypassCache: true);

        private async Task LoadPlaylistsAsync(bool bypassCache)
        {
            if (!_sessionService.IsLoggedIn) return;

            var uid = _sessionService.UserId;
            var result = await _playlistService.GetUserPlaylistsAsync(uid, bypassCache);
            if (!result.Success || result.Value.Count == 0) return;

            var playlists = result.Value;
            _dispatcher.TryEnqueue(() =>
            {
                if (_disposed) return;
                CreatedPlaylists = playlists.Where(p => p.CreatorId == uid).ToList();
                CollectedPlaylists = playlists.Where(p => p.CreatorId != uid).ToList();
                PlaylistsMenuUpdated?.Invoke(this, EventArgs.Empty);
            });
        }

        public void PerformLogout()
        {
            _sessionService.ResetLogin();
            UpdateLoginStatus();

            if (_navigation.CurrentPage == PageKey.Profile)
            {
                _navigation.Navigate(PageKey.Home);
            }

            ClearPlaylistsMenu();
            _navigation.RefreshIfCurrent(PageKey.Recommend, PageKey.Liked);
        }

        private void ClearPlaylistsMenu()
        {
            _dispatcher.TryEnqueue(() =>
            {
                if (_disposed) return;
                CreatedPlaylists = new List<PlaylistInfo>();
                CollectedPlaylists = new List<PlaylistInfo>();
                PlaylistsMenuUpdated?.Invoke(this, EventArgs.Empty);
            });
        }

        public void NavigateByTag(string? tag, bool isSettings)
        {
            if (isSettings)
            {
                _navigation.ClearBackStack();
                ShowBackBar = false;
                _navigation.Navigate(PageKey.Settings);
                return;
            }

            if (string.IsNullOrEmpty(tag) || tag == "login" || tag == "create_playlist")
                return;

            _navigation.ClearBackStack();
            ShowBackBar = false;

            switch (tag)
            {
                case "recommend":
                    _navigation.Navigate(PageKey.Recommend);
                    break;
                case "liked":
                    _navigation.Navigate(PageKey.Liked);
                    break;
                case "downloaded":
                    _navigation.Navigate(PageKey.Downloaded);
                    break;
                case "cloud":
                    _navigation.Navigate(PageKey.Cloud);
                    break;
                default:
                    if (tag.StartsWith("playlist_", StringComparison.Ordinal))
                    {
                        var playlistId = tag.Substring("playlist_".Length);
                        _navigation.Navigate(PageKey.PlaylistDetail, playlistId);
                    }
                    break;
            }
        }

        public void GoBack() => _navigation.GoBack();

        public async Task CreatePlaylistAsync()
        {
            if (!_sessionService.IsLoggedIn)
            {
                await _dialogs.ShowLoginAsync();
                return;
            }

            var name = await _dialogs.ShowInputAsync("新建歌单", "请输入歌单名称", "创建", 40);
            if (!string.IsNullOrWhiteSpace(name))
            {
                var res = await _playlistService.CreatePlaylistAsync(name);
                if (res.Success)
                {
                    await LoadPlaylistsAsync(bypassCache: true);
                }
            }
        }

        public async Task DeletePlaylistAsync(string playlistId)
        {
            if (await _dialogs.ShowConfirmAsync("删除歌单", "确定要删除这个歌单吗？此操作不可撤销。", "删除", "取消"))
            {
                var res = await _playlistService.DeletePlaylistAsync(playlistId);
                if (res.Success)
                {
                    await LoadPlaylistsAsync(bypassCache: true);
                }
            }
        }

        public async Task UnsubscribePlaylistAsync(string playlistId)
        {
            if (await _dialogs.ShowConfirmAsync("取消收藏", "确定要取消收藏这个歌单吗？", "确定", "取消"))
            {
                var res = await _playlistService.SetPlaylistSubscriptionAsync(playlistId, false);
                if (res.Success)
                {
                    await LoadPlaylistsAsync(bypassCache: true);
                }
            }
        }

        private void OnSessionChanged(object? sender, EventArgs e)
        {
            UpdateLoginStatus();
            if (!_sessionService.IsLoggedIn)
            {
                ClearPlaylistsMenu();
            }
        }

        private void OnUserPlaylistsChanged(object? sender, EventArgs e)
        {
            LoadPlaylistsAsync(bypassCache: true).Forget(_log, "Shell.PlaylistsChanged");
        }

        private void OnNavigated(object? sender, PageKey? page)
        {
            _dispatcher.TryEnqueue(() =>
            {
                if (_disposed) return;
                ShowBackBar = page is PageKey.PlaylistDetail
                    or PageKey.ArtistDetail
                    or PageKey.AllPlaylists
                    or PageKey.Search
                    or PageKey.SearchAllResults
                    or PageKey.SearchAllSongs
                    or PageKey.RecommendDailySongs
                    or PageKey.RecommendRadar
                    or PageKey.Profile;
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _sessionService.SessionChanged -= OnSessionChanged;
            _playlistService.UserPlaylistsChanged -= OnUserPlaylistsChanged;
            _navigation.Navigated -= OnNavigated;
        }
    }

    public sealed class PlayerBarViewModel : ObservableObject, IDisposable
    {
        private static readonly string[] QualityTags =
            ["standard", "higher", "exhigh", "lossless", "hires", "jyeffect", "sky", "dolby", "jymaster"];

        private readonly IPlaybackCoordinator _playback;
        private readonly ILibraryService _libraryService;
        private readonly IQualityService _qualityService;
        private readonly ISessionService _sessionService;
        private readonly IPlaylistService _playlistService;
        private readonly IDownloadService _downloadService;
        private readonly ILyricsWindowLauncher _lyricsLauncher;
        private readonly INavigationService _navigation;
        private readonly IDialogService _dialogs;
        private readonly IUiDispatcher _dispatcher;
        private readonly IApiLog _log;
        private readonly ProgressDragState _progressDrag = new();

        private long _playbackErrorVersion;
        private string _songTitle = "未播放歌曲";
        private string _artistName = "未知艺术家";
        private string _coverUrl = string.Empty;
        private bool _isPlaying;
        private bool _currentSongLiked;
        private bool _isLikeEnabled;
        private bool _isDownloadEnabled;
        private string _currentTimeText = "00:00";
        private string _totalTimeText = "00:00";
        private double _progressPercent;
        private double _volumePercent = 80;
        private bool _isShuffleEnabled;
        private RepeatMode _repeatMode = RepeatMode.None;
        private string _qualityText = "标准";
        private IReadOnlyList<QualityOptionDisplay> _qualityOptions = Array.Empty<QualityOptionDisplay>();
        private bool _isErrorOpen;
        private string _errorMessage = string.Empty;
        private bool _disposed;

        public PlayerBarViewModel(
            IPlaybackCoordinator playback,
            ILibraryService libraryService,
            IQualityService qualityService,
            ISessionService sessionService,
            IPlaylistService playlistService,
            IDownloadService downloadService,
            ILyricsWindowLauncher lyricsLauncher,
            INavigationService navigation,
            IDialogService dialogs,
            IUiDispatcher dispatcher,
            IApiLog log)
        {
            _playback = playback;
            _libraryService = libraryService;
            _qualityService = qualityService;
            _sessionService = sessionService;
            _playlistService = playlistService;
            _downloadService = downloadService;
            _lyricsLauncher = lyricsLauncher;
            _navigation = navigation;
            _dialogs = dialogs;
            _dispatcher = dispatcher;
            _log = log;

            _playback.PlaybackStateChanged += OnPlaybackStateChanged;
            _playback.CurrentSongChanged += OnCurrentSongChanged;
            _playback.PositionChanged += OnPositionChanged;
            _playback.DurationChanged += OnDurationChanged;
            _playback.PlaybackFailed += OnPlaybackFailed;
            _playback.ShuffleChanged += OnShuffleChanged;
            _playback.RepeatModeChanged += OnRepeatModeChanged;
            _playback.PlaylistChanged += OnPlaylistChanged;
            _playback.VolumeChanged += OnVolumeChanged;
            _sessionService.SessionChanged += OnSessionChanged;
            _qualityService.QualityChanged += OnQualityChanged;
            _libraryService.SongLikeChanged += OnSongLikeChanged;

            LoadQualitySetting();
        }

        public event EventHandler? PlaylistUpdated;

        public string SongTitle { get => _songTitle; private set => SetProperty(ref _songTitle, value); }
        public string ArtistName { get => _artistName; private set => SetProperty(ref _artistName, value); }
        public string CoverUrl
        {
            get => _coverUrl;
            private set
            {
                if (SetProperty(ref _coverUrl, value))
                    OnPropertyChanged(nameof(HasCover));
            }
        }
        public bool HasCover => !string.IsNullOrEmpty(CoverUrl);
        public bool IsPlaying { get => _isPlaying; private set => SetProperty(ref _isPlaying, value); }
        public bool CurrentSongLiked { get => _currentSongLiked; private set => SetProperty(ref _currentSongLiked, value); }
        public bool IsLikeEnabled { get => _isLikeEnabled; private set => SetProperty(ref _isLikeEnabled, value); }
        public bool IsDownloadEnabled { get => _isDownloadEnabled; private set => SetProperty(ref _isDownloadEnabled, value); }
        public string CurrentTimeText { get => _currentTimeText; private set => SetProperty(ref _currentTimeText, value); }
        public string TotalTimeText { get => _totalTimeText; private set => SetProperty(ref _totalTimeText, value); }
        public double ProgressPercent { get => _progressPercent; private set => SetProperty(ref _progressPercent, value); }
        public double VolumePercent { get => _volumePercent; private set => SetProperty(ref _volumePercent, value); }
        public bool IsShuffleEnabled { get => _isShuffleEnabled; private set => SetProperty(ref _isShuffleEnabled, value); }
        public RepeatMode RepeatMode { get => _repeatMode; private set => SetProperty(ref _repeatMode, value); }
        public string QualityText { get => _qualityText; private set => SetProperty(ref _qualityText, value); }
        public IReadOnlyList<QualityOptionDisplay> QualityOptions { get => _qualityOptions; private set => SetProperty(ref _qualityOptions, value); }
        public bool IsErrorOpen { get => _isErrorOpen; set => SetProperty(ref _isErrorOpen, value); }
        public string ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }

        public Song? CurrentSong => _playback.CurrentSong;
        public List<Song> GetPlaylist() => _playback.GetPlaylist();

        public void TogglePlayPause() => _playback.TogglePlayPause();
        public Task NextAsync() => _playback.NextAsync();
        public Task PreviousAsync() => _playback.PreviousAsync();
        public void ToggleShuffle() => _playback.ToggleShuffle();
        public void ToggleRepeat() => _playback.ToggleRepeat();
        public Task PlayFromPlaylistIndexAsync(int index)
        {
            var playlist = _playback.GetPlaylist();
            return index >= 0 && index < playlist.Count
                ? _playback.PlayAsync(playlist, index)
                : Task.CompletedTask;
        }

        public void SetVolumePercent(double value)
        {
            _playback.Volume = value / 100.0;
        }

        public void ProgressPointerPressed() => _progressDrag.PointerPressed();

        public void ProgressValueChanged(double newValue)
        {
            var duration = _playback.GetDuration();
            if (duration > TimeSpan.Zero)
                _progressDrag.ValueChanged(newValue, duration);
        }

        public void ProgressPointerReleased()
        {
            var pos = _progressDrag.PointerReleased();
            if (pos.HasValue) _playback.Seek(pos.Value);
        }

        public void ProgressPointerCaptureLost()
        {
            var pos = _progressDrag.PointerCaptureLost();
            if (pos.HasValue) _playback.Seek(pos.Value);
        }

        public void OpenLyricsOrWindow()
        {
            if (_lyricsLauncher.IsFancyLyricsEnabled)
            {
                _lyricsLauncher.OpenOrActivate();
                return;
            }

            if (_navigation.CurrentPage == PageKey.Lyrics)
            {
                if (_navigation.CanGoBack) _navigation.GoBack();
                return;
            }

            var currentSong = _playback.CurrentSong;
            if (currentSong != null)
            {
                _navigation.Navigate(PageKey.Lyrics, currentSong);
            }
        }

        public async Task ToggleLikeAsync()
        {
            var song = _playback.CurrentSong;
            if (song == null || !_sessionService.IsLoggedIn) return;

            IsLikeEnabled = false;
            var target = !CurrentSongLiked;
            var res = await _libraryService.LikeSongAsync(song.Id, target);
            if (res.Success)
            {
                CurrentSongLiked = target;
                _playback.NotifySongLikeChanged(song.Id, target);
                IsLikeEnabled = true;
                _playlistService.NotifyUserPlaylistsChanged();
                return;
            }

            await _dialogs.ShowMessageAsync("操作失败", res.DisplayMessage);
            IsLikeEnabled = _sessionService.IsLoggedIn && string.IsNullOrEmpty(song.LocalFilePath);
        }

        public async Task DownloadCurrentAsync()
        {
            var song = _playback.CurrentSong;
            if (song == null || !string.IsNullOrEmpty(song.LocalFilePath)) return;

            IsDownloadEnabled = false;
            var result = await _downloadService.DownloadSongAsync(song);
            IsDownloadEnabled = string.IsNullOrEmpty(song.LocalFilePath);
            await _dialogs.ShowMessageAsync(
                result.Success ? "下载完成" : "下载失败",
                result.Success ? $"已保存到：{result.Message}" : result.DisplayMessage);
        }

        public SongQualityPrivilege? GetCurrentSongPrivilege() =>
            _playback.CurrentSong?.QualityPrivilege ?? _qualityService.LastKnownPrivilege;

        public async Task<SongQualityPrivilege?> EnsureCurrentSongPrivilegeAsync(bool forceRefresh = false)
        {
            var song = _playback.CurrentSong;
            if (song != null && string.IsNullOrEmpty(song.LocalFilePath) && !string.IsNullOrWhiteSpace(song.Id))
            {
                if (forceRefresh || song.QualityPrivilege == null)
                {
                    song.QualityPrivilege = await _qualityService.GetSongQualityPrivilegeAsync(song.Id, forceRefresh);
                }
                return song.QualityPrivilege ?? _qualityService.LastKnownPrivilege;
            }

            return _qualityService.LastKnownPrivilege;
        }

        public void LoadQualitySetting()
        {
            var storedQuality = _qualityService.CurrentQuality;
            var isVip = _sessionService.IsLoggedIn && _sessionService.GetVipStatus();
            var privilege = GetCurrentSongPrivilege();
            var effectiveQuality = privilege != null
                ? privilege.ResolveFallbackQuality(storedQuality, isVip)
                : SongUrlPolicy.NormalizeQuality(storedQuality);

            if (!string.Equals(storedQuality, effectiveQuality, StringComparison.Ordinal))
            {
                _qualityService.SetQuality(effectiveQuality);
            }

            QualityText = SongUrlPolicy.GetQualityDisplayName(effectiveQuality);
            UpdateQualityMenuAvailability(isVip, privilege);
        }

        public async Task OnQualityMenuOpeningAsync()
        {
            var isVip = _sessionService.IsLoggedIn && _sessionService.GetVipStatus();
            UpdateQualityMenuAvailability(isVip, GetCurrentSongPrivilege());

            var privilege = await EnsureCurrentSongPrivilegeAsync();
            if (privilege != null)
            {
                _dispatcher.TryEnqueue(() =>
                {
                    if (!_disposed) UpdateQualityMenuAvailability(isVip, privilege);
                });
            }
        }

        public async Task SelectQualityAsync(string quality)
        {
            var previousQuality = _qualityService.CurrentQuality;
            var isVip = _sessionService.IsLoggedIn && _sessionService.GetVipStatus();
            var privilege = await EnsureCurrentSongPrivilegeAsync();

            if (privilege != null && !privilege.CanUserPlay(quality, isVip))
            {
                var fallback = privilege.ResolveFallbackQuality(quality, isVip, previousQuality);
                _qualityService.SetQuality(fallback);
                QualityText = SongUrlPolicy.GetQualityDisplayName(fallback);
                UpdateQualityMenuAvailability(isVip, privilege);

                await _dialogs.ShowMessageAsync(
                    "VIP 专属音质",
                    $"当前歌曲的“{SongUrlPolicy.GetQualityDisplayName(quality)}”音质需要 VIP 会员权限，已为您退回“{SongUrlPolicy.GetQualityDisplayName(fallback)}”音质。");
                return;
            }

            _qualityService.SetQuality(quality);
            QualityText = SongUrlPolicy.GetQualityDisplayName(quality);
            UpdateQualityMenuAvailability(isVip, privilege);

            if (_playback.CurrentSong != null &&
                _playback.IsPlaying &&
                string.IsNullOrEmpty(_playback.CurrentSong.LocalFilePath) &&
                !string.Equals(previousQuality, quality, StringComparison.OrdinalIgnoreCase))
            {
                await _playback.PlayAsync(_playback.CurrentSong);
            }
        }

        private void UpdateQualityMenuAvailability(bool isVip, SongQualityPrivilege? privilege)
        {
            var list = new List<QualityOptionDisplay>(QualityTags.Length);
            foreach (var tag in QualityTags)
            {
                var baseName = SongUrlPolicy.GetQualityDisplayName(tag);
                var requiresVip = privilege?.RequiresVip(tag) ?? false;
                var canPlay = privilege?.CanUserPlay(tag, isVip) ?? true;
                list.Add(new QualityOptionDisplay
                {
                    Tag = tag,
                    Text = requiresVip ? $"{baseName} (VIP)" : baseName,
                    Opacity = canPlay ? 1.0 : 0.55
                });
            }
            QualityOptions = list;
        }

        private void UpdateSongInfo(Song? song)
        {
            if (song == null) return;

            SongTitle = song.Name;
            ArtistName = song.ArtistNames;
            if (_libraryService.HasLoadedLikedIds && !string.IsNullOrEmpty(song.Id))
            {
                song.Liked = _libraryService.IsSongLiked(song.Id);
            }
            CurrentSongLiked = song.Liked;
            IsLikeEnabled = _sessionService.IsLoggedIn && string.IsNullOrEmpty(song.LocalFilePath);
            IsDownloadEnabled = string.IsNullOrEmpty(song.LocalFilePath);
            CoverUrl = song.CoverImgUrl;

            LoadQualitySetting();
            if (_sessionService.IsLoggedIn &&
                string.IsNullOrEmpty(song.LocalFilePath) &&
                !string.IsNullOrWhiteSpace(song.Id) &&
                !_libraryService.HasLoadedLikedIds)
            {
                RefreshCurrentSongLikeStateAsync(song).Forget(_log, "PlayerBar.RefreshLike");
            }
            if (string.IsNullOrEmpty(song.LocalFilePath) && song.QualityPrivilege == null)
            {
                RefreshSongPrivilegeForUiAsync(song).Forget(_log, "PlayerBar.RefreshPrivilege");
            }
        }

        private async Task RefreshCurrentSongLikeStateAsync(Song song)
        {
            await _libraryService.EnsureLikedSongIdsLoadedAsync();
            if (!_libraryService.HasLoadedLikedIds) return;
            var liked = _libraryService.IsSongLiked(song.Id);
            song.Liked = liked;
            _dispatcher.TryEnqueue(() =>
            {
                if (!_disposed && ReferenceEquals(_playback.CurrentSong, song))
                    CurrentSongLiked = liked;
            });
        }

        private async Task RefreshSongPrivilegeForUiAsync(Song song)
        {
            var privilege = await _qualityService.GetSongQualityPrivilegeAsync(song.Id);
            if (privilege == null) return;
            song.QualityPrivilege = privilege;
            _dispatcher.TryEnqueue(() =>
            {
                if (!_disposed && ReferenceEquals(_playback.CurrentSong, song))
                    LoadQualitySetting();
            });
        }

        private void OnPlaybackStateChanged(object? sender, bool isPlaying) =>
            _dispatcher.TryEnqueue(() => { if (!_disposed) IsPlaying = isPlaying; });

        private void OnCurrentSongChanged(object? sender, Song song) =>
            _dispatcher.TryEnqueue(() =>
            {
                if (_disposed) return;
                _playbackErrorVersion++;
                IsErrorOpen = false;
                UpdateSongInfo(song);
            });

        private void OnPositionChanged(object? sender, TimeSpan position) =>
            _dispatcher.TryEnqueue(() =>
            {
                if (_disposed || _progressDrag.IsDragging) return;
                CurrentTimeText = FormatTime(position);
                var duration = _playback.GetDuration();
                if (duration.TotalSeconds > 0)
                    ProgressPercent = (position.TotalSeconds / duration.TotalSeconds) * 100;
            });

        private void OnDurationChanged(object? sender, TimeSpan duration) =>
            _dispatcher.TryEnqueue(() => { if (!_disposed) TotalTimeText = FormatTime(duration); });

        private void OnPlaybackFailed(object? sender, string message) =>
            _dispatcher.TryEnqueue(() =>
            {
                if (_disposed) return;
                var version = ++_playbackErrorVersion;
                ErrorMessage = message;
                IsErrorOpen = true;
                AutoDismissErrorAsync(version).Forget(_log, "PlayerBar.DismissError");
            });

        private async Task AutoDismissErrorAsync(long version)
        {
            await Task.Delay(5000);
            _dispatcher.TryEnqueue(() =>
            {
                if (!_disposed && _playbackErrorVersion == version)
                    IsErrorOpen = false;
            });
        }

        private void OnShuffleChanged(object? sender, bool enabled) =>
            _dispatcher.TryEnqueue(() => { if (!_disposed) IsShuffleEnabled = enabled; });

        private void OnRepeatModeChanged(object? sender, RepeatMode mode) =>
            _dispatcher.TryEnqueue(() => { if (!_disposed) RepeatMode = mode; });

        private void OnPlaylistChanged(object? sender, EventArgs e) =>
            _dispatcher.TryEnqueue(() => { if (!_disposed) PlaylistUpdated?.Invoke(this, EventArgs.Empty); });

        private void OnVolumeChanged(object? sender, double volume) =>
            _dispatcher.TryEnqueue(() => { if (!_disposed) VolumePercent = volume * 100.0; });

        private void OnSessionChanged(object? sender, EventArgs e) =>
            _dispatcher.TryEnqueue(() =>
            {
                if (_disposed) return;
                var current = _playback.CurrentSong;
                if (current != null) UpdateSongInfo(current);
                LoadQualitySetting();
            });

        private void OnQualityChanged(object? sender, string quality) =>
            _dispatcher.TryEnqueue(() => { if (!_disposed) LoadQualitySetting(); });

        private void OnSongLikeChanged(object? sender, SongLikeChangedEventArgs e) =>
            _dispatcher.TryEnqueue(() =>
            {
                if (_disposed) return;
                _playback.NotifySongLikeChanged(e.SongId, e.IsLiked);
                if (_playback.CurrentSong != null && string.Equals(_playback.CurrentSong.Id, e.SongId, StringComparison.Ordinal))
                    CurrentSongLiked = e.IsLiked;
            });

        private static string FormatTime(TimeSpan time) => $"{(int)time.TotalMinutes:D2}:{time.Seconds:D2}";

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _playback.PlaybackStateChanged -= OnPlaybackStateChanged;
            _playback.CurrentSongChanged -= OnCurrentSongChanged;
            _playback.PositionChanged -= OnPositionChanged;
            _playback.DurationChanged -= OnDurationChanged;
            _playback.PlaybackFailed -= OnPlaybackFailed;
            _playback.ShuffleChanged -= OnShuffleChanged;
            _playback.RepeatModeChanged -= OnRepeatModeChanged;
            _playback.PlaylistChanged -= OnPlaylistChanged;
            _playback.VolumeChanged -= OnVolumeChanged;
            _sessionService.SessionChanged -= OnSessionChanged;
            _qualityService.QualityChanged -= OnQualityChanged;
            _libraryService.SongLikeChanged -= OnSongLikeChanged;
        }
    }

    public sealed class SearchSuggestionsViewModel : ObservableObject, IDisposable
    {
        private readonly ISearchService _searchService;
        private readonly INavigationService _navigation;
        private CancellationTokenSource? _cts;
        private bool _disposed;

        public SearchSuggestionsViewModel(ISearchService searchService, INavigationService navigation)
        {
            _searchService = searchService;
            _navigation = navigation;
            Suggestions = new ObservableCollection<SearchSuggestion>();
        }

        public ObservableCollection<SearchSuggestion> Suggestions { get; }

        public async Task UpdateQueryAsync(string rawText, Func<string> getCurrentText)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var cancellation = _cts;
            var query = rawText.Trim();

            if (query.Length == 0)
            {
                Suggestions.Clear();
                return;
            }

            try
            {
                await Task.Delay(300, cancellation.Token);
                var results = await _searchService.GetSearchSuggestionsAsync(query, cancellation.Token);
                if (_disposed || cancellation.IsCancellationRequested || getCurrentText().Trim() != query)
                    return;

                Suggestions.Clear();
                foreach (var item in results)
                    Suggestions.Add(item);
            }
            catch (OperationCanceledException) { }
        }

        public void SubmitQuery(string? queryText)
        {
            if (!string.IsNullOrWhiteSpace(queryText))
            {
                _navigation.Navigate(PageKey.Search, queryText);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }
}
