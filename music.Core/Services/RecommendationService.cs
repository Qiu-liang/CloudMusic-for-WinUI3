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
    public interface IRecommendationService
    {
        Task<ApiResult<List<Song>>> GetDailyRecommendSongsAsync(int limit = 30);
        Task<ApiResult<List<RecommendedPlaylist>>> GetRecommendedPlaylistsAsync(int limit = 10);
        Task<ApiResult<List<RecommendedPlaylist>>> GetRecommendResourceAsync();
        Task<ApiResult<List<Song>>> GetPersonalizedSongsAsync(int limit = 10);
        Task<ApiResult<List<Song>>> GetIntelligenceSongsAsync();
    }

    public sealed class RecommendationService : IRecommendationService
    {
        private readonly NeteaseHttpTransport _transport;
        private readonly ISessionService _sessionService;
        private readonly ILibraryService _libraryService;
        private readonly IQualityService _qualityService;
        private readonly SessionCookieJar _cookieJar;

        public RecommendationService(
            NeteaseHttpTransport transport,
            ISessionService sessionService,
            ILibraryService libraryService,
            IQualityService qualityService,
            SessionCookieJar cookieJar)
        {
            _transport = transport;
            _sessionService = sessionService;
            _libraryService = libraryService;
            _qualityService = qualityService;
            _cookieJar = cookieJar;
        }

        public async Task<ApiResult<List<Song>>> GetDailyRecommendSongsAsync(int limit = 30)
        {
            if (!_cookieJar.IsInitialized)
            {
                await _sessionService.InitializeAsync().ConfigureAwait(false);
            }

            try
            {
                var resp = await _transport.GetAsync($"/recommend/songs?limit={Math.Max(0, limit)}").ConfigureAwait(false);
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] Recommend Error: {ex.Message}");
                return ApiResult<List<Song>>.Fail(
                    new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message),
                    new List<Song>());
            }
        }

        public async Task<ApiResult<List<RecommendedPlaylist>>> GetRecommendedPlaylistsAsync(int limit = 10)
        {
            try
            {
                var resp = await _transport.GetAsync($"/personalized?limit={limit}").ConfigureAwait(false);
                if (!resp.Success)
                {
                    return ApiResult<List<RecommendedPlaylist>>.Fail(resp.Result, new List<RecommendedPlaylist>());
                }

                return ApiResult<List<RecommendedPlaylist>>.Ok(PlaylistJsonMapper.ParseRecommendedPlaylists(resp.Body));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetRecommendedPlaylists Error: {ex.Message}");
                return ApiResult<List<RecommendedPlaylist>>.Fail(
                    new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message),
                    new List<RecommendedPlaylist>());
            }
        }

        public async Task<ApiResult<List<RecommendedPlaylist>>> GetRecommendResourceAsync()
        {
            try
            {
                if (!_sessionService.IsLoggedIn)
                {
                    return await GetRecommendedPlaylistsAsync(10).ConfigureAwait(false);
                }

                var resp = await _transport.GetAsync("/recommend/resource").ConfigureAwait(false);
                if (!resp.Success)
                {
                    if (resp.Result.BusinessCode == 301 || resp.Result.Error == ApiErrorKind.Authentication)
                    {
                        return await GetRecommendedPlaylistsAsync(10).ConfigureAwait(false);
                    }
                    return ApiResult<List<RecommendedPlaylist>>.Fail(resp.Result, new List<RecommendedPlaylist>());
                }

                var (needsLogin, items) = PlaylistJsonMapper.ParseRecommendResource(resp.Body);
                if (needsLogin)
                {
                    return await GetRecommendedPlaylistsAsync(10).ConfigureAwait(false);
                }

                return ApiResult<List<RecommendedPlaylist>>.Ok(items);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetRecommendResource Error: {ex.Message}");
                return ApiResult<List<RecommendedPlaylist>>.Fail(
                    new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message),
                    new List<RecommendedPlaylist>());
            }
        }

        public async Task<ApiResult<List<Song>>> GetPersonalizedSongsAsync(int limit = 10)
        {
            try
            {
                var resp = await _transport.GetAsync($"/personalized/newsong?limit={limit}").ConfigureAwait(false);
                if (!resp.Success)
                {
                    return ApiResult<List<Song>>.Fail(resp.Result, new List<Song>());
                }

                return ApiResult<List<Song>>.Ok(SongJsonMapper.ParsePersonalizedSongs(resp.Body));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetPersonalizedSongs Error: {ex.Message}");
                return ApiResult<List<Song>>.Fail(
                    new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message),
                    new List<Song>());
            }
        }

        public async Task<ApiResult<List<Song>>> GetIntelligenceSongsAsync()
        {
            if (!_sessionService.IsLoggedIn)
            {
                return ApiResult<List<Song>>.Fail(
                    new ApiResult(false, ApiErrorKind.Authentication, "请先登录。"),
                    new List<Song>());
            }

            try
            {
                var likedIdsResult = await _libraryService.GetLikedSongIdsAsync().ConfigureAwait(false);
                if (!likedIdsResult.Success)
                {
                    return ApiResult<List<Song>>.Fail(likedIdsResult.Status, new List<Song>());
                }

                var likedIds = likedIdsResult.Value;
                if (likedIds.Count == 0) return ApiResult<List<Song>>.Ok(new List<Song>());

                var seed = likedIds[0];
                var pid = await _libraryService.GetFavoritePlaylistIdAsync().ConfigureAwait(false);
                if (string.IsNullOrEmpty(pid))
                {
                    return await _libraryService.GetLikedSongsAsync(18).ConfigureAwait(false);
                }

                var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var resp = await _transport.GetAsync(
                    $"/playmode/intelligence/list?id={seed}&pid={pid}&sid={seed}&timestamp={stamp}").ConfigureAwait(false);

                if (!resp.Success)
                {
                    return ApiResult<List<Song>>.Fail(resp.Result, new List<Song>());
                }

                var songs = SongJsonMapper.ParseIntelligenceSongs(
                    resp.Body,
                    id => _libraryService.HasLoadedLikedIds && _libraryService.IsSongLiked(id),
                    _qualityService.CachePrivilege);

                if (songs.Count > 0) return ApiResult<List<Song>>.Ok(songs);

                return await _libraryService.GetLikedSongsAsync(18).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetIntelligenceSongs Error: {ex.Message}");
                return ApiResult<List<Song>>.Fail(
                    new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message),
                    new List<Song>());
            }
        }
    }
}
