using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using music.Core.Netease;
using music.Core.Netease.Mapping;
using music.Models;
using music.Services;

namespace music.Core.Services
{
    public interface ISearchService
    {
        Task<List<SearchSuggestion>> GetSearchSuggestionsAsync(string keywords, CancellationToken cancellationToken = default);
        Task<List<Song>> SearchSongsAsync(string keywords, int limit = 30, int offset = 0);
        Task<List<SearchArtistInfo>> SearchArtistsAsync(string keywords, int limit = 10);
        Task<List<RecommendedPlaylist>> SearchPlaylistsAsync(string keywords, int limit = 10);
        Task<List<SearchAlbumInfo>> SearchAlbumsAsync(string keywords, int limit = 10);
    }

    public sealed class SearchService : ISearchService
    {
        private readonly NeteaseHttpTransport _transport;
        private readonly ILibraryService _libraryService;
        private readonly IQualityService _qualityService;

        public SearchService(
            NeteaseHttpTransport transport,
            ILibraryService libraryService,
            IQualityService qualityService)
        {
            _transport = transport;
            _libraryService = libraryService;
            _qualityService = qualityService;
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
                System.Diagnostics.Debug.WriteLine($"[API] SearchSuggestions Error: {ex.Message}");
                return new List<SearchSuggestion>();
            }
        }

        public async Task<List<Song>> SearchSongsAsync(string keywords, int limit = 30, int offset = 0)
        {
            try
            {
                var resp = await _transport.GetAsync(
                    $"/cloudsearch?keywords={Uri.EscapeDataString(keywords)}&limit={limit}&offset={offset}").ConfigureAwait(false);
                if (!resp.Success) return new List<Song>();

                return SongJsonMapper.ParseSearchSongs(
                    resp.Body,
                    id => _libraryService.HasLoadedLikedIds && _libraryService.IsSongLiked(id),
                    _qualityService.CachePrivilege);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] Search Error: {ex.Message}");
                return new List<Song>();
            }
        }

        public async Task<List<SearchArtistInfo>> SearchArtistsAsync(string keywords, int limit = 10)
        {
            try
            {
                var resp = await _transport.GetAsync(
                    $"/cloudsearch?keywords={Uri.EscapeDataString(keywords)}&type=100&limit={limit}").ConfigureAwait(false);
                return resp.Success ? ArtistJsonMapper.ParseSearchArtists(resp.Body) : new List<SearchArtistInfo>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] SearchArtists Error: {ex.Message}");
                return new List<SearchArtistInfo>();
            }
        }

        public async Task<List<RecommendedPlaylist>> SearchPlaylistsAsync(string keywords, int limit = 10)
        {
            try
            {
                var resp = await _transport.GetAsync(
                    $"/cloudsearch?keywords={Uri.EscapeDataString(keywords)}&type=1000&limit={limit}").ConfigureAwait(false);
                return resp.Success ? PlaylistJsonMapper.ParseSearchPlaylists(resp.Body) : new List<RecommendedPlaylist>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] SearchPlaylists Error: {ex.Message}");
                return new List<RecommendedPlaylist>();
            }
        }

        public async Task<List<SearchAlbumInfo>> SearchAlbumsAsync(string keywords, int limit = 10)
        {
            try
            {
                var resp = await _transport.GetAsync(
                    $"/cloudsearch?keywords={Uri.EscapeDataString(keywords)}&type=10&limit={limit}").ConfigureAwait(false);
                return resp.Success ? AlbumJsonMapper.ParseSearchAlbums(resp.Body) : new List<SearchAlbumInfo>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] SearchAlbums Error: {ex.Message}");
                return new List<SearchAlbumInfo>();
            }
        }
    }
}
