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
    public interface IArtistService
    {
        Task<ArtistDetailInfo?> GetArtistDetailAsync(string artistId, CancellationToken cancellationToken = default);
        Task<List<Song>> GetArtistSongsAsync(string artistId, int limit = 50, CancellationToken cancellationToken = default);
        Task<List<SearchAlbumInfo>> GetArtistAlbumsAsync(string artistId, int limit = 50, CancellationToken cancellationToken = default);
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

        public async Task<ArtistDetailInfo?> GetArtistDetailAsync(string artistId, CancellationToken cancellationToken = default)
        {
            var resp = await _transport.GetAsync($"/artist/detail?id={Uri.EscapeDataString(artistId)}", cancellationToken).ConfigureAwait(false);
            return resp.Success ? ArtistJsonMapper.ParseArtistDetail(resp.Body, artistId) : null;
        }

        public async Task<List<Song>> GetArtistSongsAsync(string artistId, int limit = 50, CancellationToken cancellationToken = default)
        {
            try
            {
                var resp = await _transport.GetAsync($"/artist/songs?id={Uri.EscapeDataString(artistId)}&limit={limit}", cancellationToken).ConfigureAwait(false);
                if (!resp.Success) return new List<Song>();

                return SongJsonMapper.ParseSongsList(
                    resp.Body,
                    id => _libraryService.HasLoadedLikedIds && _libraryService.IsSongLiked(id),
                    _qualityService.CachePrivilege);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetArtistSongs Error: {ex.Message}");
                return new List<Song>();
            }
        }

        public async Task<List<SearchAlbumInfo>> GetArtistAlbumsAsync(string artistId, int limit = 50, CancellationToken cancellationToken = default)
        {
            var resp = await _transport.GetAsync($"/artist/album?id={Uri.EscapeDataString(artistId)}&limit={limit}", cancellationToken).ConfigureAwait(false);
            return resp.Success ? AlbumJsonMapper.ParseArtistAlbums(resp.Body) : new List<SearchAlbumInfo>();
        }
    }
}
