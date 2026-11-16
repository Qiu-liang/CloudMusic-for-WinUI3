using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using music.Core.Abstractions;
using music.Core.Models;
using music.Core.Mvvm;
using music.Core.Results;
using music.Core.Services;
using music.Core.ViewModels;
using Xunit;

namespace music.Tests;

public class PlaylistDetailViewModelTests
{
    internal sealed class FakePlaylistService : IPlaylistService
    {
        public event EventHandler? UserPlaylistsChanged;
        public Func<string, Task<ApiResult<List<Song>>>>? OnGetDetail { get; set; }
        public Func<string, Task<PlaylistDetailInfo?>>? OnGetInfo { get; set; }
        public Func<string, bool, Task<ApiResult>>? OnSubscribe { get; set; }

        public Task<ApiResult<List<PlaylistInfo>>> GetUserPlaylistsAsync(long uid, bool bypassCache = false, CancellationToken ct = default) =>
            Task.FromResult(ApiResult<List<PlaylistInfo>>.Ok(new List<PlaylistInfo>()));

        public Task<ApiResult<List<Song>>> GetPlaylistDetailAsync(string playlistId, bool bypassCache = true, CancellationToken ct = default) =>
            OnGetDetail != null ? OnGetDetail(playlistId) : Task.FromResult(ApiResult<List<Song>>.Ok(new List<Song>()));

        public Task<PlaylistDetailInfo?> GetPlaylistInfoAsync(string playlistId, bool bypassCache = true, CancellationToken ct = default) =>
            OnGetInfo != null ? OnGetInfo(playlistId) : Task.FromResult<PlaylistDetailInfo?>(null);

        public Task<AlbumDetailInfo?> GetAlbumInfoAsync(string albumId, CancellationToken ct = default) => Task.FromResult<AlbumDetailInfo?>(null);
        public Task<ApiResult<List<Song>>> GetAlbumSongsAsync(string albumId, CancellationToken ct = default) =>
            Task.FromResult(ApiResult<List<Song>>.Ok(new List<Song>()));
        public Task<ApiResult> CreatePlaylistAsync(string name, CancellationToken ct = default) => Task.FromResult(new ApiResult(true, ApiErrorKind.None, string.Empty));
        public Task<ApiResult> DeletePlaylistAsync(string playlistId, CancellationToken ct = default) => Task.FromResult(new ApiResult(true, ApiErrorKind.None, string.Empty));

        public Task<ApiResult> SetPlaylistSubscriptionAsync(string playlistId, bool subscribe, CancellationToken ct = default) =>
            OnSubscribe != null ? OnSubscribe(playlistId, subscribe) : Task.FromResult(new ApiResult(true, ApiErrorKind.None, string.Empty));

        public void NotifyUserPlaylistsChanged() => UserPlaylistsChanged?.Invoke(this, EventArgs.Empty);
    }

    internal sealed class FakeArtistService : IArtistService
    {
        public Task<ApiResult<ArtistDetailInfo?>> GetArtistDetailAsync(string artistId, CancellationToken ct = default) =>
            Task.FromResult(ApiResult<ArtistDetailInfo?>.Ok(null));
        public Task<ApiResult<List<Song>>> GetArtistSongsAsync(string artistId, int limit = 50, CancellationToken ct = default) =>
            Task.FromResult(ApiResult<List<Song>>.Ok(new List<Song> { new() { Id = "a1", Name = "ArtistSong" } }));
        public Task<ApiResult<List<SearchAlbumInfo>>> GetArtistAlbumsAsync(string artistId, int limit = 50, CancellationToken ct = default) =>
            Task.FromResult(ApiResult<List<SearchAlbumInfo>>.Ok(new List<SearchAlbumInfo>()));
    }

    internal sealed class FakeLibraryService : ILibraryService
    {
        public bool HasLoadedLikedIds => true;
        public bool FavoritePlaylist { get; set; }
        public event EventHandler<SongLikeChangedEventArgs>? SongLikeChanged;
        public bool IsSongLiked(string? songId) => false;
        public Task EnsureLikedSongIdsLoadedAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<ApiResult<List<string>>> GetLikedSongIdsAsync(bool bypassCache = false, CancellationToken ct = default) =>
            Task.FromResult(ApiResult<List<string>>.Ok(new List<string>()));
        public Task<ApiResult<List<Song>>> GetLikedSongsAsync(int limit = 0, bool bypassCache = true, CancellationToken ct = default) =>
            Task.FromResult(ApiResult<List<Song>>.Ok(new List<Song>()));
        public Task<ApiResult> LikeSongAsync(string songId, bool like = true, CancellationToken ct = default) =>
            Task.FromResult(new ApiResult(true, ApiErrorKind.None, string.Empty));
        public Task<bool> IsFavoritePlaylistAsync(string? playlistId, CancellationToken ct = default) => Task.FromResult(FavoritePlaylist);
        public Task<string> GetFavoritePlaylistIdAsync(bool bypassCache = false, CancellationToken ct = default) =>
            Task.FromResult(FavoritePlaylist ? "fav1" : string.Empty);
        public Task<ApiResult<List<Song>>> LoadPlaylistSongsAsync(string playlistId, bool bypassCache = true, CancellationToken ct = default) =>
            Task.FromResult(ApiResult<List<Song>>.Ok(new List<Song>()));
        public void ClearState() { }
        public void RaiseLikeChanged(string id, bool liked) => SongLikeChanged?.Invoke(this, new SongLikeChangedEventArgs(id, liked));
    }

    internal sealed class FakeSessionService : ISessionService
    {
        public bool IsLoggedIn { get; set; }
        public long UserId { get; set; }
        public bool IsSecureConnection => true;
        public event EventHandler? SessionChanged;
        public event EventHandler? SessionCleared { add { } remove { } }
        public event EventHandler? VipStatusRefreshed { add { } remove { } }
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> LoginAnonymouslyAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<(bool, string, int, string)> LoginWithPhoneAsync(string p, string pw, CancellationToken ct = default) => Task.FromResult((true, "", 200, ""));
        public Task<QrCodePayload?> GenerateQrCodeAsync(CancellationToken ct = default) => Task.FromResult<QrCodePayload?>(null);
        public Task<ApiResult<QrLoginStatusResult>> CheckQrStatusAsync(string k, CancellationToken ct = default) =>
            Task.FromResult(ApiResult<QrLoginStatusResult>.Ok(new QrLoginStatusResult(QrLoginState.Waiting, 801, "")));
        public Task<bool> SetLoginCookieAsync(string cookie, CancellationToken ct = default) => Task.FromResult(true);
        public Task<ApiResult<UserInfo?>> GetUserInfoAsync(CancellationToken ct = default) => Task.FromResult(ApiResult<UserInfo?>.Ok(null));
        public UserInfo? GetCachedUserInfo() => null;
        public bool GetVipStatus() => false;
        public Task<bool> CheckVipStatusAsync(CancellationToken ct = default) => Task.FromResult(false);
        public void ResetLogin() { IsLoggedIn = false; UserId = 0; SessionChanged?.Invoke(this, EventArgs.Empty); }
    }

    internal sealed class FakePlayback : IPlaybackCoordinator
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
        public bool IsPlaying => false;
        public Song? CurrentSong => null;
        public bool IsShuffleEnabled => false;
        public double Volume { get; set; } = 0.8;
        public List<Song>? LastPlayedList { get; private set; }
        public int LastPlayedIndex { get; private set; } = -1;

        public List<Song> GetPlaylist() => new();
        public void SetPlaylist(List<Song> songs, int startIndex = 0) { }
        public Task PlayAsync(Song song) { LastPlayedList = [song]; LastPlayedIndex = 0; return Task.CompletedTask; }
        public Task PlayAsync(List<Song> songs, int index = 0) { LastPlayedList = songs; LastPlayedIndex = index; return Task.CompletedTask; }
        public Task PlayLocalAsync(string filePath, Song song) => Task.CompletedTask;
        public Task PlayLocalFilesAsync(IList<string> filePaths, IList<Song> songs, int index = 0) => Task.CompletedTask;
        public void Play() { }
        public void Pause() { }
        public void TogglePlayPause() { }
        public Task NextAsync() => Task.CompletedTask;
        public Task PreviousAsync() => Task.CompletedTask;
        public void ToggleShuffle() { }
        public void ToggleRepeat() { }
        public RepeatMode GetRepeatMode() => RepeatMode.None;
        public void Seek(TimeSpan position) { }
        public TimeSpan GetCurrentPosition() => TimeSpan.Zero;
        public TimeSpan GetDuration() => TimeSpan.Zero;
        public void NotifySongLikeChanged(string songId, bool isLiked) { }
        public void UnusedSuppress()
        {
            PlaybackStateChanged?.Invoke(this, false);
            CurrentSongChanged?.Invoke(this, new Song());
            PositionChanged?.Invoke(this, TimeSpan.Zero);
            DurationChanged?.Invoke(this, TimeSpan.Zero);
            VolumeChanged?.Invoke(this, 0);
            PlaybackFailed?.Invoke(this, "");
            ShuffleChanged?.Invoke(this, false);
            RepeatModeChanged?.Invoke(this, RepeatMode.None);
            PlaylistChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal sealed class FakeDialogService : IDialogService
    {
        public List<(string Title, string Message)> ShownMessages { get; } = new();
        public bool LoginResult { get; set; }
        public Task ShowMessageAsync(string title, string message) { ShownMessages.Add((title, message)); return Task.CompletedTask; }
        public Task<bool> ShowConfirmAsync(string t, string c, string p = "确定", string cl = "取消") => Task.FromResult(true);
        public Task<string?> ShowInputAsync(string t, string pl, string p = "确定", int m = 40) => Task.FromResult<string?>(null);
        public Task<bool> ShowLoginAsync() => Task.FromResult(LoginResult);
    }

    [Fact]
    public async Task LoadsErrorPreservingActualFailureEvenWhenPlaylistInfoSucceeds()
    {
        var playlist = new FakePlaylistService
        {
            OnGetDetail = _ => Task.FromResult(ApiResult<List<Song>>.Fail(ApiResult.NetworkError(), new List<Song>())),
            OnGetInfo = _ => Task.FromResult<PlaylistDetailInfo?>(new PlaylistDetailInfo { Id = "p1", Name = "MyList" })
        };

        using var vm = new PlaylistDetailViewModel(
            playlist, new FakeArtistService(), new FakeLibraryService(),
            new FakeSessionService(), new FakePlayback(), new FakeDialogService(),
            new ImmediateUiDispatcher(), new RecordingApiLog());

        await vm.LoadAsync("p1");

        Assert.Equal(LoadState.Error, vm.State);
        Assert.Equal("MyList", vm.Title);
        Assert.Contains("无法连接服务器", vm.ErrorMessage);
    }

    [Fact]
    public async Task LikeChangedUpdatesSongItemAndReloadsWhenFavoritePlaylist()
    {
        var loadCount = 0;
        var playlist = new FakePlaylistService
        {
            OnGetDetail = _ =>
            {
                loadCount++;
                return Task.FromResult(ApiResult<List<Song>>.Ok([new Song { Id = "s1", Name = "Song1" }]));
            },
            OnGetInfo = _ => Task.FromResult<PlaylistDetailInfo?>(new PlaylistDetailInfo { Id = "fav1", Name = "我喜欢的音乐", CreatorId = 1 })
        };
        var library = new FakeLibraryService { FavoritePlaylist = true };
        var session = new FakeSessionService { IsLoggedIn = true, UserId = 1 };

        using var vm = new PlaylistDetailViewModel(
            playlist, new FakeArtistService(), library, session,
            new FakePlayback(), new FakeDialogService(),
            new ImmediateUiDispatcher(), new RecordingApiLog());

        await vm.LoadAsync("fav1");
        Assert.Single(vm.Songs);
        Assert.False(vm.Songs[0].IsLiked);

        library.RaiseLikeChanged("s1", true);
        await Task.Delay(30, TestContext.Current.CancellationToken);

        Assert.True(loadCount >= 2);
    }

    [Fact]
    public async Task DisposePreventsStaleAsyncLoadFromApplying()
    {
        var gate = new TaskCompletionSource<ApiResult<List<Song>>>();
        var playlist = new FakePlaylistService { OnGetDetail = _ => gate.Task };

        var vm = new PlaylistDetailViewModel(
            playlist, new FakeArtistService(), new FakeLibraryService(),
            new FakeSessionService(), new FakePlayback(), new FakeDialogService(),
            new ImmediateUiDispatcher(), new RecordingApiLog());

        var loading = vm.LoadAsync("p1");
        vm.Dispose();
        gate.SetResult(ApiResult<List<Song>>.Ok([new Song { Id = "1", Name = "Late" }]));
        await loading;

        Assert.Empty(vm.Songs);
        Assert.Equal(LoadState.Loading, vm.State);
    }

    [Fact]
    public async Task SubscribeRequiresLoginAndPlaysClickedSong()
    {
        var subscribed = false;
        var playlist = new FakePlaylistService
        {
            OnGetDetail = _ => Task.FromResult(ApiResult<List<Song>>.Ok([new Song { Id = "s1", Name = "S1" }])),
            OnGetInfo = _ => Task.FromResult<PlaylistDetailInfo?>(new PlaylistDetailInfo { Id = "p2", Name = "Other", CreatorId = 99, Subscribed = false }),
            OnSubscribe = (_, target) => { subscribed = target; return Task.FromResult(new ApiResult(true, ApiErrorKind.None, "")); }
        };
        var session = new FakeSessionService { IsLoggedIn = false, UserId = 0 };
        var dialogs = new FakeDialogService { LoginResult = true };
        var playback = new FakePlayback();

        using var vm = new PlaylistDetailViewModel(
            playlist, new FakeArtistService(), new FakeLibraryService(),
            session, playback, dialogs,
            new ImmediateUiDispatcher(), new RecordingApiLog());

        await vm.LoadAsync("p2");
        Assert.True(vm.CanSubscribe);

        vm.ToggleSubscribeCommand.Execute(null);
        await Task.Delay(30, TestContext.Current.CancellationToken);
        Assert.True(subscribed);
        Assert.True(vm.IsSubscribed);

        await vm.PlaySongAsync(vm.Songs[0]);
        Assert.Equal(0, playback.LastPlayedIndex);
        Assert.Equal("s1", playback.LastPlayedList![0].Id);
    }
}
