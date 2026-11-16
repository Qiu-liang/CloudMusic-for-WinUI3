using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using music.Core.Abstractions;
using music.Core.Models;
using music.Core.Netease;
using music.Core.Netease.Mapping;
using music.Core.Results;

namespace music.Core.Services
{
    public interface IPlaylistService
    {
        event EventHandler? UserPlaylistsChanged;

        Task<ApiResult<List<PlaylistInfo>>> GetUserPlaylistsAsync(long uid, bool bypassCache = false, CancellationToken cancellationToken = default);
        Task<ApiResult<List<Song>>> GetPlaylistDetailAsync(string playlistId, bool bypassCache = true, CancellationToken cancellationToken = default);
        Task<PlaylistDetailInfo?> GetPlaylistInfoAsync(string playlistId, bool bypassCache = true, CancellationToken cancellationToken = default);
        Task<AlbumDetailInfo?> GetAlbumInfoAsync(string albumId, CancellationToken cancellationToken = default);
        Task<ApiResult<List<Song>>> GetAlbumSongsAsync(string albumId, CancellationToken cancellationToken = default);
        Task<ApiResult> CreatePlaylistAsync(string name, CancellationToken cancellationToken = default);
        Task<ApiResult> DeletePlaylistAsync(string playlistId, CancellationToken cancellationToken = default);
        Task<ApiResult> SetPlaylistSubscriptionAsync(string playlistId, bool subscribe, CancellationToken cancellationToken = default);
        void NotifyUserPlaylistsChanged();
    }

    public sealed class PlaylistService : IPlaylistService
    {
        private readonly NeteaseHttpTransport _transport;
        private readonly ILibraryService _libraryService;
        private readonly IQualityService _qualityService;
        private readonly IApiLog _log;

        public PlaylistService(
            NeteaseHttpTransport transport,
            ILibraryService libraryService,
            IQualityService qualityService,
            IApiLog? log = null)
        {
            _transport = transport;
            _libraryService = libraryService;
            _qualityService = qualityService;
            _log = log ?? NullApiLog.Instance;
        }

        public event EventHandler? UserPlaylistsChanged;

        public void NotifyUserPlaylistsChanged() => UserPlaylistsChanged?.Invoke(this, EventArgs.Empty);

        public async Task<ApiResult<List<PlaylistInfo>>> GetUserPlaylistsAsync(
            long uid,
            bool bypassCache = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var url = $"/user/playlist?uid={uid}";
                if (bypassCache) url = AppendTimestamp(url);
                var resp = await _transport.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (!resp.Success)
                {
                    return ApiResult<List<PlaylistInfo>>.Fail(resp.Result, new List<PlaylistInfo>());
                }

                return ApiResult<List<PlaylistInfo>>.Ok(PlaylistJsonMapper.ParseUserPlaylists(resp.Body));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Write($"GetPlaylists Error: {ex}");
                return ApiResult<List<PlaylistInfo>>.Fail(
                    new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message),
                    new List<PlaylistInfo>());
            }
        }

        public async Task<ApiResult<List<Song>>> GetPlaylistDetailAsync(
            string playlistId,
            bool bypassCache = true,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await _libraryService.LoadPlaylistSongsAsync(playlistId, bypassCache, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Write($"GetPlaylistDetail Error: {ex}");
                return ApiResult<List<Song>>.Fail(
                    new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message),
                    new List<Song>());
            }
        }

        public async Task<PlaylistDetailInfo?> GetPlaylistInfoAsync(
            string playlistId,
            bool bypassCache = true,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var url = $"/playlist/detail?id={Uri.EscapeDataString(playlistId)}";
                if (bypassCache) url = AppendTimestamp(url);
                var resp = await _transport.GetAsync(url, cancellationToken).ConfigureAwait(false);
                return resp.Success ? PlaylistJsonMapper.ParsePlaylistInfo(resp.Body, _log) : null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Write($"GetPlaylistInfo Error: {ex}");
                return null;
            }
        }

        public async Task<AlbumDetailInfo?> GetAlbumInfoAsync(string albumId, CancellationToken cancellationToken = default)
        {
            try
            {
                var resp = await _transport.GetAsync($"/album?id={Uri.EscapeDataString(albumId)}", cancellationToken).ConfigureAwait(false);
                return resp.Success ? AlbumJsonMapper.ParseAlbumInfo(resp.Body) : null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Write($"GetAlbumInfo Error: {ex}");
                return null;
            }
        }

        public async Task<ApiResult<List<Song>>> GetAlbumSongsAsync(string albumId, CancellationToken cancellationToken = default)
        {
            try
            {
                var resp = await _transport.GetAsync($"/album?id={Uri.EscapeDataString(albumId)}", cancellationToken).ConfigureAwait(false);
                if (!resp.Success)
                {
                    return ApiResult<List<Song>>.Fail(resp.Result, new List<Song>());
                }

                var songs = SongJsonMapper.ParseSongsList(
                    resp.Body,
                    id => _libraryService.HasLoadedLikedIds && _libraryService.IsSongLiked(id),
                    _qualityService.CachePrivilege);
                return ApiResult<List<Song>>.Ok(songs);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Write($"GetAlbumSongs Error: {ex}");
                return ApiResult<List<Song>>.Fail(
                    new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message),
                    new List<Song>());
            }
        }

        public async Task<ApiResult> CreatePlaylistAsync(string name, CancellationToken cancellationToken = default)
        {
            try
            {
                var resp = await _transport.GetAsync($"/playlist/create?name={Uri.EscapeDataString(name)}", cancellationToken).ConfigureAwait(false);
                if (!resp.Success) return resp.Result;

                if (AuthJsonMapper.TryParseCode200(resp.Body))
                {
                    UserPlaylistsChanged?.Invoke(this, EventArgs.Empty);
                    return new ApiResult(true, ApiErrorKind.None, string.Empty, 200, 200);
                }
                return new ApiResult(false, ApiErrorKind.Business, "创建歌单失败。");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Write($"CreatePlaylist Error: {ex}");
                return new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message);
            }
        }

        public async Task<ApiResult> DeletePlaylistAsync(string playlistId, CancellationToken cancellationToken = default)
        {
            try
            {
                var resp = await _transport.GetAsync($"/playlist/delete?id={Uri.EscapeDataString(playlistId)}", cancellationToken).ConfigureAwait(false);
                if (!resp.Success) return resp.Result;

                if (AuthJsonMapper.TryParseCode200(resp.Body))
                {
                    UserPlaylistsChanged?.Invoke(this, EventArgs.Empty);
                    return new ApiResult(true, ApiErrorKind.None, string.Empty, 200, 200);
                }
                return new ApiResult(false, ApiErrorKind.Business, "删除歌单失败。");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Write($"DeletePlaylist Error: {ex}");
                return new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message);
            }
        }

        public async Task<ApiResult> SetPlaylistSubscriptionAsync(string playlistId, bool subscribe, CancellationToken cancellationToken = default)
        {
            try
            {
                var resp = await _transport.GetAsync(
                    $"/playlist/subscribe?t={(subscribe ? 1 : 2)}&id={Uri.EscapeDataString(playlistId)}",
                    cancellationToken).ConfigureAwait(false);
                if (!resp.Success) return resp.Result;

                if (AuthJsonMapper.TryParseCode200(resp.Body))
                {
                    UserPlaylistsChanged?.Invoke(this, EventArgs.Empty);
                    return new ApiResult(true, ApiErrorKind.None, string.Empty, 200, 200);
                }
                return new ApiResult(false, ApiErrorKind.Business, "操作失败。");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Write($"SetPlaylistSubscription Error: {ex}");
                return new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message);
            }
        }

        private static string AppendTimestamp(string url) =>
            LikedMusicSyncHelper.AppendTimestamp(url, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }
}
