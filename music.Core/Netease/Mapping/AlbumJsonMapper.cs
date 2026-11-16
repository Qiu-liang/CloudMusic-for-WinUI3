using System;
using System.Collections.Generic;
using System.Text.Json;
using music.Services;

namespace music.Core.Netease.Mapping
{
    internal static class AlbumJsonMapper
    {
        public static AlbumDetailInfo? ParseAlbumInfo(string json)
        {
            try
            {
                var result = JsonSerializer.Deserialize<JsonElement>(json);
                if (result.TryGetProperty("album", out var album))
                {
                    var info = new AlbumDetailInfo
                    {
                        Id = album.GetProperty("id").GetInt64().ToString(),
                        Name = album.GetProperty("name").GetString() ?? string.Empty,
                        PicUrl = album.TryGetProperty("picUrl", out var picUrl) ? picUrl.GetString() ?? string.Empty : string.Empty,
                        Description = album.TryGetProperty("description", out var desc) ? desc.GetString() ?? string.Empty : string.Empty,
                        Size = album.TryGetProperty("size", out var size) ? size.GetInt32() : 0,
                        SubType = album.TryGetProperty("subType", out var subType) ? subType.GetString() ?? string.Empty : string.Empty,
                        Company = album.TryGetProperty("company", out var company) ? company.GetString() ?? string.Empty : string.Empty
                    };

                    if (album.TryGetProperty("artist", out var artist))
                    {
                        info.ArtistName = artist.TryGetProperty("name", out var artistName) ? artistName.GetString() ?? string.Empty : string.Empty;
                        info.ArtistId = artist.TryGetProperty("id", out var artistId) ? artistId.GetInt64().ToString() : string.Empty;
                    }

                    if (album.TryGetProperty("publishTime", out var publishTime))
                    {
                        var timestamp = publishTime.GetInt64();
                        if (timestamp > 0)
                        {
                            var dateTime = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).LocalDateTime;
                            info.PublishTime = dateTime.ToString("yyyy-MM-dd");
                        }
                    }

                    return info;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetAlbumInfo Error: {ex.Message}");
            }
            return null;
        }

        public static List<SearchAlbumInfo> ParseSearchAlbums(string json)
        {
            var albums = new List<SearchAlbumInfo>();
            try
            {
                var result = JsonSerializer.Deserialize<JsonElement>(json);
                if (result.TryGetProperty("result", out var searchResult) &&
                    searchResult.TryGetProperty("albums", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        var album = new SearchAlbumInfo
                        {
                            Id = item.GetProperty("id").GetInt64().ToString(),
                            Name = item.GetProperty("name").GetString() ?? string.Empty,
                            PicUrl = item.TryGetProperty("picUrl", out var picUrl) ? picUrl.GetString() ?? string.Empty : string.Empty,
                            Size = item.TryGetProperty("size", out var size) ? size.GetInt32() : 0
                        };

                        if (item.TryGetProperty("artist", out var artist))
                        {
                            album.ArtistName = artist.TryGetProperty("name", out var artistName) ? artistName.GetString() ?? string.Empty : string.Empty;
                        }

                        if (item.TryGetProperty("publishTime", out var publishTime))
                        {
                            var timestamp = publishTime.GetInt64();
                            if (timestamp > 0)
                            {
                                var dateTime = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).LocalDateTime;
                                album.PublishTime = dateTime.ToString("yyyy-MM-dd");
                            }
                        }

                        albums.Add(album);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] SearchAlbums Error: {ex.Message}");
            }
            return albums;
        }

        public static List<SearchAlbumInfo> ParseArtistAlbums(string json)
        {
            var albums = new List<SearchAlbumInfo>();
            try
            {
                var result = JsonSerializer.Deserialize<JsonElement>(json);
                if (result.TryGetProperty("hotAlbums", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        albums.Add(new SearchAlbumInfo
                        {
                            Id = item.GetProperty("id").GetInt64().ToString(),
                            Name = item.GetProperty("name").GetString() ?? string.Empty,
                            PicUrl = item.TryGetProperty("picUrl", out var picUrl) ? picUrl.GetString() ?? string.Empty : string.Empty,
                            Size = item.TryGetProperty("size", out var size) ? size.GetInt32() : 0
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ArtistAlbums] Error: {ex.Message}");
            }
            return albums;
        }
    }
}
