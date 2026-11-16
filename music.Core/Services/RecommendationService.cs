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
    public interface IRecommendationService
    {
        Task<ApiResult<List<Song>>> GetDailyRecommendSongsAsync(int limit = 30, CancellationToken cancellationToken = default);
        Task<ApiResult<List<RecommendedPlaylist>>> GetRecommendedPlaylistsAsync(int limit = 10, CancellationToken cancellationToken = default);
        Task<ApiResult<List<RecommendedPlaylist>>> GetRecommendResourceAsync(CancellationToken cancellationToken = default);
        Task<ApiResult<List<Song>>> GetPersonalizedSongsAsync(int limit = 10, CancellationToken cancellationToken = default);
        Task<ApiResult<List<Song>>> GetIntelligenceSongsAsync(CancellationToken cancellationToken = default);
    }

    public sealed class RecommendationService : IRecommendationService
    {
        private readonly NeteaseHttpTransport _transport;
        private readonly ISessionService _sessionService;
        private readonly ILibraryService _libraryService;
        private readonly IQualityService _qualityService;
        private readonly SessionCookieJar _cookieJar;
        private readonly IApiLog _log;

        public RecommendationService(
            NeteaseHttpTransport transport,
            ISessionService sessionService,
            ILibraryService libraryService,
            IQualityService qualityService,
            SessionCookieJar cookieJar,
            IApiLog? log = null)
        {
            _transport = transport;
            _sessionService = sessionService;
            _libraryService = libraryService;
            _qualityService = qualityService;
            _cookieJar = cookieJar;
            _log = log ?? NullApiLog.Instance;
        }

        public async Task<ApiResult<List<Song>>> GetDailyRecommendSongsAsync(int limit = 30, CancellationToken cancellationToken = default)
        {
            if (!_cookieJar.IsInitialized)
            {
                await _sessionService.InitializeAsync(cancellationToken).ConfigureAwait(false);
            }

            try
            {
                var resp = await _transport.GetAsync($"/recommend/songs?limit={Math.Max(0, limit)}", cancellationToken).ConfigureAwait(false);
                if (!resp.Success)
                {
                    return ApiResult<List<Song>>.Fail(resp.Result, new List<Song>());
                }

                var songs = SongJsonMapper.ParseDailyRecommendSongs(
                    resp.Body,
                    limit,
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
                _log.Write($"GetDailyRecommendSongs Error: {ex}");
                return ApiResult<List<Song>>.Fail(
                    new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message),
                    new List<Song>());
            }
        }

        public async Task<ApiResult<List<RecommendedPlaylist>>> GetRecommendedPlaylistsAsync(int limit = 10, CancellationToken cancellationToken = default)
        {
            try
            {
                var resp = await _transport.GetAsync($"/personalized?limit={limit}", cancellationToken).ConfigureAwait(false);
                if (!resp.Success)
                {
                    return ApiResult<List<RecommendedPlaylist>>.Fail(resp.Result, new List<RecommendedPlaylist>());
                }

                return ApiResult<List<RecommendedPlaylist>>.Ok(PlaylistJsonMapper.ParseRecommendedPlaylists(resp.Body));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Write($"GetRecommendedPlaylists Error: {ex}");
                return ApiResult<List<RecommendedPlaylist>>.Fail(
                    new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message),
                    new List<RecommendedPlaylist>());
            }
        }

        public async Task<ApiResult<List<RecommendedPlaylist>>> GetRecommendResourceAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (!_sessionService.IsLoggedIn)
                {
                    return await GetRecommendedPlaylistsAsync(10, cancellationToken).ConfigureAwait(false);
                }

                var resp = await _transport.GetAsync("/recommend/resource", cancellationToken).ConfigureAwait(false);
                if (!resp.Success)
                {
                    if (resp.Result.BusinessCode == 301 || resp.Result.Error == ApiErrorKind.Authentication)
                    {
                        return await GetRecommendedPlaylistsAsync(10, cancellationToken).ConfigureAwait(false);
                    }
                    return ApiResult<List<RecommendedPlaylist>>.Fail(resp.Result, new List<RecommendedPlaylist>());
                }

                var (needsLogin, items) = PlaylistJsonMapper.ParseRecommendResource(resp.Body);
                if (needsLogin)
                {
                    return await GetRecommendedPlaylistsAsync(10, cancellationToken).ConfigureAwait(false);
                }

                return ApiResult<List<RecommendedPlaylist>>.Ok(items);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Write($"GetRecommendResource Error: {ex}");
                return ApiResult<List<RecommendedPlaylist>>.Fail(
                    new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message),
                    new List<RecommendedPlaylist>());
            }
        }

        public async Task<ApiResult<List<Song>>> GetPersonalizedSongsAsync(int limit = 10, CancellationToken cancellationToken = default)
        {
            try
            {
                var resp = await _transport.GetAsync($"/personalized/newsong?limit={limit}", cancellationToken).ConfigureAwait(false);
                if (!resp.Success)
                {
                    return ApiResult<List<Song>>.Fail(resp.Result, new List<Song>());
                }

                return ApiResult<List<Song>>.Ok(SongJsonMapper.ParsePersonalizedSongs(resp.Body));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Write($"GetPersonalizedSongs Error: {ex}");
                return ApiResult<List<Song>>.Fail(
                    new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message),
                    new List<Song>());
            }
        }

        public async Task<ApiResult<List<Song>>> GetIntelligenceSongsAsync(CancellationToken cancellationToken = default)
        {
            if (!_sessionService.IsLoggedIn)
            {
                return ApiResult<List<Song>>.Fail(
                    new ApiResult(false, ApiErrorKind.Authentication, "请先登录。"),
                    new List<Song>());
            }

            try
            {
                var likedIdsResult = await _libraryService.GetLikedSongIdsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!likedIdsResult.Success)
                {
                    return ApiResult<List<Song>>.Fail(likedIdsResult.Status, new List<Song>());
                }

                var likedIds = likedIdsResult.Value;
                if (likedIds.Count == 0) return ApiResult<List<Song>>.Ok(new List<Song>());

                var seed = likedIds[0];
                var pid = await _libraryService.GetFavoritePlaylistIdAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrEmpty(pid))
                {
                    return await _libraryService.GetLikedSongsAsync(18, cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var resp = await _transport.GetAsync(
                    $"/playmode/intelligence/list?id={seed}&pid={pid}&sid={seed}&timestamp={stamp}",
                    cancellationToken).ConfigureAwait(false);

                if (!resp.Success)
                {
                    return ApiResult<List<Song>>.Fail(resp.Result, new List<Song>());
                }

                var songs = SongJsonMapper.ParseIntelligenceSongs(
                    resp.Body,
                    id => _libraryService.HasLoadedLikedIds && _libraryService.IsSongLiked(id),
                    _qualityService.CachePrivilege,
                    _log);

                if (songs.Count > 0) return ApiResult<List<Song>>.Ok(songs);

                return await _libraryService.GetLikedSongsAsync(18, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Write($"GetIntelligenceSongs Error: {ex}");
                return ApiResult<List<Song>>.Fail(
                    new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message),
                    new List<Song>());
            }
        }
    }
}
