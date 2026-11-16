using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using music.Core.Netease;
using music.Core.Netease.Mapping;
using music.Core.Results;
using music.Models;
using music.Services;

namespace music.Core.Services
{
    public interface IPlaylistService
    {
        event EventHandler? UserPlaylistsChanged;

        Task<List<PlaylistInfo>> GetUserPlaylistsAsync(long uid, bool bypassCache = false);
        Task<ApiResult<List<Song>>> GetPlaylistDetailAsync(string playlistId, bool bypassCache = true);
        Task<PlaylistDetailInfo?> GetPlaylistInfoAsync(string playlistId, bool bypassCache = true);
        Task<AlbumDetailInfo?> GetAlbumInfoAsync(string albumId);
        Task<List<Song>> GetAlbumSongsAsync(string albumId);
        Task<ApiResult> CreatePlaylistAsync(string name);
        Task<ApiResult> DeletePlaylistAsync(string playlistId);
        Task<ApiResult> SetPlaylistSubscriptionAsync(string playlistId, bool subscribe);
        void NotifyUserPlaylistsChanged();
    }

    public sealed class PlaylistService : IPlaylistService
    {
        private readonly NeteaseHttpTransport _transport;
        private readonly LibraryService _libraryService;
        private readonly IQualityService _qualityService;

        public PlaylistService(
            NeteaseHttpTransport transport,
            LibraryService libraryService,
            IQualityService qualityService)
        {
            _transport = transport;
            _libraryService = libraryService;
            _qualityService = qualityService;
        }

        public event EventHandler? UserPlaylistsChanged;

        public void NotifyUserPlaylistsChanged() => UserPlaylistsChanged?.Invoke(this, EventArgs.Empty);

        public async Task<List<PlaylistInfo>> GetUserPlaylistsAsync(long uid, bool bypassCache = false)
        {
            try
            {
                var url = $"/user/playlist?uid={uid}";
                if (bypassCache) url = LibraryService.AppendTimestamp(url);
                var resp = await _transport.GetAsync(url).ConfigureAwait(false);
                return resp.Success ? PlaylistJsonMapper.ParseUserPlaylists(resp.Body) : new List<PlaylistInfo>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetPlaylists Error: {ex.Message}");
                return new List<PlaylistInfo>();
            }
        }

        public async Task<ApiResult<List<Song>>> GetPlaylistDetailAsync(string playlistId, bool bypassCache = true)
        {
            try
            {
                return await _libraryService.LoadPlaylistSongsInternalAsync(playlistId, bypassCache).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetPlaylistDetail Error: {ex.Message}");
                return ApiResult<List<Song>>.Ok(new List<Song>());
            }
        }

        public async Task<PlaylistDetailInfo?> GetPlaylistInfoAsync(string playlistId, bool bypassCache = true)
        {
            try
            {
                var url = $"/playlist/detail?id={Uri.EscapeDataString(playlistId)}";
                if (bypassCache) url = LibraryService.AppendTimestamp(url);
                var resp = await _transport.GetAsync(url).ConfigureAwait(false);
                return resp.Success ? PlaylistJsonMapper.ParsePlaylistInfo(resp.Body) : null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetPlaylistInfo Error: {ex.Message}");
                return null;
            }
        }

        public async Task<AlbumDetailInfo?> GetAlbumInfoAsync(string albumId)
        {
            try
            {
                var resp = await _transport.GetAsync($"/album?id={Uri.EscapeDataString(albumId)}").ConfigureAwait(false);
                return resp.Success ? AlbumJsonMapper.ParseAlbumInfo(resp.Body) : null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetAlbumInfo Error: {ex.Message}");
                return null;
            }
        }

        public async Task<List<Song>> GetAlbumSongsAsync(string albumId)
        {
            try
            {
                var resp = await _transport.GetAsync($"/album?id={Uri.EscapeDataString(albumId)}").ConfigureAwait(false);
                if (!resp.Success) return new List<Song>();

                return SongJsonMapper.ParseSongsList(
                    resp.Body,
                    id => _libraryService.HasLoadedLikedIds && _libraryService.IsSongLiked(id),
                    _qualityService.CachePrivilege);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetAlbumSongs Error: {ex.Message}");
                return new List<Song>();
            }
        }

        public async Task<ApiResult> CreatePlaylistAsync(string name)
        {
            try
            {
                var resp = await _transport.GetAsync($"/playlist/create?name={Uri.EscapeDataString(name)}").ConfigureAwait(false);
                if (!resp.Success) return resp.Result;

                if (AuthJsonMapper.TryParseCode200(resp.Body))
                {
                    UserPlaylistsChanged?.Invoke(this, EventArgs.Empty);
                    return new ApiResult(true, ApiErrorKind.None, string.Empty, 200, 200);
                }
                return new ApiResult(false, ApiErrorKind.Business, "创建歌单失败。");
            }
            catch (Exception ex)
            {
                return new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message);
            }
        }

        public async Task<ApiResult> DeletePlaylistAsync(string playlistId)
        {
            try
            {
                var resp = await _transport.GetAsync($"/playlist/delete?id={Uri.EscapeDataString(playlistId)}").ConfigureAwait(false);
                if (!resp.Success) return resp.Result;

                if (AuthJsonMapper.TryParseCode200(resp.Body))
                {
                    UserPlaylistsChanged?.Invoke(this, EventArgs.Empty);
                    return new ApiResult(true, ApiErrorKind.None, string.Empty, 200, 200);
                }
                return new ApiResult(false, ApiErrorKind.Business, "删除歌单失败。");
            }
            catch (Exception ex)
            {
                return new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message);
            }
        }

        public async Task<ApiResult> SetPlaylistSubscriptionAsync(string playlistId, bool subscribe)
        {
            try
            {
                var resp = await _transport.GetAsync(
                    $"/playlist/subscribe?t={(subscribe ? 1 : 2)}&id={Uri.EscapeDataString(playlistId)}").ConfigureAwait(false);
                if (!resp.Success) return resp.Result;

                if (AuthJsonMapper.TryParseCode200(resp.Body))
                {
                    UserPlaylistsChanged?.Invoke(this, EventArgs.Empty);
                    return new ApiResult(true, ApiErrorKind.None, string.Empty, 200, 200);
                }
                return new ApiResult(false, ApiErrorKind.Business, "操作失败。");
            }
            catch (Exception ex)
            {
                return new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message);
            }
        }
    }
}
