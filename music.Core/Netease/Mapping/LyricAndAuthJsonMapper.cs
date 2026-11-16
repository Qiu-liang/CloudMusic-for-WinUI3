using System;
using System.Collections.Generic;
using System.Text.Json;
using music.Services;

namespace music.Core.Netease.Mapping
{
    internal static class LyricJsonMapper
    {
        public static LyricInfo? ParseLyricInfo(string json)
        {
            try
            {
                var result = JsonSerializer.Deserialize<JsonElement>(json);
                var lyricInfo = new LyricInfo();

                if (result.TryGetProperty("lrc", out var lrc) &&
                    lrc.TryGetProperty("lyric", out var lyric))
                {
                    lyricInfo.LrcLyric = lyric.GetString() ?? string.Empty;
                }

                if (result.TryGetProperty("tlyric", out var tlyric) &&
                    tlyric.TryGetProperty("lyric", out var tLyric))
                {
                    lyricInfo.TranslatedLyric = tLyric.GetString() ?? string.Empty;
                }

                return lyricInfo;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetLyrics Error: {ex.Message}");
                return null;
            }
        }
    }

    internal static class AuthJsonMapper
    {
        public static bool TryParseCode200(string json)
        {
            try
            {
                var result = JsonSerializer.Deserialize<JsonElement>(json);
                return result.TryGetProperty("code", out var code) && code.GetInt32() == 200;
            }
            catch
            {
                return false;
            }
        }

        public static string? TryExtractQrUnikey(string json)
        {
            try
            {
                var result = JsonSerializer.Deserialize<JsonElement>(json);
                if (result.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("unikey", out var unikey))
                {
                    return unikey.GetString();
                }
            }
            catch { }
            return null;
        }

        public static string? TryExtractQrImageBase64(string json)
        {
            try
            {
                var result = JsonSerializer.Deserialize<JsonElement>(json);
                if (result.TryGetProperty("data", out var qrData) &&
                    qrData.TryGetProperty("qrimg", out var qrimg))
                {
                    var base64 = qrimg.GetString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(base64) && base64.Contains(','))
                    {
                        base64 = base64[(base64.IndexOf(',') + 1)..];
                    }
                    return string.IsNullOrEmpty(base64) ? null : base64;
                }
            }
            catch { }
            return null;
        }

        public static QrLoginStatusResult ParseQrCheckStatus(string json)
        {
            var checkResult = JsonSerializer.Deserialize<JsonElement>(json);
            if (!checkResult.TryGetProperty("code", out var code) || !code.TryGetInt32(out var statusCode))
            {
                return new QrLoginStatusResult(QrLoginState.Waiting, 0, string.Empty);
            }

            return statusCode switch
            {
                800 => new QrLoginStatusResult(QrLoginState.Expired, 800, string.Empty),
                801 => new QrLoginStatusResult(QrLoginState.Waiting, 801, string.Empty),
                802 => new QrLoginStatusResult(QrLoginState.Scanned, 802, string.Empty),
                803 => new QrLoginStatusResult(
                    QrLoginState.Confirmed,
                    803,
                    TryExtractCookie(checkResult, out var cookie) ? cookie : string.Empty),
                _ => new QrLoginStatusResult(QrLoginState.Failed, statusCode, string.Empty)
            };
        }

        public static bool TryExtractCookie(JsonElement checkResult, out string cookie)
        {
            cookie = string.Empty;
            if (!checkResult.TryGetProperty("cookie", out var element)) return false;

            if (element.ValueKind == JsonValueKind.String)
            {
                cookie = element.GetString() ?? string.Empty;
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                var pairs = new List<string>();
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) continue;
                    var raw = item.GetString();
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    pairs.Add(raw.Split(';')[0].Trim());
                }

                cookie = string.Join("; ", pairs);
            }

            return !string.IsNullOrEmpty(cookie);
        }
    }
}
