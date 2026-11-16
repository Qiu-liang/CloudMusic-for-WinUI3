using System;
using System.Collections.Generic;
using System.Text.Json;
using music.Services;

namespace music.Core.Netease.Mapping
{
    internal static class ProfileJsonMapper
    {
        public static UserDetailInfo? ParseUserDetail(string json)
        {
            try
            {
                var result = JsonSerializer.Deserialize<JsonElement>(json);
                if (result.TryGetProperty("profile", out var profile))
                {
                    return new UserDetailInfo
                    {
                        UserId = profile.GetProperty("userId").GetInt64(),
                        Nickname = profile.GetProperty("nickname").GetString() ?? string.Empty,
                        AvatarUrl = profile.TryGetProperty("avatarUrl", out var avatar) ? avatar.GetString() ?? string.Empty : string.Empty,
                        Signature = profile.TryGetProperty("signature", out var sig) ? sig.GetString() ?? string.Empty : string.Empty,
                        FollowCount = profile.TryGetProperty("follows", out var follows) ? follows.GetInt32() : 0,
                        FollowedCount = profile.TryGetProperty("followeds", out var followeds) ? followeds.GetInt32() : 0,
                        EventCount = profile.TryGetProperty("eventCount", out var events) ? events.GetInt32() : 0
                    };
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetUserDetail Error: {ex.Message}");
            }
            return null;
        }

        public static List<FollowUser> ParseFollowUsers(string json, string propertyName)
        {
            var users = new List<FollowUser>();
            try
            {
                var result = JsonSerializer.Deserialize<JsonElement>(json);
                if (result.TryGetProperty(propertyName, out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        users.Add(new FollowUser
                        {
                            UserId = item.GetProperty("userId").GetInt64(),
                            Nickname = item.GetProperty("nickname").GetString() ?? string.Empty,
                            AvatarUrl = item.TryGetProperty("avatarUrl", out var av) ? av.GetString() ?? string.Empty : string.Empty,
                            Signature = item.TryGetProperty("signature", out var sig) ? sig.GetString() ?? string.Empty : string.Empty
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] ParseFollowUsers Error: {ex.Message}");
            }
            return users;
        }

        public static List<RecentSong> ParseRecentSongs(string json)
        {
            var songs = new List<RecentSong>();
            try
            {
                var result = JsonSerializer.Deserialize<JsonElement>(json);
                if (result.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("list", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        try
                        {
                            var song = new RecentSong();

                            if (item.TryGetProperty("playTime", out var playTime))
                                song.PlayTime = playTime.GetInt64();

                            if (item.TryGetProperty("resourceId", out var resourceId))
                                song.ResourceId = resourceId.GetString() ?? string.Empty;

                            if (item.TryGetProperty("resourceType", out var resourceType))
                                song.ResourceType = resourceType.GetString() ?? string.Empty;

                            if (item.TryGetProperty("data", out var songData) && songData.ValueKind == JsonValueKind.Object)
                            {
                                song.Id = SongUrlPolicy.ReadString(songData, "id");
                                song.Name = SongUrlPolicy.ReadString(songData, "name");

                                if (songData.TryGetProperty("ar", out var artists) && artists.ValueKind == JsonValueKind.Array)
                                {
                                    var artistNames = new List<string>();
                                    foreach (var artist in artists.EnumerateArray())
                                    {
                                        if (artist.ValueKind != JsonValueKind.Object) continue;
                                        artistNames.Add(SongUrlPolicy.ReadString(artist, "name"));
                                    }
                                    song.Artist = string.Join(" / ", artistNames);
                                }

                                if (songData.TryGetProperty("al", out var album) && album.ValueKind == JsonValueKind.Object)
                                    song.AlbumName = SongUrlPolicy.ReadString(album, "name");

                                song.Duration = SongUrlPolicy.ReadInt64(songData, "dt");
                                song.Fee = SongUrlPolicy.ReadInt32(songData, "fee");
                            }

                            songs.Add(song);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[API] Recent song parse error: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetRecentSongs Error: {ex.Message}");
            }
            return songs;
        }
    }
}
