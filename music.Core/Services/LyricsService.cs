using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using music.Core.Netease;
using music.Core.Netease.Mapping;
using music.Models;
using music.Services;

namespace music.Core.Services
{
    public interface ILyricsService
    {
        Task<LyricInfo?> GetLyricsAsync(string songId, CancellationToken cancellationToken = default);
    }

    public sealed class LyricsService : ILyricsService
    {
        private readonly NeteaseHttpTransport _transport;

        public LyricsService(NeteaseHttpTransport transport)
        {
            _transport = transport;
        }

        public async Task<LyricInfo?> GetLyricsAsync(string songId, CancellationToken cancellationToken = default)
        {
            try
            {
                var resp = await _transport.GetAsync($"/lyric?id={Uri.EscapeDataString(songId)}", cancellationToken).ConfigureAwait(false);
                return resp.Success ? LyricJsonMapper.ParseLyricInfo(resp.Body) : null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetLyrics Error: {ex.Message}");
                return null;
            }
        }
    }

    public static class LrcParser
    {
        private static readonly Regex LrcTimeRegex = new(@"\[(\d{1,3}):(\d{2})(?:\.(\d{1,3}))?\]", RegexOptions.Compiled);

        /// <summary>
        /// 单时间戳解析（保留普通歌词页 LyricsPage 的原有行为）。
        /// </summary>
        public static List<LyricLine> ParseSimple(string? lrcContent)
        {
            var lines = new List<LyricLine>();
            if (string.IsNullOrEmpty(lrcContent)) return lines;

            foreach (var line in lrcContent.Split('\n'))
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine)) continue;

                if (trimmedLine.StartsWith('[') && trimmedLine.Contains(']'))
                {
                    var timeEnd = trimmedLine.IndexOf(']');
                    var timeStr = trimmedLine.Substring(1, timeEnd - 1);
                    var text = trimmedLine.Substring(timeEnd + 1).Trim();

                    if (TryParseSimpleTime(timeStr, out var time) && !string.IsNullOrEmpty(text))
                    {
                        lines.Add(new LyricLine { Time = time, Text = text });
                    }
                }
            }

            return lines.OrderBy(l => l.Time).ToList();
        }

        /// <summary>
        /// 多时间戳 + 译文对齐解析（保留独立歌词窗口 LyricsWindow 的原有行为）。
        /// </summary>
        public static List<LyricLine> ParseWithTranslations(string? lrcContent, string? tlyricContent = null)
        {
            var lines = new List<LyricLine>();
            if (string.IsNullOrEmpty(lrcContent)) return lines;

            foreach (var raw in lrcContent.Split('\n'))
            {
                var trimmed = raw.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                var matches = LrcTimeRegex.Matches(trimmed);
                if (matches.Count == 0) continue;

                var lastMatch = matches[matches.Count - 1];
                var text = trimmed.Substring(lastMatch.Index + lastMatch.Length).Trim();
                if (string.IsNullOrEmpty(text)) continue;

                foreach (Match m in matches)
                {
                    if (TryParseRegexTime(m, out var t))
                        lines.Add(new LyricLine { Time = t, Text = text });
                }
            }

            lines = lines.OrderBy(l => l.Time).ToList();

            if (!string.IsNullOrEmpty(tlyricContent) && lines.Count > 0)
            {
                var trans = new List<(TimeSpan Time, string Text)>();
                foreach (var raw in tlyricContent.Split('\n'))
                {
                    var trimmed = raw.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;

                    var matches = LrcTimeRegex.Matches(trimmed);
                    if (matches.Count == 0) continue;

                    var lastMatch = matches[matches.Count - 1];
                    var text = trimmed.Substring(lastMatch.Index + lastMatch.Length).Trim();
                    if (string.IsNullOrEmpty(text) || text.StartsWith("//")) continue;

                    foreach (Match m in matches)
                    {
                        if (TryParseRegexTime(m, out var t))
                            trans.Add((t, text));
                    }
                }

                foreach (var (tTime, tText) in trans)
                {
                    var idx = lines.FindIndex(l => Math.Abs((l.Time - tTime).TotalMilliseconds) < 50);
                    if (idx >= 0 && string.IsNullOrEmpty(lines[idx].Translation))
                        lines[idx].Translation = tText;
                }
            }

            return lines;
        }

        private static bool TryParseSimpleTime(string timeStr, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            try
            {
                var parts = timeStr.Split(':');
                if (parts.Length == 2)
                {
                    var minutes = int.Parse(parts[0], CultureInfo.InvariantCulture);
                    var seconds = double.Parse(parts[1], CultureInfo.InvariantCulture);
                    time = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static bool TryParseRegexTime(Match match, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            if (!int.TryParse(match.Groups[1].Value, out var minutes)) return false;
            if (!int.TryParse(match.Groups[2].Value, out var seconds)) return false;
            int ms = 0;
            if (match.Groups[3].Success)
            {
                var frac = match.Groups[3].Value;
                if (!int.TryParse(frac, out ms)) return false;
                if (frac.Length == 1) ms *= 100;
                else if (frac.Length == 2) ms *= 10;
            }
            time = new TimeSpan(0, 0, minutes, seconds, ms);
            return true;
        }
    }
}
