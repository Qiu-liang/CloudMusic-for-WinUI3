using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using music.Core.Abstractions;
using music.Core.Models;
using music.Core.Netease;
using music.Core.Netease.Mapping;
using music.Core.Results;

namespace music.Core.Services
{
    public interface ILibraryService
    {
        bool HasLoadedLikedIds { get; }
        event EventHandler<SongLikeChangedEventArgs>? SongLikeChanged;

        bool IsSongLiked(string? songId);
        Task EnsureLikedSongIdsLoadedAsync(CancellationToken cancellationToken = default);
        Task<ApiResult<List<string>>> GetLikedSongIdsAsync(bool bypassCache = false, CancellationToken cancellationToken = default);
        Task<ApiResult<List<Song>>> GetLikedSongsAsync(int limit = 0, bool bypassCache = true, CancellationToken cancellationToken = default);
        Task<ApiResult> LikeSongAsync(string songId, bool like = true, CancellationToken cancellationToken = default);
        Task<bool> IsFavoritePlaylistAsync(string? playlistId, CancellationToken cancellationToken = default);
        Task<string> GetFavoritePlaylistIdAsync(bool bypassCache = false, CancellationToken cancellationToken = default);
        Task<ApiResult<List<Song>>> LoadPlaylistSongsAsync(string playlistId, bool bypassCache = true, CancellationToken cancellationToken = default);
        void ClearState();
    }

    public sealed class LibraryService : ILibraryService
    {
        private readonly NeteaseHttpTransport _transport;
        private readonly IQualityService _qualityService;
        private readonly ISessionService _sessionService;
        private readonly IApiLog _log;
        private readonly object _likedLock = new();
        private readonly HashSet<string> _likedSongIds = new(StringComparer.Ordinal);
        private bool _hasLoadedLikedIds;
        private string _favoritePlaylistId = string.Empty;

        public LibraryService(
            NeteaseHttpTransport transport,
            IQualityService qualityService,
            ISessionService sessionService,
            IApiLog? log = null)
        {
            _transport = transport;
            _qualityService = qualityService;
            _sessionService = sessionService;
            _log = log ?? NullApiLog.Instance;
            _sessionService.SessionCleared += (_, _) => ClearState();
            _sessionService.SessionChanged += (_, _) => ClearState();
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

        public async Task EnsureLikedSongIdsLoadedAsync(CancellationToken cancellationToken = default)
        {
            if (!_sessionService.IsLoggedIn) return;
            if (!HasLoadedLikedIds)
            {
                await GetLikedSongIdsAsync(bypassCache: true, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<ApiResult<List<string>>> GetLikedSongIdsAsync(bool bypassCache = false, CancellationToken cancellationToken = default)
        {
            if (!_sessionService.IsLoggedIn)
            {
                return ApiResult<List<string>>.Fail(
                    new ApiResult(false, ApiErrorKind.Authentication, "请先登录。"),
                    new List<string>());
            }

            try
            {
                var url = $"/likelist?uid={_sessionService.UserId}";
                if (bypassCache) url = AppendTimestamp(url);

                var response = await _transport.GetAsync(url, cancellationToken).ConfigureAwait(false);
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Write($"GetLikedSongIds Error: {ex}");
                return ApiResult<List<string>>.Fail(
                    new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message),
                    new List<string>());
            }
        }

        public async Task<ApiResult<List<Song>>> GetLikedSongsAsync(int limit = 0, bool bypassCache = true, CancellationToken cancellationToken = default)
        {
            try
            {
                var idsResult = await GetLikedSongIdsAsync(bypassCache, cancellationToken).ConfigureAwait(false);
                if (!idsResult.Success)
                {
                    return ApiResult<List<Song>>.Fail(idsResult.Status, new List<Song>());
                }

                var ids = idsResult.Value;
                if (limit <= 0)
                {
                    var favoritePlaylistId = await GetFavoritePlaylistIdAsync(bypassCache, cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(favoritePlaylistId))
                    {
                        var playlistSongsResult = await LoadPlaylistSongsAsync(favoritePlaylistId, bypassCache, cancellationToken).ConfigureAwait(false);
                        if (playlistSongsResult.Success && (playlistSongsResult.Value.Count > 0 || ids.Count == 0))
                        {
                            foreach (var song in playlistSongsResult.Value) song.Liked = true;
                            return playlistSongsResult;
                        }
                    }
                }

                if (ids.Count == 0) return ApiResult<List<Song>>.Ok(new List<Song>());
                if (limit > 0) ids = ids.Take(limit).ToList();

                var songsResult = await GetSongsByIdsInternalAsync(ids, cancellationToken).ConfigureAwait(false);
                if (!songsResult.Success) return songsResult;
                foreach (var song in songsResult.Value) song.Liked = true;
                return songsResult;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Write($"GetLikedSongs Error: {ex}");
                return ApiResult<List<Song>>.Fail(
                    new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message),
                    new List<Song>());
            }
        }

        public async Task<ApiResult> LikeSongAsync(string songId, bool like = true, CancellationToken cancellationToken = default)
        {
            try
            {
                var url = AppendTimestamp($"/like?id={Uri.EscapeDataString(songId)}&like={like.ToString().ToLowerInvariant()}");
                var response = await _transport.GetAsync(url, cancellationToken).ConfigureAwait(false);
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Write($"LikeSong Error: {ex}");
                return new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message);
            }
        }

        public async Task<bool> IsFavoritePlaylistAsync(string? playlistId, CancellationToken cancellationToken = default)
        {
            if (!_sessionService.IsLoggedIn || string.IsNullOrWhiteSpace(playlistId)) return false;
            var favoriteId = await GetFavoritePlaylistIdAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            return !string.IsNullOrEmpty(favoriteId) && string.Equals(favoriteId, playlistId, StringComparison.Ordinal);
        }

        public async Task<string> GetFavoritePlaylistIdAsync(bool bypassCache = false, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrEmpty(_favoritePlaylistId)) return _favoritePlaylistId;

            try
            {
                var url = $"/user/playlist?uid={_sessionService.UserId}";
                if (bypassCache) url = AppendTimestamp(url);
                var response = await _transport.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (response.Success)
                {
                    var found = PlaylistJsonMapper.FindFavoritePlaylistId(response.Body, _log);
                    if (!string.IsNullOrEmpty(found))
                        _favoritePlaylistId = found;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Write($"/user/playlist 获取喜欢歌单失败: {ex}");
            }

            return _favoritePlaylistId;
        }

        public async Task<ApiResult<List<Song>>> LoadPlaylistSongsAsync(
            string playlistId,
            bool bypassCache = true,
            CancellationToken cancellationToken = default)
        {
            var detailUrl = $"/playlist/detail?id={Uri.EscapeDataString(playlistId)}";
            if (bypassCache) detailUrl = AppendTimestamp(detailUrl);

            var detailResponse = await _transport.GetAsync(detailUrl, cancellationToken).ConfigureAwait(false);
            if (!detailResponse.Success)
            {
                return ApiResult<List<Song>>.Fail(detailResponse.Result, new List<Song>());
            }

            var trackIds = LikedMusicSyncHelper.ExtractPlaylistTrackIds(detailResponse.Body);
            if (trackIds.Count > 0)
            {
                return await GetSongsByIdsInternalAsync(trackIds, cancellationToken).ConfigureAwait(false);
            }

            if (PlaylistJsonMapper.IsTrackCountZero(detailResponse.Body))
            {
                return ApiResult<List<Song>>.Ok(new List<Song>());
            }

            var fallbackUrl = $"/playlist/track/all?id={Uri.EscapeDataString(playlistId)}";
            if (bypassCache) fallbackUrl = AppendTimestamp(fallbackUrl);

            var fallbackResponse = await _transport.GetAsync(fallbackUrl, cancellationToken).ConfigureAwait(false);
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

        internal async Task<ApiResult<List<Song>>> GetSongsByIdsInternalAsync(
            IReadOnlyList<string> ids,
            CancellationToken cancellationToken = default)
        {
            var orderedIds = ids
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (orderedIds.Count == 0) return ApiResult<List<Song>>.Ok(new List<Song>());

            var songsById = new Dictionary<string, Song>(StringComparer.Ordinal);
            foreach (var batch in ApiBatching.Chunk(orderedIds, 200))
            {
                var response = await _transport.GetAsync($"/song/detail?ids={string.Join(",", batch)}", cancellationToken).ConfigureAwait(false);
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
