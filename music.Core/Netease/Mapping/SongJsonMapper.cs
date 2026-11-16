using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using music.Models;
using music.Services;

namespace music.Core.Netease.Mapping
{
    internal static class SongJsonMapper
    {
        public static Dictionary<string, JsonElement> ExtractPrivilegesById(JsonElement root)
        {
            var map = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("privileges", out var privileges) &&
                privileges.ValueKind == JsonValueKind.Array)
            {
                foreach (var priv in privileges.EnumerateArray())
                {
                    if (priv.ValueKind != JsonValueKind.Object) continue;
                    if (priv.TryGetProperty("id", out var idProp))
                    {
                        var id = idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt64(out var numId)
                            ? numId.ToString()
                            : idProp.GetString() ?? string.Empty;
                        if (!string.IsNullOrEmpty(id))
                        {
                            map[id] = priv;
                        }
                    }
                }
            }
            return map;
        }

        public static Song ParseSong(
            JsonElement item,
            JsonElement? privilege = null,
            Func<string, bool>? isLikedLookup = null,
            Action<string, SongQualityPrivilege>? onPrivilegeParsed = null)
        {
            var song = new Song
            {
                Id = SongUrlPolicy.ReadString(item, "id"),
                Name = SongUrlPolicy.ReadString(item, "name"),
                Duration = SongUrlPolicy.ReadInt64(item, "dt")
            };

            if (SongUrlPolicy.TryReadInt32(item, "fee", out var fee))
            {
                song.Fee = fee;
                System.Diagnostics.Debug.WriteLine($"[API] Song '{song.Name}' fee={song.Fee}");
            }
            song.VipFlag = SongUrlPolicy.ReadBoolean(item, "vipFlag");
            song.VipPlayFlag = SongUrlPolicy.ReadBoolean(item, "vipPlayFlag");
            song.PayPlayFlag = SongUrlPolicy.ReadBoolean(item, "payPlayFlag");

            song.QualityPrivilege = SongQualityPrivilege.FromJson(item, privilege);
            if (song.QualityPrivilege != null)
            {
                if (song.Fee == 0 && song.QualityPrivilege.Fee != 0)
                {
                    song.Fee = song.QualityPrivilege.Fee;
                }
                onPrivilegeParsed?.Invoke(song.Id, song.QualityPrivilege);
            }

            song.IsVip = song.Fee == 1 || song.VipFlag || song.VipPlayFlag;
            song.IsPaid = song.Fee == 4 || song.PayPlayFlag;

            if (song.IsVip)
                System.Diagnostics.Debug.WriteLine($"[API] Song '{song.Name}' is VIP");

            if (item.TryGetProperty("ar", out var artists) && artists.ValueKind == JsonValueKind.Array)
            {
                foreach (var artist in artists.EnumerateArray())
                {
                    if (artist.ValueKind != JsonValueKind.Object) continue;
                    song.Artists.Add(new Artist
                    {
                        Id = SongUrlPolicy.ReadString(artist, "id"),
                        Name = SongUrlPolicy.ReadString(artist, "name")
                    });
                }
            }

            if (item.TryGetProperty("al", out var album) && album.ValueKind == JsonValueKind.Object)
            {
                song.Album = new Album
                {
                    Id = SongUrlPolicy.ReadString(album, "id"),
                    Name = SongUrlPolicy.ReadString(album, "name")
                };
                song.CoverImgUrl = SongUrlPolicy.ReadString(album, "picUrl");
            }

            if (!string.IsNullOrEmpty(song.Id) && isLikedLookup != null)
            {
                song.Liked = isLikedLookup(song.Id);
            }

            return song;
        }

        public static List<Song> ParseSongsList(
            string json,
            Func<string, bool>? isLikedLookup = null,
            Action<string, SongQualityPrivilege>? onPrivilegeParsed = null)
        {
            var songs = new List<Song>();
            if (string.IsNullOrWhiteSpace(json)) return songs;

            var result = JsonSerializer.Deserialize<JsonElement>(json);
            if (result.ValueKind != JsonValueKind.Object)
                throw new JsonException("响应不是有效的 JSON 对象。");

            var privilegesById = ExtractPrivilegesById(result);
            if (result.TryGetProperty("songs", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    var id = SongUrlPolicy.ReadString(item, "id");
                    JsonElement? priv = privilegesById.TryGetValue(id, out var matchedPriv) ? matchedPriv : null;
                    songs.Add(ParseSong(item, priv, isLikedLookup, onPrivilegeParsed));
                }
            }

            return songs;
        }

        public static List<Song> ParseDailyRecommendSongs(
            string json,
            int limit,
            Func<string, bool>? isLikedLookup = null,
            Action<string, SongQualityPrivilege>? onPrivilegeParsed = null)
        {
            var songs = new List<Song>();
            if (string.IsNullOrWhiteSpace(json)) return songs;

            var result = JsonSerializer.Deserialize<JsonElement>(json);
            if (result.ValueKind != JsonValueKind.Object)
                throw new JsonException("响应不是有效的 JSON 对象。");

            if (result.TryGetProperty("data", out var data) &&
                data.TryGetProperty("dailySongs", out var dailySongs))
            {
                foreach (var item in dailySongs.EnumerateArray().Take(Math.Max(0, limit)))
                {
                    songs.Add(ParseSong(item, null, isLikedLookup, onPrivilegeParsed));
                }
            }

            return songs;
        }

        public static List<Song> ParsePersonalizedSongs(string json)
        {
            var songs = new List<Song>();
            if (string.IsNullOrWhiteSpace(json)) return songs;

            var result = JsonSerializer.Deserialize<JsonElement>(json);
            if (result.ValueKind != JsonValueKind.Object)
                throw new JsonException("响应不是有效的 JSON 对象。");

            if (result.TryGetProperty("result", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    var song = new Song
                    {
                        Id = SongUrlPolicy.ReadString(item, "id"),
                        Name = SongUrlPolicy.ReadString(item, "name")
                    };

                    if (item.TryGetProperty("song", out var songObj) && songObj.ValueKind == JsonValueKind.Object)
                    {
                        if (songObj.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var artist in artists.EnumerateArray())
                            {
                                if (artist.ValueKind != JsonValueKind.Object) continue;
                                song.Artists.Add(new Artist
                                {
                                    Id = SongUrlPolicy.ReadString(artist, "id"),
                                    Name = SongUrlPolicy.ReadString(artist, "name")
                                });
                            }
                        }

                        if (songObj.TryGetProperty("album", out var album) && album.ValueKind == JsonValueKind.Object)
                        {
                            song.Album = new Album
                            {
                                Id = SongUrlPolicy.ReadString(album, "id"),
                                Name = SongUrlPolicy.ReadString(album, "name")
                            };
                            song.CoverImgUrl = SongUrlPolicy.ReadString(album, "picUrl");
                        }

                        song.Duration = SongUrlPolicy.ReadInt64(songObj, "duration");
                    }

                    if (string.IsNullOrEmpty(song.CoverImgUrl) && item.TryGetProperty("picUrl", out var outerPicUrl))
                    {
                        song.CoverImgUrl = outerPicUrl.GetString() ?? string.Empty;
                    }

                    songs.Add(song);
                }
            }

            return songs;
        }

        public static List<Song> ParseIntelligenceSongs(
            string json,
            Func<string, bool>? isLikedLookup = null,
            Action<string, SongQualityPrivilege>? onPrivilegeParsed = null)
        {
            var songs = new List<Song>();
            if (string.IsNullOrWhiteSpace(json)) return songs;

            var result = JsonSerializer.Deserialize<JsonElement>(json);
            if (result.ValueKind != JsonValueKind.Object)
                throw new JsonException("响应不是有效的 JSON 对象。");

            if (result.TryGetProperty("data", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    try
                    {
                        var songElement = item.TryGetProperty("songInfo", out var songInfo) ? songInfo : item;
                        if (songElement.TryGetProperty("id", out _) && songElement.TryGetProperty("name", out _))
                            songs.Add(ParseSong(songElement, null, isLikedLookup, onPrivilegeParsed));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"GetIntelligenceSongs: 跳过无法解析的条目 {ex.GetType().Name}");
                    }
                }
            }

            return songs;
        }

        public static List<Song> ParseSearchSongs(
            string json,
            Func<string, bool>? isLikedLookup = null,
            Action<string, SongQualityPrivilege>? onPrivilegeParsed = null)
        {
            var songs = new List<Song>();
            if (string.IsNullOrWhiteSpace(json)) return songs;

            var result = JsonSerializer.Deserialize<JsonElement>(json);
            if (result.ValueKind != JsonValueKind.Object)
                throw new JsonException("响应不是有效的 JSON 对象。");

            if (result.TryGetProperty("result", out var searchResult) &&
                searchResult.TryGetProperty("songs", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    songs.Add(ParseSong(item, null, isLikedLookup, onPrivilegeParsed));
                }
            }

            return songs;
        }
    }
}
