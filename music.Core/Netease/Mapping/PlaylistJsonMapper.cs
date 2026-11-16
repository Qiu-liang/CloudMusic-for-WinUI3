using System;
using System.Collections.Generic;
using System.Text.Json;
using music.Services;

namespace music.Core.Netease.Mapping
{
    internal static class PlaylistJsonMapper
    {
        public static List<PlaylistInfo> ParseUserPlaylists(string json)
        {
            var playlists = new List<PlaylistInfo>();
            try
            {
                var result = JsonSerializer.Deserialize<JsonElement>(json);
                if (result.TryGetProperty("playlist", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        playlists.Add(new PlaylistInfo
                        {
                            Id = item.GetProperty("id").GetInt64().ToString(),
                            Name = item.GetProperty("name").GetString() ?? string.Empty,
                            CoverImgUrl = item.GetProperty("coverImgUrl").GetString() ?? string.Empty,
                            TrackCount = item.GetProperty("trackCount").GetInt32(),
                            PlayCount = item.TryGetProperty("playCount", out var pc) ? pc.GetInt64() : 0,
                            CreatorName = item.GetProperty("creator").GetProperty("nickname").GetString() ?? string.Empty,
                            CreatorId = item.GetProperty("creator").GetProperty("userId").GetInt64()
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetPlaylists Error: {ex.Message}");
            }
            return playlists;
        }

        public static bool IsTrackCountZero(string detailJson)
        {
            try
            {
                var detailResult = JsonSerializer.Deserialize<JsonElement>(detailJson);
                return detailResult.ValueKind == JsonValueKind.Object &&
                       detailResult.TryGetProperty("playlist", out var playlistObj) &&
                       playlistObj.ValueKind == JsonValueKind.Object &&
                       playlistObj.TryGetProperty("trackCount", out var trackCountProp) &&
                       trackCountProp.TryGetInt32(out var trackCount) &&
                       trackCount == 0;
            }
            catch
            {
                return false;
            }
        }

        public static PlaylistDetailInfo? ParsePlaylistInfo(string json)
        {
            try
            {
                var result = JsonSerializer.Deserialize<JsonElement>(json);
                if (result.TryGetProperty("playlist", out var playlist))
                {
                    var info = new PlaylistDetailInfo
                    {
                        Id = playlist.GetProperty("id").GetInt64().ToString(),
                        Name = playlist.GetProperty("name").GetString() ?? string.Empty,
                        CoverImgUrl = playlist.TryGetProperty("coverImgUrl", out var coverImgUrl) ? coverImgUrl.GetString() ?? string.Empty : string.Empty,
                        Description = playlist.TryGetProperty("description", out var desc) ? desc.GetString() ?? string.Empty : string.Empty,
                        TrackCount = playlist.TryGetProperty("trackCount", out var trackCount) ? trackCount.GetInt32() : 0,
                        PlayCount = playlist.TryGetProperty("playCount", out var playCount) ? playCount.GetInt64() : 0,
                        SubscribedCount = playlist.TryGetProperty("subscribedCount", out var subscribedCount) ? subscribedCount.GetInt64() : 0,
                        ShareCount = playlist.TryGetProperty("shareCount", out var shareCount) ? shareCount.GetInt64() : 0,
                        CommentCount = playlist.TryGetProperty("commentCount", out var commentCount) ? commentCount.GetInt64() : 0,
                        Privacy = playlist.TryGetProperty("privacy", out var privacy) ? privacy.GetInt32() : 0,
                        Subscribed = playlist.TryGetProperty("subscribed", out var subscribed) && subscribed.GetBoolean()
                    };

                    if (playlist.TryGetProperty("creator", out var creator))
                    {
                        info.CreatorName = creator.TryGetProperty("nickname", out var nickname) ? nickname.GetString() ?? string.Empty : string.Empty;
                        info.CreatorAvatarUrl = creator.TryGetProperty("avatarUrl", out var avatarUrl) ? avatarUrl.GetString() ?? string.Empty : string.Empty;
                        info.CreatorId = creator.TryGetProperty("userId", out var creatorId) ? creatorId.GetInt64() : 0;
                    }

                    if (playlist.TryGetProperty("tags", out var tags))
                    {
                        foreach (var tag in tags.EnumerateArray())
                        {
                            info.Tags.Add(tag.GetString() ?? string.Empty);
                        }
                    }

                    return info;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetPlaylistInfo Error: {ex.Message}");
            }
            return null;
        }

        public static string? FindFavoritePlaylistId(string json)
        {
            try
            {
                var result = JsonSerializer.Deserialize<JsonElement>(json);
                if (result.TryGetProperty("playlist", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        if (item.TryGetProperty("specialType", out var specialType) &&
                            specialType.TryGetInt32(out var type) && type == 5 &&
                            item.TryGetProperty("id", out var id))
                        {
                            return id.GetInt64().ToString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] FindFavoritePlaylistId Error: {ex.Message}");
            }
            return null;
        }

        public static (bool NeedsLogin, List<RecommendedPlaylist> Items) ParseRecommendResource(string json)
        {
            var playlists = new List<RecommendedPlaylist>();
            try
            {
                var result = JsonSerializer.Deserialize<JsonElement>(json);
                if (result.TryGetProperty("code", out var code) && code.GetInt32() == 301)
                {
                    return (true, playlists);
                }

                if (result.TryGetProperty("recommend", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        var playCount = 0L;
                        if (item.TryGetProperty("playCount", out var pc))
                            playCount = pc.GetInt64();
                        else if (item.TryGetProperty("played", out var played))
                            playCount = played.GetInt64();
                        else if (item.TryGetProperty("playcount", out var playcount))
                            playCount = playcount.GetInt64();

                        playlists.Add(new RecommendedPlaylist
                        {
                            Id = item.GetProperty("id").GetInt64().ToString(),
                            Name = item.GetProperty("name").GetString() ?? string.Empty,
                            PicUrl = item.TryGetProperty("picUrl", out var picUrl) ? picUrl.GetString() ?? string.Empty : string.Empty,
                            PlayCount = playCount,
                            TrackCount = item.TryGetProperty("trackCount", out var trackCount) ? trackCount.GetInt32() : 0
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetRecommendResource Error: {ex.Message}");
            }
            return (false, playlists);
        }

        public static List<RecommendedPlaylist> ParseRecommendedPlaylists(string json)
        {
            var playlists = new List<RecommendedPlaylist>();
            try
            {
                var result = JsonSerializer.Deserialize<JsonElement>(json);
                if (result.TryGetProperty("result", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        playlists.Add(new RecommendedPlaylist
                        {
                            Id = item.GetProperty("id").GetInt64().ToString(),
                            Name = item.GetProperty("name").GetString() ?? string.Empty,
                            PicUrl = item.GetProperty("picUrl").GetString() ?? string.Empty,
                            PlayCount = item.TryGetProperty("playCount", out var playCount) ? playCount.GetInt64() : 0,
                            TrackCount = item.TryGetProperty("trackCount", out var trackCount) ? trackCount.GetInt32() : 0
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetRecommendedPlaylists Error: {ex.Message}");
            }
            return playlists;
        }

        public static List<RecommendedPlaylist> ParseSearchPlaylists(string json)
        {
            var playlists = new List<RecommendedPlaylist>();
            try
            {
                var result = JsonSerializer.Deserialize<JsonElement>(json);
                if (result.TryGetProperty("result", out var searchResult) &&
                    searchResult.TryGetProperty("playlists", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        playlists.Add(new RecommendedPlaylist
                        {
                            Id = item.GetProperty("id").GetInt64().ToString(),
                            Name = item.GetProperty("name").GetString() ?? string.Empty,
                            PicUrl = item.TryGetProperty("coverImgUrl", out var coverImgUrl) ? coverImgUrl.GetString() ?? string.Empty : string.Empty,
                            PlayCount = item.TryGetProperty("playCount", out var playCount) ? playCount.GetInt64() : 0,
                            TrackCount = item.TryGetProperty("trackCount", out var trackCount) ? trackCount.GetInt32() : 0
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] SearchPlaylists Error: {ex.Message}");
            }
            return playlists;
        }
    }
}
