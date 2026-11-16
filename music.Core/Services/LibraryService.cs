using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using music.Core.Netease;
using music.Core.Netease.Mapping;
using music.Core.Results;
using music.Models;
using music.Services;

namespace music.Core.Services
{
    public interface ILibraryService
    {
        bool HasLoadedLikedIds { get; }
        event EventHandler<SongLikeChangedEventArgs>? SongLikeChanged;

        bool IsSongLiked(string? songId);
        Task EnsureLikedSongIdsLoadedAsync();
        Task<ApiResult<List<string>>> GetLikedSongIdsAsync(bool bypassCache = false);
        Task<ApiResult<List<Song>>> GetLikedSongsAsync(int limit = 0, bool bypassCache = true);
        Task<ApiResult> LikeSongAsync(string songId, bool like = true);
        Task<bool> IsFavoritePlaylistAsync(string? playlistId);
        Task<string> GetFavoritePlaylistIdAsync(bool bypassCache = false);
        void ClearState();
    }

    public sealed class LibraryService : ILibraryService
    {
        private readonly NeteaseHttpTransport _transport;
        private readonly IQualityService _qualityService;
        private readonly Func<bool> _isLoggedIn;
        private readonly Func<long> _getUserId;
        private readonly object _likedLock = new();
        private readonly HashSet<string> _likedSongIds = new(StringComparer.Ordinal);
        private bool _hasLoadedLikedIds;
        private string _favoritePlaylistId = string.Empty;

        public LibraryService(
            NeteaseHttpTransport transport,
            IQualityService qualityService,
            Func<bool> isLoggedIn,
            Func<long> getUserId)
        {
            _transport = transport;
            _qualityService = qualityService;
            _isLoggedIn = isLoggedIn;
            _getUserId = getUserId;
        }

        public bool HasLoadedLikedIds
        {
            get
            {
                lock (_likedLock) return _hasLoadedLikedIds;
            }
        }

        public event EventHandler<SongLikeChangedEventArgs>? SongLikeChanged;

        public bool IsSongLiked(string? songId)
        {
            if (string.IsNullOrWhiteSpace(songId)) return false;
            lock (_likedLock)
            {
                return _likedSongIds.Contains(songId);
            }
        }

        public void ClearState()
        {
            lock (_likedLock)
            {
                _likedSongIds.Clear();
                _hasLoadedLikedIds = false;
            }
            _favoritePlaylistId = string.Empty;
        }

        public async Task EnsureLikedSongIdsLoadedAsync()
        {
            if (!_isLoggedIn()) return;
            if (!HasLoadedLikedIds)
            {
                await GetLikedSongIdsAsync(bypassCache: true).ConfigureAwait(false);
            }
        }

        public async Task<ApiResult<List<string>>> GetLikedSongIdsAsync(bool bypassCache = false)
        {
            if (!_isLoggedIn())
            {
                return ApiResult<List<string>>.Fail(
                    new ApiResult(false, ApiErrorKind.Authentication, "请先登录。"),
                    new List<string>());
            }

            try
            {
                var url = $"/likelist?uid={_getUserId()}";
                if (bypassCache) url = AppendTimestamp(url);

                var response = await _transport.GetAsync(url).ConfigureAwait(false);
                if (!response.Success)
                {
                    return ApiResult<List<string>>.Fail(response.Result, new List<string>());
                }

                var ids = LikedMusicSyncHelper.ExtractLikedIds(response.Body);
                lock (_likedLock)
                {
                    _likedSongIds.Clear();
                    foreach (var id in ids) _likedSongIds.Add(id);
                    _hasLoadedLikedIds = true;
                }
                return ApiResult<List<string>>.Ok(ids);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetLikedSongIds Error: {ex.Message}");
                return ApiResult<List<string>>.Fail(
                    new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message),
                    new List<string>());
            }
        }

        public async Task<ApiResult<List<Song>>> GetLikedSongsAsync(int limit = 0, bool bypassCache = true)
        {
            try
            {
                var idsResult = await GetLikedSongIdsAsync(bypassCache).ConfigureAwait(false);
                if (!idsResult.Success)
                {
                    return ApiResult<List<Song>>.Fail(idsResult.Status, new List<Song>());
                }

                var ids = idsResult.Value;
                if (limit <= 0)
                {
                    var favoritePlaylistId = await GetFavoritePlaylistIdAsync(bypassCache).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(favoritePlaylistId))
                    {
                        var playlistSongsResult = await LoadPlaylistSongsInternalAsync(favoritePlaylistId, bypassCache).ConfigureAwait(false);
                        if (playlistSongsResult.Success && (playlistSongsResult.Value.Count > 0 || ids.Count == 0))
                        {
                            foreach (var song in playlistSongsResult.Value) song.Liked = true;
                            return playlistSongsResult;
                        }
                    }
                }

                if (ids.Count == 0) return ApiResult<List<Song>>.Ok(new List<Song>());
                if (limit > 0) ids = ids.Take(limit).ToList();

                var songsResult = await GetSongsByIdsInternalAsync(ids).ConfigureAwait(false);
                foreach (var song in songsResult.Value) song.Liked = true;
                return songsResult;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetLikedSongs Error: {ex.Message}");
                return ApiResult<List<Song>>.Ok(new List<Song>());
            }
        }

        public async Task<ApiResult> LikeSongAsync(string songId, bool like = true)
        {
            try
            {
                var url = AppendTimestamp($"/like?id={Uri.EscapeDataString(songId)}&like={like.ToString().ToLowerInvariant()}");
                var response = await _transport.GetAsync(url).ConfigureAwait(false);
                if (!response.Success)
                {
                    return response.Result;
                }

                if (AuthJsonMapper.TryParseCode200(response.Body))
                {
                    lock (_likedLock)
                    {
                        LikedMusicSyncHelper.ApplyLikeChange(_likedSongIds, songId, like);
                    }
                    SongLikeChanged?.Invoke(this, new SongLikeChangedEventArgs(songId, like));
                    return new ApiResult(true, ApiErrorKind.None, string.Empty, 200, 200);
                }

                return new ApiResult(false, ApiErrorKind.Business, "操作未完成，请重试。");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] LikeSong Error: {ex.Message}");
                return new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message);
            }
        }

        public async Task<bool> IsFavoritePlaylistAsync(string? playlistId)
        {
            if (!_isLoggedIn() || string.IsNullOrWhiteSpace(playlistId)) return false;
            var favoriteId = await GetFavoritePlaylistIdAsync().ConfigureAwait(false);
            return !string.IsNullOrEmpty(favoriteId) && string.Equals(favoriteId, playlistId, StringComparison.Ordinal);
        }

        public async Task<string> GetFavoritePlaylistIdAsync(bool bypassCache = false)
        {
            if (!string.IsNullOrEmpty(_favoritePlaylistId)) return _favoritePlaylistId;

            try
            {
                var url = $"/user/playlist?uid={_getUserId()}";
                if (bypassCache) url = AppendTimestamp(url);
                var response = await _transport.GetAsync(url).ConfigureAwait(false);
                if (response.Success)
                {
                    var found = PlaylistJsonMapper.FindFavoritePlaylistId(response.Body);
                    if (!string.IsNullOrEmpty(found))
                        _favoritePlaylistId = found;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"/user/playlist 获取喜欢歌单失败: {ex.Message}");
            }

            return _favoritePlaylistId;
        }

        internal async Task<ApiResult<List<Song>>> LoadPlaylistSongsInternalAsync(string playlistId, bool bypassCache)
        {
            var detailUrl = $"/playlist/detail?id={Uri.EscapeDataString(playlistId)}";
            if (bypassCache) detailUrl = AppendTimestamp(detailUrl);

            var detailResponse = await _transport.GetAsync(detailUrl).ConfigureAwait(false);
            if (!detailResponse.Success)
            {
                return ApiResult<List<Song>>.Fail(detailResponse.Result, new List<Song>());
            }

            var trackIds = LikedMusicSyncHelper.ExtractPlaylistTrackIds(detailResponse.Body);
            if (trackIds.Count > 0)
            {
                return await GetSongsByIdsInternalAsync(trackIds).ConfigureAwait(false);
            }

            if (PlaylistJsonMapper.IsTrackCountZero(detailResponse.Body))
            {
                return ApiResult<List<Song>>.Ok(new List<Song>());
            }

            var fallbackUrl = $"/playlist/track/all?id={Uri.EscapeDataString(playlistId)}";
            if (bypassCache) fallbackUrl = AppendTimestamp(fallbackUrl);

            var fallbackResponse = await _transport.GetAsync(fallbackUrl).ConfigureAwait(false);
            if (!fallbackResponse.Success)
            {
                return ApiResult<List<Song>>.Fail(fallbackResponse.Result, new List<Song>());
            }

            var songs = SongJsonMapper.ParseSongsList(
                fallbackResponse.Body,
                id => HasLoadedLikedIds && IsSongLiked(id),
                _qualityService.CachePrivilege);
            return ApiResult<List<Song>>.Ok(songs);
        }

        internal async Task<ApiResult<List<Song>>> GetSongsByIdsInternalAsync(IReadOnlyList<string> ids)
        {
            var orderedIds = ids
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (orderedIds.Count == 0) return ApiResult<List<Song>>.Ok(new List<Song>());

            var songsById = new Dictionary<string, Song>(StringComparer.Ordinal);
            foreach (var batch in ApiBatching.Chunk(orderedIds, 200))
            {
                var response = await _transport.GetAsync($"/song/detail?ids={string.Join(",", batch)}").ConfigureAwait(false);
                if (!response.Success)
                {
                    return ApiResult<List<Song>>.Fail(response.Result, new List<Song>());
                }

                foreach (var song in SongJsonMapper.ParseSongsList(
                    response.Body,
                    id => HasLoadedLikedIds && IsSongLiked(id),
                    _qualityService.CachePrivilege))
                {
                    if (!string.IsNullOrEmpty(song.Id))
                        songsById[song.Id] = song;
                }
            }

            var result = orderedIds.Where(songsById.ContainsKey).Select(id => songsById[id]).ToList();
            return ApiResult<List<Song>>.Ok(result);
        }

        internal static string AppendTimestamp(string url) =>
            LikedMusicSyncHelper.AppendTimestamp(url, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }
}
