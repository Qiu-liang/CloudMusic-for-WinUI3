using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using music.Core.Abstractions;
using music.Core.Mvvm;
using music.Core.Results;
using music.Core.Services;
using music.Core.ViewModels;
using music.Models;
using music.Services;
using Xunit;

namespace music.Tests;

public class ViewModelsAndArchitectureTests
{
    private sealed class StubNavigationService : INavigationService
    {
        public PageKey? CurrentPage { get; set; }
        public bool CanGoBack { get; set; }
        public event EventHandler<PageKey?>? Navigated;
        public List<(PageKey Key, object? Parameter)> Navigations { get; } = new();
        public List<PageKey[]> Refreshes { get; } = new();
        public int ClearBackStackCount { get; private set; }
        public int GoBackCount { get; private set; }

        public void Navigate(PageKey key, object? parameter = null)
        {
            CurrentPage = key;
            Navigations.Add((key, parameter));
            Navigated?.Invoke(this, key);
        }

        public void GoBack() => GoBackCount++;
        public void ClearBackStack() => ClearBackStackCount++;
        public void RefreshIfCurrent(params PageKey[] pages) => Refreshes.Add(pages);
    }

    private sealed class StubDialogService : IDialogService
    {
        public List<(string Title, string Message)> Messages { get; } = new();
        public bool LoginResult { get; set; }
        public bool ConfirmResult { get; set; } = true;
        public string? InputResult { get; set; }

        public Task ShowMessageAsync(string title, string message)
        {
            Messages.Add((title, message));
            return Task.CompletedTask;
        }

        public Task<bool> ShowConfirmAsync(string title, string content, string primaryButtonText = "确定", string closeButtonText = "取消") =>
            Task.FromResult(ConfirmResult);

        public Task<string?> ShowInputAsync(string title, string placeholder, string primaryButtonText = "确定", int maxLength = 40) =>
            Task.FromResult(InputResult);

        public Task<bool> ShowLoginAsync() => Task.FromResult(LoginResult);
    }

    private sealed class StubSessionService : ISessionService
    {
        public bool IsLoggedIn { get; set; }
        public long UserId { get; set; }
        public bool IsSecureConnection => true;
        public bool VipStatus { get; set; }
        public UserInfo? CachedUser { get; set; }
        public ApiResult<UserInfo?> NextUserInfoResult { get; set; } = ApiResult<UserInfo?>.Ok(null);
        public (bool Success, string Message, int Code, string RawResponse) NextPhoneResult { get; set; } = (true, "", 200, "MUSIC_U=ok");
        public ApiResult<QrLoginStatusResult> NextQrStatusResult { get; set; } =
            ApiResult<QrLoginStatusResult>.Ok(new QrLoginStatusResult(QrLoginState.Waiting, 801, ""));
        public QrCodePayload? NextQrPayload { get; set; } = new("k1", Convert.ToBase64String(new byte[] { 1, 2, 3 }));
        public event EventHandler? SessionChanged;
        public event EventHandler? SessionCleared { add { } remove { } }
        public event EventHandler? VipStatusRefreshed { add { } remove { } }

        public Task InitializeAsync() => Task.CompletedTask;
        public Task<bool> LoginAnonymouslyAsync() => Task.FromResult(true);
        public Task<(bool Success, string Message, int Code, string RawResponse)> LoginWithPhoneAsync(string phone, string password) =>
            Task.FromResult(NextPhoneResult);
        public Task<QrCodePayload?> GenerateQrCodeAsync(CancellationToken ct = default) =>
            Task.FromResult(NextQrPayload);
        public Task<ApiResult<QrLoginStatusResult>> CheckQrStatusAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(NextQrStatusResult);
        public Task<bool> SetLoginCookieAsync(string cookie) => Task.FromResult(true);
        public Task<ApiResult<UserInfo?>> GetUserInfoAsync() => Task.FromResult(NextUserInfoResult);
        public UserInfo? GetCachedUserInfo() => CachedUser;
        public bool GetVipStatus() => VipStatus;
        public Task<bool> CheckVipStatusAsync() => Task.FromResult(VipStatus);
        public void ResetLogin()
        {
            IsLoggedIn = false;
            UserId = 0;
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class StubLibraryService : ILibraryService
    {
        public bool HasLoadedLikedIds { get; set; } = true;
        public HashSet<string> LikedIds { get; } = new(StringComparer.Ordinal);
        public ApiResult<List<Song>> NextLikedSongsResult { get; set; } = ApiResult<List<Song>>.Ok(new List<Song>());
        public ApiResult NextLikeResult { get; set; } = new(true, ApiErrorKind.None, string.Empty);
        public event EventHandler<SongLikeChangedEventArgs>? SongLikeChanged;

        public bool IsSongLiked(string? songId) => songId != null && LikedIds.Contains(songId);
        public Task EnsureLikedSongIdsLoadedAsync() => Task.CompletedTask;
        public Task<ApiResult<List<string>>> GetLikedSongIdsAsync(bool bypassCache = false) =>
            Task.FromResult(ApiResult<List<string>>.Ok(LikedIds.ToList()));
        public Task<ApiResult<List<Song>>> GetLikedSongsAsync(int limit = 0, bool bypassCache = true) =>
            Task.FromResult(NextLikedSongsResult);
        public Task<ApiResult> LikeSongAsync(string songId, bool like = true)
        {
            if (NextLikeResult.Success)
            {
                if (like) LikedIds.Add(songId);
                else LikedIds.Remove(songId);
                SongLikeChanged?.Invoke(this, new SongLikeChangedEventArgs(songId, like));
            }
            return Task.FromResult(NextLikeResult);
        }
        public Task<bool> IsFavoritePlaylistAsync(string? playlistId) => Task.FromResult(false);
        public Task<string> GetFavoritePlaylistIdAsync(bool bypassCache = false) => Task.FromResult(string.Empty);
        public void ClearState() => LikedIds.Clear();
    }

    private sealed class StubPlayback : IPlaybackCoordinator
    {
        public event EventHandler<bool>? PlaybackStateChanged;
        public event EventHandler<Song>? CurrentSongChanged;
        public event EventHandler<TimeSpan>? PositionChanged;
        public event EventHandler<TimeSpan>? DurationChanged;
        public event EventHandler<double>? VolumeChanged;
        public event EventHandler<string>? PlaybackFailed;
        public event EventHandler<bool>? ShuffleChanged;
        public event EventHandler<RepeatMode>? RepeatModeChanged;
        public event EventHandler? PlaylistChanged;

        public bool IsPlaying { get; set; }
        public Song? CurrentSong { get; set; }
        public bool IsShuffleEnabled { get; set; }
        public double Volume { get; set; } = 0.8;
        public RepeatMode CurrentRepeatMode { get; set; } = RepeatMode.None;
        public TimeSpan CurrentPosition { get; set; }
        public TimeSpan CurrentDuration { get; set; }
        public TimeSpan? LastSeek { get; private set; }
        public List<Song>? LastPlayedSongs { get; private set; }
        public List<string> LikeNotifications { get; } = new();
        public List<Song> Queue { get; } = new();

        public List<Song> GetPlaylist() => Queue;
        public void SetPlaylist(List<Song> songs, int startIndex = 0) { Queue.Clear(); Queue.AddRange(songs); }
        public Task PlayAsync(Song song) { CurrentSong = song; LastPlayedSongs = [song]; CurrentSongChanged?.Invoke(this, song); return Task.CompletedTask; }
        public Task PlayAsync(List<Song> songs, int index = 0) { LastPlayedSongs = songs; return Task.CompletedTask; }
        public Task PlayLocalAsync(string filePath, Song song) => Task.CompletedTask;
        public Task PlayLocalFilesAsync(IList<string> filePaths, IList<Song> songs, int index = 0) => Task.CompletedTask;
        public void Play() { IsPlaying = true; PlaybackStateChanged?.Invoke(this, true); }
        public void Pause() { IsPlaying = false; PlaybackStateChanged?.Invoke(this, false); }
        public void TogglePlayPause() { IsPlaying = !IsPlaying; PlaybackStateChanged?.Invoke(this, IsPlaying); }
        public Task NextAsync() => Task.CompletedTask;
        public Task PreviousAsync() => Task.CompletedTask;
        public void ToggleShuffle() { IsShuffleEnabled = !IsShuffleEnabled; ShuffleChanged?.Invoke(this, IsShuffleEnabled); }
        public void ToggleRepeat()
        {
            CurrentRepeatMode = (RepeatMode)(((int)CurrentRepeatMode + 1) % 3);
            RepeatModeChanged?.Invoke(this, CurrentRepeatMode);
        }
        public RepeatMode GetRepeatMode() => CurrentRepeatMode;
        public void Seek(TimeSpan position) => LastSeek = position;
        public TimeSpan GetCurrentPosition() => CurrentPosition;
        public TimeSpan GetDuration() => CurrentDuration;
        public void NotifySongLikeChanged(string songId, bool isLiked) => LikeNotifications.Add($"{songId}:{isLiked}");

        public void RaiseSongChanged(Song song) { CurrentSong = song; CurrentSongChanged?.Invoke(this, song); }
        public void RaisePositionChanged(TimeSpan pos) { CurrentPosition = pos; PositionChanged?.Invoke(this, pos); }
        public void RaiseDurationChanged(TimeSpan dur) { CurrentDuration = dur; DurationChanged?.Invoke(this, dur); }
        public void RaiseVolumeChanged(double vol) { Volume = vol; VolumeChanged?.Invoke(this, vol); }
        public void RaiseFailed(string message) => PlaybackFailed?.Invoke(this, message);
        public void RaisePlaylistChanged() => PlaylistChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class StubQualityService : IQualityService
    {
        public SongQualityPrivilege? LastKnownPrivilege { get; set; }
        public string CurrentQuality { get; private set; } = "standard";
        public event EventHandler<string>? QualityChanged;

        public void SetQuality(string quality)
        {
            CurrentQuality = quality;
            QualityChanged?.Invoke(this, quality);
        }

        public void CachePrivilege(string songId, SongQualityPrivilege? privilege) => LastKnownPrivilege = privilege;
        public void ClearCache() => LastKnownPrivilege = null;
        public Task<SongQualityPrivilege?> GetSongQualityPrivilegeAsync(string songId, bool forceRefresh = false, CancellationToken ct = default) =>
            Task.FromResult(LastKnownPrivilege);
    }

    private sealed class StubPlaylistService : IPlaylistService
    {
        public event EventHandler? UserPlaylistsChanged;
        public List<PlaylistInfo> UserPlaylists { get; set; } = new();
        public Task<ApiResult<List<PlaylistInfo>>> GetUserPlaylistsAsync(long uid, bool bypassCache = false) =>
            Task.FromResult(ApiResult<List<PlaylistInfo>>.Ok(UserPlaylists));
        public Task<ApiResult<List<Song>>> GetPlaylistDetailAsync(string playlistId, bool bypassCache = true) =>
            Task.FromResult(ApiResult<List<Song>>.Ok(new List<Song>()));
        public Task<PlaylistDetailInfo?> GetPlaylistInfoAsync(string playlistId, bool bypassCache = true) => Task.FromResult<PlaylistDetailInfo?>(null);
        public Task<AlbumDetailInfo?> GetAlbumInfoAsync(string albumId) => Task.FromResult<AlbumDetailInfo?>(null);
        public Task<ApiResult<List<Song>>> GetAlbumSongsAsync(string albumId) =>
            Task.FromResult(ApiResult<List<Song>>.Ok(new List<Song>()));
        public Task<ApiResult> CreatePlaylistAsync(string name) => Task.FromResult(new ApiResult(true, ApiErrorKind.None, ""));
        public Task<ApiResult> DeletePlaylistAsync(string playlistId) => Task.FromResult(new ApiResult(true, ApiErrorKind.None, ""));
        public Task<ApiResult> SetPlaylistSubscriptionAsync(string playlistId, bool subscribe) => Task.FromResult(new ApiResult(true, ApiErrorKind.None, ""));
        public void NotifyUserPlaylistsChanged() => UserPlaylistsChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class StubSearchService : ISearchService
    {
        public List<Song> Songs { get; set; } = new();
        public List<SearchArtistInfo> Artists { get; set; } = new();
        public List<RecommendedPlaylist> Playlists { get; set; } = new();
        public List<SearchAlbumInfo> Albums { get; set; } = new();
        public ApiResult? NextError { get; set; }
        public Task<List<SearchSuggestion>> GetSearchSuggestionsAsync(string keywords, CancellationToken ct = default) =>
            Task.FromResult(new List<SearchSuggestion> { new() { Name = keywords, Artist = "Art" } });
        public Task<ApiResult<List<Song>>> SearchSongsAsync(string keywords, int limit = 30, int offset = 0) =>
            Task.FromResult(NextError != null ? ApiResult<List<Song>>.Fail(NextError, new List<Song>()) : ApiResult<List<Song>>.Ok(Songs));
        public Task<ApiResult<List<SearchArtistInfo>>> SearchArtistsAsync(string keywords, int limit = 10) =>
            Task.FromResult(NextError != null ? ApiResult<List<SearchArtistInfo>>.Fail(NextError, new List<SearchArtistInfo>()) : ApiResult<List<SearchArtistInfo>>.Ok(Artists));
        public Task<ApiResult<List<RecommendedPlaylist>>> SearchPlaylistsAsync(string keywords, int limit = 10) =>
            Task.FromResult(NextError != null ? ApiResult<List<RecommendedPlaylist>>.Fail(NextError, new List<RecommendedPlaylist>()) : ApiResult<List<RecommendedPlaylist>>.Ok(Playlists));
        public Task<ApiResult<List<SearchAlbumInfo>>> SearchAlbumsAsync(string keywords, int limit = 10) =>
            Task.FromResult(NextError != null ? ApiResult<List<SearchAlbumInfo>>.Fail(NextError, new List<SearchAlbumInfo>()) : ApiResult<List<SearchAlbumInfo>>.Ok(Albums));
    }

    private sealed class StubRecommendationService : IRecommendationService
    {
        public List<string> CallOrder { get; } = new();
        public ApiResult? NextError { get; set; }
        public Task<ApiResult<List<Song>>> GetDailyRecommendSongsAsync(int limit = 30)
        {
            CallOrder.Add("daily");
            return Task.FromResult(NextError != null
                ? ApiResult<List<Song>>.Fail(NextError, new List<Song>())
                : ApiResult<List<Song>>.Ok(new List<Song> { new() { Id = "d1", Name = "Daily1" } }));
        }
        public Task<ApiResult<List<RecommendedPlaylist>>> GetRecommendedPlaylistsAsync(int limit = 10)
        {
            CallOrder.Add("playlists");
            return Task.FromResult(NextError != null
                ? ApiResult<List<RecommendedPlaylist>>.Fail(NextError, new List<RecommendedPlaylist>())
                : ApiResult<List<RecommendedPlaylist>>.Ok(new List<RecommendedPlaylist> { new() { Id = "p1", Name = "Play1" } }));
        }
        public Task<ApiResult<List<RecommendedPlaylist>>> GetRecommendResourceAsync()
        {
            CallOrder.Add("radar");
            return Task.FromResult(NextError != null
                ? ApiResult<List<RecommendedPlaylist>>.Fail(NextError, new List<RecommendedPlaylist>())
                : ApiResult<List<RecommendedPlaylist>>.Ok(new List<RecommendedPlaylist> { new() { Id = "r1", Name = "Radar1" } }));
        }
        public Task<ApiResult<List<Song>>> GetPersonalizedSongsAsync(int limit = 10)
        {
            CallOrder.Add($"personalized:{limit}");
            return Task.FromResult(NextError != null
                ? ApiResult<List<Song>>.Fail(NextError, new List<Song>())
                : ApiResult<List<Song>>.Ok(new List<Song> { new() { Id = "s1", Name = "Per1" } }));
        }
        public Task<ApiResult<List<Song>>> GetIntelligenceSongsAsync()
        {
            CallOrder.Add("intelligence");
            return Task.FromResult(NextError != null
                ? ApiResult<List<Song>>.Fail(NextError, new List<Song>())
                : ApiResult<List<Song>>.Ok(new List<Song> { new() { Id = "i1", Name = "Int1" } }));
        }
    }

    private sealed class StubSettingsService : ISettingsService
    {
        public string ServerAddress { get; private set; } = "http://localhost:3000";
        public bool AllowInsecureLogin { get; set; }
        public bool FancyLyrics { get; set; }
        public string Theme { get; set; } = "System";
        public void UpdateServerAddress(string address) => ServerAddress = address;
    }

    private sealed class StubThemeService : IThemeService
    {
        public string CurrentTheme { get; private set; } = "System";
        public void ApplySavedTheme() { }
        public void ApplyTheme(string theme) => CurrentTheme = theme;
    }

    private sealed class StubDownloadService : IDownloadService
    {
        public string Folder { get; private set; } = @"C:\MusicDownloads";
        public ApiResult NextDownloadResult { get; set; } = new(true, ApiErrorKind.None, @"C:\MusicDownloads\song.mp3");
        public string GetDefaultFolder() => @"C:\MusicDownloads";
        public string GetCurrentFolder() => Folder;
        public string SaveFolder(string? folderPath) => Folder = folderPath ?? @"C:\MusicDownloads";
        public string ResetFolder() => Folder = @"C:\MusicDownloads";
        public bool IsDefaultFolder(string? folderPath) => folderPath == @"C:\MusicDownloads";
        public Task<ApiResult> DownloadSongAsync(Song song, CancellationToken ct = default) => Task.FromResult(NextDownloadResult);
        public List<LocalAudioFileEntry> ScanDownloadedFiles() => new();
        public void DeleteDownloadedFile(string fileName) { }
    }

    private sealed class StubConnectionTester : IServerConnectionTester
    {
        public Task<(bool Success, HttpStatusCode? StatusCode, string? ErrorMessage)> TestConnectionAsync(string serverAddress, CancellationToken ct = default) =>
            Task.FromResult<(bool, HttpStatusCode?, string?)>((true, HttpStatusCode.OK, null));
    }

    private sealed class StubFilePickerService : IFilePickerService
    {
        public Task<(Stream Stream, string FileName)?> PickAudioFileAsync() => Task.FromResult<(Stream, string)?>(null);
        public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);
        public Task<bool> LaunchFolderAsync(string folderPath) => Task.FromResult(true);
    }

    private sealed class StubLyricsLauncher : ILyricsWindowLauncher
    {
        public bool IsFancyLyricsEnabled { get; set; }
        public int OpenCount { get; private set; }
        public void OpenOrActivate() => OpenCount++;
    }

    private sealed class StubLyricsService : ILyricsService
    {
        public Func<string, CancellationToken, Task<LyricInfo?>>? Handler { get; set; }
        public Task<LyricInfo?> GetLyricsAsync(string songId, CancellationToken ct = default) =>
            Handler != null ? Handler(songId, ct) : Task.FromResult<LyricInfo?>(null);
    }

    private sealed class StubCoverArtService : ICoverArtService
    {
        public int DownloadCount { get; private set; }
        public byte[] Bytes { get; set; } = new byte[] { 9, 8, 7 };
        public Task<byte[]?> DownloadImageBytesAsync(string url, CancellationToken ct = default)
        {
            DownloadCount++;
            return Task.FromResult<byte[]?>(Bytes);
        }
    }

    [Fact]
    public async Task LikedViewModel_RequiresLoginAndLoadsSongs()
    {
        var session = new StubSessionService { IsLoggedIn = false };
        var library = new StubLibraryService();
        using var vm = new LikedViewModel(library, session, new StubPlayback(), new StubDialogService(), new ImmediateUiDispatcher(), new RecordingApiLog());

        await vm.LoadAsync();
        Assert.Equal(LoadState.Error, vm.State);
        Assert.Equal("请先登录", vm.ErrorMessage);

        session.IsLoggedIn = true;
        library.NextLikedSongsResult = ApiResult<List<Song>>.Ok(new List<Song> { new() { Id = "10", Name = "S1", Liked = true } });

        await vm.LoadAsync();
        Assert.Equal(LoadState.Content, vm.State);
        Assert.Single(vm.Songs);
        Assert.Equal("S1", vm.Songs[0].Name);
    }

    [Fact]
    public async Task SearchViewModel_LoadsResultsAndNavigates()
    {
        var search = new StubSearchService
        {
            Songs = new List<Song> { new() { Id = "1", Name = "Hit" } },
            Artists = new List<SearchArtistInfo> { new() { Id = "a1", Name = "Singer" } }
        };
        var playback = new StubPlayback();
        var nav = new StubNavigationService();
        using var vm = new SearchViewModel(search, playback, nav);

        await vm.LoadAsync("Hit");
        Assert.Equal(LoadState.Content, vm.State);
        Assert.True(vm.HasSongs);
        Assert.True(vm.HasArtists);

        await vm.PlaySongAtAsync(0);
        Assert.Equal("1", playback.LastPlayedSongs?[0].Id);

        vm.OpenArtist(vm.Artists[0]);
        Assert.Equal((PageKey.ArtistDetail, (object?)"a1"), nav.Navigations[0]);
    }

    [Fact]
    public async Task RecommendViewModel_PreservesSixCallOrderAndLoggedInVisibility()
    {
        var rec = new StubRecommendationService();
        var session = new StubSessionService { IsLoggedIn = true };
        var nav = new StubNavigationService();
        using var vm = new RecommendViewModel(rec, session, new StubPlayback(), nav, new RecordingApiLog());

        await vm.LoadAsync();

        Assert.Equal(LoadState.Content, vm.State);
        Assert.True(vm.ShowLoggedInSections);
        Assert.Equal(new[] { "playlists", "personalized:20", "daily", "radar", "personalized:38", "intelligence" }, rec.CallOrder);
        vm.OpenAllDailySongs();
        Assert.Equal((PageKey.RecommendDailySongs, (object?)"daily"), nav.Navigations[0]);
    }

    [Fact]
    public async Task SettingsViewModel_LoadsTestsConnectionAndEnforcesVipQualityFallback()
    {
        var settings = new StubSettingsService();
        var theme = new StubThemeService();
        var quality = new StubQualityService();
        var session = new StubSessionService { IsLoggedIn = true, VipStatus = false };
        var playback = new StubPlayback
        {
            CurrentSong = new Song
            {
                Id = "vip-song",
                QualityPrivilege = new SongQualityPrivilege
                {
                    FreeMaxLevel = "exhigh",
                    PlayableMaxLevel = "exhigh",
                    SongMaxLevel = "lossless",
                    AvailableQualities = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "standard", "higher", "exhigh", "lossless" },
                    ChargeTypesByQuality = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["lossless"] = 1 }
                }
            }
        };
        var dialogs = new StubDialogService();
        using var vm = new SettingsViewModel(
            settings, theme, quality, session, new StubDownloadService(),
            new StubConnectionTester(), playback, dialogs, new StubFilePickerService(), new ImmediateUiDispatcher());

        vm.Load();
        await vm.TestConnectionAsync();
        Assert.Equal(StatusTone.Success, vm.ServerStatusTone);

        // Select lossless (index 3) for non-VIP -> falls back to previous playable quality ("standard") and shows VIP dialog
        await vm.SelectQualityByIndexAsync(3);
        Assert.Single(dialogs.Messages);
        Assert.Equal("VIP 专属音质", dialogs.Messages[0].Title);
        Assert.Equal("standard", quality.CurrentQuality);
    }

    [Fact]
    public async Task LoginViewModel_ValidatesPhoneAndHandlesQrStates()
    {
        var session = new StubSessionService();
        using var vm = new LoginViewModel(session, new RecordingApiLog());

        var emptyPhone = await vm.LoginWithPhoneAsync("", "pw");
        Assert.False(emptyPhone.Success);
        Assert.True(vm.HasPhoneError);

        var okPhone = await vm.LoginWithPhoneAsync("13800138000", "secret");
        Assert.True(okPhone.Success);
        Assert.False(vm.HasPhoneError);
        Assert.Null(okPhone.ErrorMessage);

        await vm.GenerateQrCodeAsync();
        Assert.NotNull(vm.QrImageBytes);
        Assert.Equal("请使用网易云音乐APP扫码登录", vm.QrStatusText);
        Assert.True(vm.IsQrLoginActive);
    }

    [Fact]
    public async Task ShellViewModel_RestoresExpiredSessionAndLogoutNavigatesHome()
    {
        var session = new StubSessionService
        {
            IsLoggedIn = true,
            UserId = 123,
            NextUserInfoResult = ApiResult<UserInfo?>.Fail(new ApiResult(false, ApiErrorKind.Authentication, "unauthorized", 401), null)
        };
        var playlists = new StubPlaylistService
        {
            UserPlaylists = new List<PlaylistInfo> { new() { Id = "p1", Name = "Mine", CreatorId = 123 } }
        };
        var nav = new StubNavigationService { CurrentPage = PageKey.Profile };
        using var vm = new ShellViewModel(session, playlists, nav, new StubDialogService(), new ImmediateUiDispatcher(), new RecordingApiLog());

        vm.InitializeSessionRestore();
        await Task.Delay(30, TestContext.Current.CancellationToken);

        Assert.False(session.IsLoggedIn);
        Assert.Equal(PageKey.Home, nav.CurrentPage);

        vm.PerformLogout();
        Assert.NotEmpty(nav.Refreshes);
    }

    [Fact]
    public async Task PlayerBarViewModel_SyncsLikeSeekQualityAndLyricsLauncher()
    {
        var playback = new StubPlayback { CurrentDuration = TimeSpan.FromSeconds(100) };
        var library = new StubLibraryService();
        var quality = new StubQualityService();
        var session = new StubSessionService { IsLoggedIn = true };
        var launcher = new StubLyricsLauncher { IsFancyLyricsEnabled = true };
        var nav = new StubNavigationService();

        using var vm = new PlayerBarViewModel(
            playback, library, quality, session, new StubPlaylistService(),
            new StubDownloadService(), launcher, nav, new StubDialogService(),
            new ImmediateUiDispatcher(), new RecordingApiLog());

        var song = new Song { Id = "s100", Name = "Track", CoverImgUrl = "https://img/1.jpg" };
        playback.RaiseSongChanged(song);

        await vm.ToggleLikeAsync();
        Assert.True(vm.CurrentSongLiked);
        Assert.Contains("s100:True", playback.LikeNotifications);

        vm.ProgressPointerPressed();
        vm.ProgressValueChanged(50);
        vm.ProgressPointerReleased();
        Assert.Equal(TimeSpan.FromSeconds(50), playback.LastSeek);

        vm.OpenLyricsOrWindow();
        Assert.Equal(1, launcher.OpenCount);

        launcher.IsFancyLyricsEnabled = false;
        vm.OpenLyricsOrWindow();
        Assert.Equal(PageKey.Lyrics, nav.CurrentPage);
    }

    [Fact]
    public async Task LyricsWindowViewModel_ParsesTranslationsIgnoresStaleResponsesAndFiltersJitter()
    {
        var tcsOld = new TaskCompletionSource<LyricInfo?>();
        var lyricsService = new StubLyricsService
        {
            Handler = (id, _) => id == "old"
                ? tcsOld.Task
                : Task.FromResult<LyricInfo?>(new LyricInfo
                {
                    LrcLyric = "[00:01.00]Line1\n[00:05.00]Line2",
                    TranslatedLyric = "[00:05.00]行2"
                })
        };
        var cover = new StubCoverArtService();
        var playback = new StubPlayback
        {
            CurrentSong = new Song { Id = "old", Name = "Old", CoverImgUrl = "https://img/cover.jpg" },
            IsPlaying = true
        };

        using var vm = new LyricsWindowViewModel(lyricsService, cover, playback, new ImmediateUiDispatcher(), new RecordingApiLog());
        vm.Initialize();

        playback.RaiseSongChanged(new Song { Id = "new", Name = "New", CoverImgUrl = "https://img/cover.jpg" });
        tcsOld.SetResult(new LyricInfo { LrcLyric = "[00:01.00]Stale" });
        await Task.Delay(30, TestContext.Current.CancellationToken);

        Assert.Equal(2, vm.Lyrics.Count);
        Assert.Equal("Line2", vm.Lyrics[1].Text);
        Assert.Equal("行2", vm.Lyrics[1].Translation);

        playback.RaisePositionChanged(TimeSpan.FromSeconds(5.2));
        Assert.Equal(1, vm.CurrentLyricIndex);

        // Jitter backward by 300ms (<800ms) -> stays on line 1
        playback.RaisePositionChanged(TimeSpan.FromSeconds(4.9));
        Assert.Equal(1, vm.CurrentLyricIndex);

        // Large seek backward (>800ms) -> moves back to line 0
        playback.RaisePositionChanged(TimeSpan.FromSeconds(1.5));
        Assert.Equal(0, vm.CurrentLyricIndex);
        // Same cover URL was not re-downloaded twice unnecessarily
        Assert.Equal(1, cover.DownloadCount);
    }

    [Fact]
    public void ArchitectureGuard_ViewModelsDoNotDependOnNeteaseTransportOrHttp()
    {
        var vmAssembly = typeof(ShellViewModel).Assembly;
        var vmTypes = vmAssembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.StartsWith("music.Core.ViewModels", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(vmTypes);

        string[] forbiddenTypeNames =
        [
            "NeteaseHttpTransport",
            "SessionCookieJar",
            "ServerEndpoint",
            "HttpClient",
            "JsonElement"
        ];

        foreach (var type in vmTypes)
        {
            foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                foreach (var param in ctor.GetParameters())
                {
                    Assert.DoesNotContain(param.ParameterType.Name, forbiddenTypeNames);
                }
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                Assert.DoesNotContain(field.FieldType.Name, forbiddenTypeNames);
            }
        }
    }

    [Fact]
    public void ArchitectureGuard_ViewsContainZeroLegacyGlobalsOrDirectPlatformCalls()
    {
        var repoRoot = FindRepoRoot();
        var musicDir = Path.Combine(repoRoot, "music");

        var forbiddenRegex = new Regex(
            @"\b(App\.ApiService|MainWindow\.PlaybackService|MainWindow\.MainContentFrame|App\.m_window|ApplicationData\.Current\.LocalSettings|HttpClient|JsonElement)\b",
            RegexOptions.Compiled);

        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(musicDir, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(musicDir, file).Replace('\\', '/');
            if (rel.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) ||
                rel.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) ||
                rel.StartsWith("Composition/", StringComparison.OrdinalIgnoreCase) ||
                rel.StartsWith("Infrastructure/Platform/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var content = File.ReadAllText(file);
            foreach (Match m in forbiddenRegex.Matches(content))
            {
                violations.Add($"{rel}: {m.Value}");
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public async Task AsyncRelayCommand_MarshalsCanExecuteChangedToDispatcherAndLogsFullStackTrace()
    {
        var uiThreadId = Environment.CurrentManagedThreadId;
        var dispatchedThreadIds = new List<int>();
        var syncCtx = new TrackingSynchronizationContext();
        var dispatcher = new TrackingDispatcher(action =>
        {
            syncCtx.Post(_ =>
            {
                dispatchedThreadIds.Add(Environment.CurrentManagedThreadId);
                action();
            }, null);
        });
        var log = new RecordingApiLog();

        var canExecuteEvents = 0;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cmd = new AsyncRelayCommand(
            async () =>
            {
                await Task.Delay(10).ConfigureAwait(false);
                throw new InvalidOperationException("boom-cmd");
            },
            log: log,
            dispatcher: dispatcher);

        cmd.CanExecuteChanged += (_, _) =>
        {
            canExecuteEvents++;
            if (canExecuteEvents == 2) completed.TrySetResult();
        };

        await Task.Run(() => cmd.Execute(null), TestContext.Current.CancellationToken);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal(2, canExecuteEvents);
        Assert.Equal(2, dispatcher.EnqueueCount);
        Assert.Contains(log.Messages, m => m.Contains("InvalidOperationException") && m.Contains("boom-cmd") && m.Contains("at "));

        // Verify TaskExtensions.Forget also logs full exception stack trace
        var forgetLog = new RecordingApiLog();
        Task.Run(() => throw new ApplicationException("boom-forget"), TestContext.Current.CancellationToken).Forget(forgetLog, "ForgetTest");
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Contains(forgetLog.Messages, m => m.Contains("ApplicationException") && m.Contains("boom-forget") && m.Contains("at "));
    }

    [Fact]
    public async Task ViewModels_EnterErrorStateWithRealMessageWhenServicesFail()
    {
        var netErr = ApiResult.NetworkError();

        var rec = new StubRecommendationService { NextError = netErr };
        using var recVm = new RecommendViewModel(rec, new StubSessionService(), new StubPlayback(), new StubNavigationService(), new RecordingApiLog());
        await recVm.LoadAsync();
        Assert.Equal(LoadState.Error, recVm.State);
        Assert.Contains("无法连接服务器", recVm.ErrorMessage);

        var search = new StubSearchService { NextError = new ApiResult(false, ApiErrorKind.InvalidResponse, "坏 JSON") };
        using var searchVm = new SearchViewModel(search, new StubPlayback(), new StubNavigationService());
        await searchVm.LoadAsync("jay");
        Assert.Equal(LoadState.Error, searchVm.State);
        Assert.Equal("坏 JSON", searchVm.ErrorMessage);

        using var searchAllSongsVm = new SearchAllSongsViewModel(search, new StubPlayback(), new RecordingApiLog());
        await searchAllSongsVm.LoadAsync("jay");
        Assert.Equal(LoadState.Error, searchAllSongsVm.State);
        Assert.Equal("坏 JSON", searchAllSongsVm.ErrorMessage);
    }

    private sealed class TrackingDispatcher : IUiDispatcher
    {
        private readonly Action<Action> _dispatch;
        public int EnqueueCount { get; private set; }
        public TrackingDispatcher(Action<Action> dispatch) => _dispatch = dispatch;
        public bool TryEnqueue(Action action)
        {
            EnqueueCount++;
            _dispatch(action);
            return true;
        }
    }

    private sealed class TrackingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => ThreadPool.QueueUserWorkItem(_ => d(state));
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "music.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir) ?? string.Empty;
        }
        throw new DirectoryNotFoundException("Could not locate music.slnx repository root.");
    }
}
