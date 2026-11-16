using System;
using System.Collections.Generic;
using System.Text.Json;
using music.Services;

namespace music.Core.Netease.Mapping
{
    internal static class SearchJsonMapper
    {
        public static List<SearchSuggestion> ParseSuggestions(string json)
        {
            var suggestions = new List<SearchSuggestion>();
            var result = JsonSerializer.Deserialize<JsonElement>(json);
            if (result.ValueKind != JsonValueKind.Object)
                throw new JsonException("响应不是有效的 JSON 对象。");

            if (result.TryGetProperty("result", out var searchResult))
            {
                if (searchResult.TryGetProperty("songs", out var songs))
                {
                    foreach (var song in songs.EnumerateArray())
                    {
                        suggestions.Add(new SearchSuggestion
                        {
                            Id = song.GetProperty("id").GetInt64().ToString(),
                            Name = song.GetProperty("name").GetString() ?? string.Empty,
                            Type = "song",
                            Artist = song.TryGetProperty("artists", out var artists) && artists.GetArrayLength() > 0
                                ? artists[0].GetProperty("name").GetString() ?? string.Empty
                                : string.Empty
                        });
                    }
                }

                if (searchResult.TryGetProperty("artists", out var artistsList))
                {
                    foreach (var artist in artistsList.EnumerateArray())
                    {
                        suggestions.Add(new SearchSuggestion
                        {
                            Id = artist.GetProperty("id").GetInt64().ToString(),
                            Name = artist.GetProperty("name").GetString() ?? string.Empty,
                            Type = "artist"
                        });
                    }
                }

                if (searchResult.TryGetProperty("playlists", out var playlists))
                {
                    foreach (var playlist in playlists.EnumerateArray())
                    {
                        suggestions.Add(new SearchSuggestion
                        {
                            Id = playlist.GetProperty("id").GetInt64().ToString(),
                            Name = playlist.GetProperty("name").GetString() ?? string.Empty,
                            Type = "playlist"
                        });
                    }
                }
            }
            return suggestions;
        }
    }
}
