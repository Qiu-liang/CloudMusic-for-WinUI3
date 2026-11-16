using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using music.Core.Netease;
using music.Core.Netease.Mapping;
using music.Core.Results;
using music.Models;
using music.Services;

namespace music.Core.Services
{
    public interface IArtistService
    {
        Task<ApiResult<ArtistDetailInfo?>> GetArtistDetailAsync(string artistId, CancellationToken cancellationToken = default);
        Task<ApiResult<List<Song>>> GetArtistSongsAsync(string artistId, int limit = 50, CancellationToken cancellationToken = default);
        Task<ApiResult<List<SearchAlbumInfo>>> GetArtistAlbumsAsync(string artistId, int limit = 50, CancellationToken cancellationToken = default);
    }

    public sealed class ArtistService : IArtistService
    {
        private readonly NeteaseHttpTransport _transport;
        private readonly ILibraryService _libraryService;
        private readonly IQualityService _qualityService;

        public ArtistService(
            NeteaseHttpTransport transport,
            ILibraryService libraryService,
            IQualityService qualityService)
        {
            _transport = transport;
            _libraryService = libraryService;
            _qualityService = qualityService;
        }

        public async Task<ApiResult<ArtistDetailInfo?>> GetArtistDetailAsync(string artistId, CancellationToken cancellationToken = default)
        {
            try
            {
                var resp = await _transport.GetAsync($"/artist/detail?id={Uri.EscapeDataString(artistId)}", cancellationToken).ConfigureAwait(false);
                if (!resp.Success)
                {
                    return ApiResult<ArtistDetailInfo?>.Fail(resp.Result, null);
                }

                return ApiResult<ArtistDetailInfo?>.Ok(ArtistJsonMapper.ParseArtistDetail(resp.Body, artistId));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetArtistDetail Error: {ex.Message}");
                return ApiResult<ArtistDetailInfo?>.Fail(
                    new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message),
                    null);
            }
        }

        public async Task<ApiResult<List<Song>>> GetArtistSongsAsync(string artistId, int limit = 50, CancellationToken cancellationToken = default)
        {
            try
            {
                var resp = await _transport.GetAsync($"/artist/songs?id={Uri.EscapeDataString(artistId)}&limit={limit}", cancellationToken).ConfigureAwait(false);
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
                System.Diagnostics.Debug.WriteLine($"[API] GetArtistSongs Error: {ex.Message}");
                return ApiResult<List<Song>>.Fail(
                    new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message),
                    new List<Song>());
            }
        }

        public async Task<ApiResult<List<SearchAlbumInfo>>> GetArtistAlbumsAsync(string artistId, int limit = 50, CancellationToken cancellationToken = default)
        {
            try
            {
                var resp = await _transport.GetAsync($"/artist/album?id={Uri.EscapeDataString(artistId)}&limit={limit}", cancellationToken).ConfigureAwait(false);
                if (!resp.Success)
                {
                    return ApiResult<List<SearchAlbumInfo>>.Fail(resp.Result, new List<SearchAlbumInfo>());
                }

                return ApiResult<List<SearchAlbumInfo>>.Ok(AlbumJsonMapper.ParseArtistAlbums(resp.Body));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetArtistAlbums Error: {ex.Message}");
                return ApiResult<List<SearchAlbumInfo>>.Fail(
                    new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message),
                    new List<SearchAlbumInfo>());
            }
        }
    }
}
