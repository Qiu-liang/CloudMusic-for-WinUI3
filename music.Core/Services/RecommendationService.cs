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
        Task<List<RecommendedPlaylist>> GetRecommendedPlaylistsAsync(int limit = 10);
        Task<List<RecommendedPlaylist>> GetRecommendResourceAsync();
        Task<List<Song>> GetPersonalizedSongsAsync(int limit = 10);
        Task<List<Song>> GetIntelligenceSongsAsync();
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
                return ApiResult<List<Song>>.Ok(new List<Song>());
            }
        }

        public async Task<List<RecommendedPlaylist>> GetRecommendedPlaylistsAsync(int limit = 10)
        {
            try
            {
                var resp = await _transport.GetAsync($"/personalized?limit={limit}").ConfigureAwait(false);
                return resp.Success ? PlaylistJsonMapper.ParseRecommendedPlaylists(resp.Body) : new List<RecommendedPlaylist>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetRecommendedPlaylists Error: {ex.Message}");
                return new List<RecommendedPlaylist>();
            }
        }

        public async Task<List<RecommendedPlaylist>> GetRecommendResourceAsync()
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
                    return new List<RecommendedPlaylist>();
                }

                var (needsLogin, items) = PlaylistJsonMapper.ParseRecommendResource(resp.Body);
                if (needsLogin)
                {
                    return await GetRecommendedPlaylistsAsync(10).ConfigureAwait(false);
                }

                return items;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetRecommendResource Error: {ex.Message}");
                return new List<RecommendedPlaylist>();
            }
        }

        public async Task<List<Song>> GetPersonalizedSongsAsync(int limit = 10)
        {
            try
            {
                var resp = await _transport.GetAsync($"/personalized/newsong?limit={limit}").ConfigureAwait(false);
                return resp.Success ? SongJsonMapper.ParsePersonalizedSongs(resp.Body) : new List<Song>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetPersonalizedSongs Error: {ex.Message}");
                return new List<Song>();
            }
        }

        public async Task<List<Song>> GetIntelligenceSongsAsync()
        {
            if (!_sessionService.IsLoggedIn)
            {
                return new List<Song>();
            }

            try
            {
                var likedIdsResult = await _libraryService.GetLikedSongIdsAsync().ConfigureAwait(false);
                var likedIds = likedIdsResult.Value;
                if (likedIds.Count == 0) return new List<Song>();

                var seed = likedIds[0];
                var pid = await _libraryService.GetFavoritePlaylistIdAsync().ConfigureAwait(false);
                if (string.IsNullOrEmpty(pid))
                {
                    return (await _libraryService.GetLikedSongsAsync(18).ConfigureAwait(false)).Value;
                }

                var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var resp = await _transport.GetAsync(
                    $"/playmode/intelligence/list?id={seed}&pid={pid}&sid={seed}&timestamp={stamp}").ConfigureAwait(false);

                var songs = resp.Success
                    ? SongJsonMapper.ParseIntelligenceSongs(
                        resp.Body,
                        id => _libraryService.HasLoadedLikedIds && _libraryService.IsSongLiked(id),
                        _qualityService.CachePrivilege)
                    : new List<Song>();

                if (songs.Count > 0) return songs;

                return (await _libraryService.GetLikedSongsAsync(18).ConfigureAwait(false)).Value;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetIntelligenceSongs Error: {ex.Message}");
                return (await _libraryService.GetLikedSongsAsync(18).ConfigureAwait(false)).Value;
            }
        }
    }
}
