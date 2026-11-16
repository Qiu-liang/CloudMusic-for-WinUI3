using System;
using System.Collections.Generic;
using System.Text.Json;
using music.Core.Netease;

namespace music.Core.Services
{
    public sealed class SongLikeChangedEventArgs : EventArgs
    {
        public SongLikeChangedEventArgs(string songId, bool isLiked)
        {
            SongId = songId;
            IsLiked = isLiked;
        }

        public string SongId { get; }
        public bool IsLiked { get; }
    }

    /// <summary>
    /// Pure helpers for bypassing API GET caches and reconciling liked-song and playlist track IDs.
    /// </summary>
    public static class LikedMusicSyncHelper
    {
        public static string AppendTimestamp(string url, long timestampMs)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;
            var separator = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            return $"{url}{separator}timestamp={timestampMs}";
        }

        public static List<string> ExtractPlaylistTrackIds(string? playlistDetailJson)
        {
            var ids = new List<string>();
            if (string.IsNullOrWhiteSpace(playlistDetailJson)) return ids;

            try
            {
                using var document = JsonDocument.Parse(playlistDetailJson);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("playlist", out var playlist) ||
                    playlist.ValueKind != JsonValueKind.Object ||
                    !playlist.TryGetProperty("trackIds", out var trackIds) ||
                    trackIds.ValueKind != JsonValueKind.Array)
                {
                    return ids;
                }

                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in trackIds.EnumerateArray())
                {
                    var id = entry.ValueKind == JsonValueKind.Object
                        ? SongUrlPolicy.ReadString(entry, "id")
                        : ReadScalarId(entry);
                    if (!string.IsNullOrWhiteSpace(id) && seen.Add(id))
                    {
                        ids.Add(id);
                    }
                }
            }
            catch (JsonException)
            {
                return new List<string>();
            }

            return ids;
        }

        public static List<string> ExtractLikedIds(string? likelistJson)
        {
            var ids = new List<string>();
            if (string.IsNullOrWhiteSpace(likelistJson)) return ids;

            try
            {
                using var document = JsonDocument.Parse(likelistJson);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("ids", out var idsArray) ||
                    idsArray.ValueKind != JsonValueKind.Array)
                {
                    return ids;
                }

                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in idsArray.EnumerateArray())
                {
                    var id = ReadScalarId(entry);
                    if (!string.IsNullOrWhiteSpace(id) && seen.Add(id))
                    {
                        ids.Add(id);
                    }
                }
            }
            catch (JsonException)
            {
                return new List<string>();
            }

            return ids;
        }

        public static bool ApplyLikeChange(ISet<string> likedIds, string? songId, bool isLiked)
        {
            if (likedIds == null || string.IsNullOrWhiteSpace(songId)) return false;
            return isLiked ? likedIds.Add(songId) : likedIds.Remove(songId);
        }

        private static string ReadScalarId(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Number when element.TryGetInt64(out var numId) => numId.ToString(),
                JsonValueKind.String => element.GetString()?.Trim() ?? string.Empty,
                _ => string.Empty
            };
        }
    }
}
