using System;
using System.Collections.Generic;
using System.Text.Json;

namespace music.Services
{
    /// <summary>
    /// Helper policy for song URL quality fallback and response URL extraction.
    /// </summary>
    public static class SongUrlPolicy
    {
        private static readonly string[] OrderedQualities =
        {
            "jymaster",
            "dolby",
            "sky",
            "jyeffect",
            "hires",
            "lossless",
            "exhigh",
            "higher",
            "standard"
        };

        /// <summary>
        /// Builds the ordered list of /song/url/v1 quality levels to attempt,
        /// degrading from the requested quality down toward standard.
        /// </summary>
        public static IReadOnlyList<string> BuildQualityFallbackLevels(string? requestedQuality)
        {
            var normalized = string.IsNullOrWhiteSpace(requestedQuality)
                ? "standard"
                : requestedQuality.Trim();

            var levels = new List<string> { normalized };

            var index = Array.FindIndex(
                OrderedQualities,
                q => string.Equals(q, normalized, StringComparison.OrdinalIgnoreCase));

            if (index < 0 || index < Array.IndexOf(OrderedQualities, "exhigh"))
            {
                AddIfMissing(levels, "exhigh");
            }

            if (index < 0 || index < Array.IndexOf(OrderedQualities, "standard"))
            {
                AddIfMissing(levels, "standard");
            }

            return levels;
        }

        /// <summary>
        /// Extracts a playable audio URL from a /song/url/v1 or /song/url JSON payload.
        /// </summary>
        public static string ExtractSongUrl(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return string.Empty;

            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return string.Empty;
                if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                    return string.Empty;

                foreach (var item in data.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    if (item.TryGetProperty("url", out var urlElement) && urlElement.ValueKind == JsonValueKind.String)
                    {
                        var url = urlElement.GetString();
                        if (!string.IsNullOrWhiteSpace(url)) return url;
                    }
                }
            }
            catch (JsonException)
            {
                return string.Empty;
            }

            return string.Empty;
        }

        private static void AddIfMissing(List<string> levels, string candidate)
        {
            if (!levels.Exists(l => string.Equals(l, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                levels.Add(candidate);
            }
        }
    }
}
