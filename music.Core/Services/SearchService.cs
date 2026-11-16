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
    public interface ISearchService
    {
        Task<List<SearchSuggestion>> GetSearchSuggestionsAsync(string keywords, CancellationToken cancellationToken = default);
        Task<ApiResult<List<Song>>> SearchSongsAsync(string keywords, int limit = 30, int offset = 0, CancellationToken cancellationToken = default);
        Task<ApiResult<List<SearchArtistInfo>>> SearchArtistsAsync(string keywords, int limit = 10, CancellationToken cancellationToken = default);
        Task<ApiResult<List<RecommendedPlaylist>>> SearchPlaylistsAsync(string keywords, int limit = 10, CancellationToken cancellationToken = default);
        Task<ApiResult<List<SearchAlbumInfo>>> SearchAlbumsAsync(string keywords, int limit = 10, CancellationToken cancellationToken = default);
    }

    public sealed class SearchService : ISearchService
    {
        private readonly NeteaseHttpTransport _transport;
        private readonly ILibraryService _libraryService;
        private readonly IQualityService _qualityService;
        private readonly IApiLog _log;

        public SearchService(
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

        public async Task<List<SearchSuggestion>> GetSearchSuggestionsAsync(string keywords, CancellationToken cancellationToken = default)
        {
            try
            {
                var resp = await _transport.GetAsync(
                    $"/search/suggest?keywords={Uri.EscapeDataString(keywords)}",
                    cancellationToken).ConfigureAwait(false);
                return resp.Success ? SearchJsonMapper.ParseSuggestions(resp.Body) : new List<SearchSuggestion>();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Write($"SearchSuggestions Error: {ex}");
                return new List<SearchSuggestion>();
            }
        }

        public async Task<ApiResult<List<Song>>> SearchSongsAsync(
            string keywords,
            int limit = 30,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var resp = await _transport.GetAsync(
                    $"/cloudsearch?keywords={Uri.EscapeDataString(keywords)}&limit={limit}&offset={offset}",
                    cancellationToken).ConfigureAwait(false);
                if (!resp.Success)
                {
                    return ApiResult<List<Song>>.Fail(resp.Result, new List<Song>());
                }

                var songs = SongJsonMapper.ParseSearchSongs(
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
                _log.Write($"SearchSongs Error: {ex}");
                return ApiResult<List<Song>>.Fail(
                    new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message),
                    new List<Song>());
            }
        }

        public async Task<ApiResult<List<SearchArtistInfo>>> SearchArtistsAsync(
            string keywords,
            int limit = 10,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var resp = await _transport.GetAsync(
                    $"/cloudsearch?keywords={Uri.EscapeDataString(keywords)}&type=100&limit={limit}",
                    cancellationToken).ConfigureAwait(false);
                if (!resp.Success)
                {
                    return ApiResult<List<SearchArtistInfo>>.Fail(resp.Result, new List<SearchArtistInfo>());
                }
                return ApiResult<List<SearchArtistInfo>>.Ok(ArtistJsonMapper.ParseSearchArtists(resp.Body));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Write($"SearchArtists Error: {ex}");
                return ApiResult<List<SearchArtistInfo>>.Fail(
                    new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message),
                    new List<SearchArtistInfo>());
            }
        }

        public async Task<ApiResult<List<RecommendedPlaylist>>> SearchPlaylistsAsync(
            string keywords,
            int limit = 10,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var resp = await _transport.GetAsync(
                    $"/cloudsearch?keywords={Uri.EscapeDataString(keywords)}&type=1000&limit={limit}",
                    cancellationToken).ConfigureAwait(false);
                if (!resp.Success)
                {
                    return ApiResult<List<RecommendedPlaylist>>.Fail(resp.Result, new List<RecommendedPlaylist>());
                }
                return ApiResult<List<RecommendedPlaylist>>.Ok(PlaylistJsonMapper.ParseSearchPlaylists(resp.Body));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Write($"SearchPlaylists Error: {ex}");
                return ApiResult<List<RecommendedPlaylist>>.Fail(
                    new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message),
                    new List<RecommendedPlaylist>());
            }
        }

        public async Task<ApiResult<List<SearchAlbumInfo>>> SearchAlbumsAsync(
            string keywords,
            int limit = 10,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var resp = await _transport.GetAsync(
                    $"/cloudsearch?keywords={Uri.EscapeDataString(keywords)}&type=10&limit={limit}",
                    cancellationToken).ConfigureAwait(false);
                if (!resp.Success)
                {
                    return ApiResult<List<SearchAlbumInfo>>.Fail(resp.Result, new List<SearchAlbumInfo>());
                }
                return ApiResult<List<SearchAlbumInfo>>.Ok(AlbumJsonMapper.ParseSearchAlbums(resp.Body));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Write($"SearchAlbums Error: {ex}");
                return ApiResult<List<SearchAlbumInfo>>.Fail(
                    new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message),
                    new List<SearchAlbumInfo>());
            }
        }
    }
}
