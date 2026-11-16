using System;
using System.Collections.Generic;
using System.Text.Json;
using music.Core.Models;

namespace music.Core.Netease.Mapping
{
    internal static class ArtistJsonMapper
    {
        public static ArtistDetailInfo? ParseArtistDetail(string json, string artistId)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            var detailResult = JsonSerializer.Deserialize<JsonElement>(json);
            if (detailResult.ValueKind != JsonValueKind.Object)
                throw new JsonException("响应不是有效的 JSON 对象。");

            if (detailResult.TryGetProperty("data", out var data) &&
                data.TryGetProperty("artist", out var artist))
            {
                var name = artist.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty;
                if (string.IsNullOrEmpty(name)) return null;

                var info = new ArtistDetailInfo
                {
                    Id = artistId,
                    Name = name,
                    CoverUrl = artist.TryGetProperty("cover", out var coverEl) ? coverEl.GetString() ?? string.Empty : string.Empty,
                    AvatarUrl = artist.TryGetProperty("avatar", out var avatarEl) ? avatarEl.GetString() ?? string.Empty : string.Empty,
                    PicUrl = artist.TryGetProperty("picUrl", out var picUrlEl) ? picUrlEl.GetString() ?? string.Empty : string.Empty,
                    BriefDesc = artist.TryGetProperty("briefDesc", out var descEl) ? descEl.GetString() ?? string.Empty : string.Empty
                };

                if (artist.TryGetProperty("alias", out var aliasEl) && aliasEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in aliasEl.EnumerateArray())
                    {
                        var text = a.GetString();
                        if (!string.IsNullOrEmpty(text)) info.Aliases.Add(text);
                    }
                }

                return info;
            }

            return null;
        }

        public static List<SearchArtistInfo> ParseSearchArtists(string json)
        {
            var artists = new List<SearchArtistInfo>();
            var result = JsonSerializer.Deserialize<JsonElement>(json);
            if (result.ValueKind != JsonValueKind.Object)
                throw new JsonException("响应不是有效的 JSON 对象。");

            if (result.TryGetProperty("result", out var searchResult) &&
                searchResult.TryGetProperty("artists", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    artists.Add(new SearchArtistInfo
                    {
                        Id = item.GetProperty("id").GetInt64().ToString(),
                        Name = item.GetProperty("name").GetString() ?? string.Empty,
                        PicUrl = item.TryGetProperty("picUrl", out var picUrl) ? picUrl.GetString() ?? string.Empty : string.Empty,
                        SongCount = item.TryGetProperty("musicSize", out var musicSize) ? musicSize.GetInt32() : 0
                    });
                }
            }
            return artists;
        }
    }
}
