using System;
using System.Collections.Generic;
using System.Text.Json;
using music.Core.Results;

namespace music.Core.Netease
{
    /// <summary>
    /// Parsed /song/url/v1 item metadata returned by the API.
    /// </summary>
    public sealed class SongUrlData
    {
        public string Url { get; init; } = string.Empty;
        public string ActualLevel { get; init; } = string.Empty;
        public int Fee { get; init; }
        public int Payed { get; init; }
        public bool IsFreeTrial { get; init; }
        public int? CannotListenReason { get; init; }
    }

    /// <summary>
    /// API-driven audio quality privilege and availability metadata for a song,
    /// parsed from /song/detail (or song list privilege objects).
    /// </summary>
    public sealed class SongQualityPrivilege
    {
        private static readonly (string PropertyName, string Quality)[] SongQualityProperties =
        {
            ("l", "standard"),
            ("m", "higher"),
            ("h", "exhigh"),
            ("sq", "lossless"),
            ("hr", "hires"),
            ("je", "jyeffect"),
            ("sk", "sky"),
            ("db", "dolby"),
            ("jm", "jymaster")
        };

        public int Fee { get; init; }
        public int Payed { get; init; }
        public string FreeMaxLevel { get; init; } = string.Empty;
        public string PlayableMaxLevel { get; init; } = string.Empty;
        public string SongMaxLevel { get; init; } = string.Empty;
        public IReadOnlyDictionary<string, int> ChargeTypesByQuality { get; init; } =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlySet<string> AvailableQualities { get; init; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static SongQualityPrivilege? FromSongDetailJson(string? json, string? songId = null)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return null;

                JsonElement? matchedSong = null;
                if (root.TryGetProperty("songs", out var songs) && songs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in songs.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object) continue;
                        if (string.IsNullOrWhiteSpace(songId) || MatchesId(item, songId))
                        {
                            matchedSong = item;
                            break;
                        }
                    }
                }

                JsonElement? matchedPrivilege = null;
                if (root.TryGetProperty("privileges", out var privileges) && privileges.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in privileges.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object) continue;
                        if (string.IsNullOrWhiteSpace(songId) || MatchesId(item, songId))
                        {
                            matchedPrivilege = item;
                            break;
                        }
                    }
                }
                else if (matchedSong.HasValue &&
                         matchedSong.Value.TryGetProperty("privilege", out var embeddedPrivilege) &&
                         embeddedPrivilege.ValueKind == JsonValueKind.Object)
                {
                    matchedPrivilege = embeddedPrivilege;
                }
                else if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                {
                    if (data.TryGetProperty("privilege", out var dataPrivilege) &&
                        dataPrivilege.ValueKind == JsonValueKind.Object)
                    {
                        matchedPrivilege = dataPrivilege;
                    }
                    matchedSong ??= data;
                }

                if (!matchedSong.HasValue && !matchedPrivilege.HasValue) return null;
                return FromJson(matchedSong, matchedPrivilege);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public static SongQualityPrivilege? FromJson(JsonElement? songElement, JsonElement? privilegeElement)
        {
            if (!privilegeElement.HasValue &&
                songElement.HasValue &&
                songElement.Value.ValueKind == JsonValueKind.Object &&
                songElement.Value.TryGetProperty("privilege", out var embedded) &&
                embedded.ValueKind == JsonValueKind.Object)
            {
                privilegeElement = embedded;
            }

            if (!songElement.HasValue && !privilegeElement.HasValue) return null;

            var fee = 0;
            var payed = 0;
            var freeMaxLevel = string.Empty;
            var playableMaxLevel = string.Empty;
            var songMaxLevel = string.Empty;
            var chargeTypes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var availableQualities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (songElement.HasValue && songElement.Value.ValueKind == JsonValueKind.Object)
            {
                var song = songElement.Value;
                if (SongUrlPolicy.TryReadInt32(song, "fee", out var parsedSongFee))
                {
                    fee = parsedSongFee;
                }

                foreach (var (propName, quality) in SongQualityProperties)
                {
                    if (song.TryGetProperty(propName, out var qObj) && qObj.ValueKind == JsonValueKind.Object)
                    {
                        availableQualities.Add(quality);
                    }
                }
            }

            if (privilegeElement.HasValue && privilegeElement.Value.ValueKind == JsonValueKind.Object)
            {
                var priv = privilegeElement.Value;
                if (SongUrlPolicy.TryReadInt32(priv, "fee", out var parsedPrivFee))
                {
                    fee = parsedPrivFee;
                }

                if (SongUrlPolicy.TryReadInt32(priv, "payed", out var parsedPayed))
                {
                    payed = parsedPayed;
                }

                if (priv.TryGetProperty("flLevel", out var flLevelProp) && flLevelProp.ValueKind == JsonValueKind.String)
                {
                    freeMaxLevel = (flLevelProp.GetString() ?? string.Empty).Trim();
                }
                if (string.IsNullOrEmpty(freeMaxLevel) &&
                    SongUrlPolicy.TryReadInt32(priv, "fl", out var flBitrate))
                {
                    freeMaxLevel = SongUrlPolicy.BitrateToQualityLevel(flBitrate);
                }

                if (priv.TryGetProperty("plLevel", out var plLevelProp) && plLevelProp.ValueKind == JsonValueKind.String)
                {
                    playableMaxLevel = (plLevelProp.GetString() ?? string.Empty).Trim();
                }
                if (string.IsNullOrEmpty(playableMaxLevel) &&
                    SongUrlPolicy.TryReadInt32(priv, "pl", out var plBitrate))
                {
                    playableMaxLevel = SongUrlPolicy.BitrateToQualityLevel(plBitrate);
                }

                if (priv.TryGetProperty("playMaxBrLevel", out var playMaxProp) && playMaxProp.ValueKind == JsonValueKind.String)
                {
                    songMaxLevel = (playMaxProp.GetString() ?? string.Empty).Trim();
                }
                else if (priv.TryGetProperty("maxBrLevel", out var maxBrProp) && maxBrProp.ValueKind == JsonValueKind.String)
                {
                    songMaxLevel = (maxBrProp.GetString() ?? string.Empty).Trim();
                }
                if (string.IsNullOrEmpty(songMaxLevel) &&
                    SongUrlPolicy.TryReadInt32(priv, "maxbr", out var maxBitrate))
                {
                    songMaxLevel = SongUrlPolicy.BitrateToQualityLevel(maxBitrate);
                }

                if (priv.TryGetProperty("chargeInfoList", out var chargeList) &&
                    chargeList.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in chargeList.EnumerateArray())
                    {
                        if (entry.ValueKind != JsonValueKind.Object) continue;
                        if (!SongUrlPolicy.TryReadInt32(entry, "rate", out var rate))
                            continue;
                        if (!SongUrlPolicy.TryReadInt32(entry, "chargeType", out var chargeType))
                            continue;

                        var level = SongUrlPolicy.BitrateToQualityLevel(rate);
                        if (!string.IsNullOrEmpty(level) && !string.Equals(level, "none", StringComparison.OrdinalIgnoreCase))
                        {
                            chargeTypes[level] = chargeType;
                        }
                    }
                }
            }

            // Require at least one meaningful privilege signal from the API
            if (string.IsNullOrEmpty(freeMaxLevel) &&
                string.IsNullOrEmpty(playableMaxLevel) &&
                chargeTypes.Count == 0 &&
                !privilegeElement.HasValue)
            {
                return null;
            }

            return new SongQualityPrivilege
            {
                Fee = fee,
                Payed = payed,
                FreeMaxLevel = freeMaxLevel,
                PlayableMaxLevel = playableMaxLevel,
                SongMaxLevel = songMaxLevel,
                ChargeTypesByQuality = chargeTypes,
                AvailableQualities = availableQualities
            };
        }

        /// <summary>
        /// Determines whether the specified audio quality requires VIP for this song
        /// based on the API's chargeInfoList, flLevel (fl), and fee fields.
        /// </summary>
        public bool RequiresVip(string? quality)
        {
            var normalized = SongUrlPolicy.NormalizeQuality(quality);

            if (Fee == 1) return true;

            if (ChargeTypesByQuality.TryGetValue(normalized, out var chargeType))
            {
                if (chargeType > 0) return true;
                if (!string.Equals(FreeMaxLevel, "none", StringComparison.OrdinalIgnoreCase))
                {
                    var freeRank = SongUrlPolicy.GetQualityRank(FreeMaxLevel);
                    var targetRank = SongUrlPolicy.GetQualityRank(normalized);
                    if (freeRank <= 0 || targetRank <= freeRank) return false;
                }
            }

            if (string.Equals(FreeMaxLevel, "none", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var flRank = SongUrlPolicy.GetQualityRank(FreeMaxLevel);
            var qRank = SongUrlPolicy.GetQualityRank(normalized);
            if (flRank > 0 && qRank > 0)
            {
                return qRank > flRank;
            }

            if (ChargeTypesByQuality.Count > 0 && qRank > 0)
            {
                var maxFreeRank = 0;
                var anyChargedLowerOrEqual = false;
                foreach (var kvp in ChargeTypesByQuality)
                {
                    var rank = SongUrlPolicy.GetQualityRank(kvp.Key);
                    if (rank <= 0) continue;
                    if (kvp.Value == 0 && rank > maxFreeRank) maxFreeRank = rank;
                    if (kvp.Value > 0 && rank <= qRank) anyChargedLowerOrEqual = true;
                }

                if (anyChargedLowerOrEqual) return true;
                if (maxFreeRank > 0) return qRank > maxFreeRank;
            }

            return false;
        }

        /// <summary>
        /// Determines whether the current user is allowed to play the specified quality
        /// for this song according to the API's privilege response and current VIP state.
        /// </summary>
        public bool CanUserPlay(string? quality, bool isVip)
        {
            var normalized = SongUrlPolicy.NormalizeQuality(quality);
            if (!RequiresVip(normalized)) return true;
            if (Payed > 0) return true;
            if (!isVip) return false;

            return true;
        }

        /// <summary>
        /// Resolves the best available quality for this song according to API privilege data
        /// when the user selects a quality they do not have permission to play.
        /// </summary>
        public string ResolveFallbackQuality(string? requestedQuality, bool isVip, string? previousQuality = null)
        {
            var requested = SongUrlPolicy.NormalizeQuality(requestedQuality);
            if (CanUserPlay(requested, isVip)) return requested;

            if (!string.IsNullOrWhiteSpace(previousQuality))
            {
                var previous = SongUrlPolicy.NormalizeQuality(previousQuality);
                if (CanUserPlay(previous, isVip)) return previous;
            }

            if (!string.IsNullOrWhiteSpace(PlayableMaxLevel) &&
                !string.Equals(PlayableMaxLevel, "none", StringComparison.OrdinalIgnoreCase))
            {
                var pl = SongUrlPolicy.NormalizeQuality(PlayableMaxLevel);
                if (CanUserPlay(pl, isVip)) return pl;
            }

            if (!string.IsNullOrWhiteSpace(FreeMaxLevel) &&
                !string.Equals(FreeMaxLevel, "none", StringComparison.OrdinalIgnoreCase))
            {
                var fl = SongUrlPolicy.NormalizeQuality(FreeMaxLevel);
                if (CanUserPlay(fl, isVip)) return fl;
            }

            var bestFreeQuality = string.Empty;
            var bestFreeRank = 0;
            foreach (var kvp in ChargeTypesByQuality)
            {
                if (kvp.Value == 0)
                {
                    var rank = SongUrlPolicy.GetQualityRank(kvp.Key);
                    if (rank > bestFreeRank)
                    {
                        bestFreeRank = rank;
                        bestFreeQuality = kvp.Key;
                    }
                }
            }

            if (!string.IsNullOrEmpty(bestFreeQuality)) return bestFreeQuality;
            return "standard";
        }

        private static bool MatchesId(JsonElement element, string targetId)
        {
            if (!element.TryGetProperty("id", out var idProp)) return false;
            if (idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt64(out var numId))
            {
                return string.Equals(numId.ToString(), targetId.Trim(), StringComparison.Ordinal);
            }
            if (idProp.ValueKind == JsonValueKind.String)
            {
                return string.Equals(idProp.GetString()?.Trim(), targetId.Trim(), StringComparison.Ordinal);
            }
            return false;
        }
    }

    /// <summary>
    /// Helper policy for song URL quality fallback, API privilege parsing, and response URL extraction.
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

        public static string NormalizeQuality(string? quality) =>
            string.IsNullOrWhiteSpace(quality) ? "standard" : quality.Trim().ToLowerInvariant();

        /// <summary>
        /// Returns the ascending rank of a quality level (standard = 1 .. jymaster = 9), or 0 if unknown/none.
        /// </summary>
        public static int GetQualityRank(string? quality)
        {
            if (string.IsNullOrWhiteSpace(quality)) return 0;
            var normalized = quality.Trim();
            if (string.Equals(normalized, "none", StringComparison.OrdinalIgnoreCase)) return 0;

            var index = Array.FindIndex(
                OrderedQualities,
                q => string.Equals(q, normalized, StringComparison.OrdinalIgnoreCase));

            return index < 0 ? 0 : OrderedQualities.Length - index;
        }

        /// <summary>
        /// Maps a Netease bitrate integer (e.g. 128000, 192000, 320000, 999000, 1999000) to its level name.
        /// </summary>
        public static string BitrateToQualityLevel(int bitrate)
        {
            if (bitrate <= 0) return "none";
            if (bitrate <= 128000) return "standard";
            if (bitrate <= 192000) return "higher";
            if (bitrate <= 320000) return "exhigh";
            if (bitrate <= 999000) return "lossless";
            return "hires";
        }

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
        public static string ExtractSongUrl(string? json) => ExtractSongUrlData(json)?.Url ?? string.Empty;

        /// <summary>
        /// Safely reads an Int32 from a JSON property, returning false when missing, null, or non-numeric.
        /// Note: JsonElement.TryGetInt32 throws InvalidOperationException on JsonValueKind.Null.
        /// </summary>
        internal static bool TryReadInt32(JsonElement obj, string propertyName, out int value)
        {
            value = 0;
            if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(propertyName, out var element))
            {
                return false;
            }

            if (element.ValueKind == JsonValueKind.Number)
            {
                return element.TryGetInt32(out value);
            }

            if (element.ValueKind == JsonValueKind.String)
            {
                return int.TryParse(element.GetString(), out value);
            }

            return false;
        }

        /// <summary>
        /// Safely reads an Int64 from a JSON property, returning false when missing, null, or non-numeric.
        /// Note: JsonElement.TryGetInt64 throws InvalidOperationException on JsonValueKind.Null.
        /// </summary>
        internal static bool TryReadInt64(JsonElement obj, string propertyName, out long value)
        {
            value = 0;
            if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(propertyName, out var element))
            {
                return false;
            }

            if (element.ValueKind == JsonValueKind.Number)
            {
                return element.TryGetInt64(out value);
            }

            if (element.ValueKind == JsonValueKind.String)
            {
                return long.TryParse(element.GetString(), out value);
            }

            return false;
        }

        internal static int ReadInt32(JsonElement obj, string propertyName, int defaultValue = 0) =>
            TryReadInt32(obj, propertyName, out var value) ? value : defaultValue;

        internal static long ReadInt64(JsonElement obj, string propertyName, long defaultValue = 0) =>
            TryReadInt64(obj, propertyName, out var value) ? value : defaultValue;

        internal static bool ReadBoolean(JsonElement obj, string propertyName, bool defaultValue = false)
        {
            if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(propertyName, out var element))
            {
                return defaultValue;
            }

            return element.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => element.TryGetInt64(out var numeric) ? numeric != 0 : defaultValue,
                JsonValueKind.String => bool.TryParse(element.GetString(), out var parsed) ? parsed : defaultValue,
                _ => defaultValue
            };
        }

        internal static string ReadString(JsonElement obj, string propertyName, string defaultValue = "")
        {
            if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(propertyName, out var element))
            {
                return defaultValue;
            }

            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? defaultValue,
                JsonValueKind.Number => element.GetRawText(),
                _ => defaultValue
            };
        }

        /// <summary>
        /// Extracts URL, actual level, fee, payed, and trial metadata from a /song/url/v1 or /song/url JSON payload.
        /// </summary>
        public static SongUrlData? ExtractSongUrlData(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return null;
                if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                    return null;

                foreach (var item in data.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;

                    var url = ReadString(item, "url");
                    var level = ReadString(item, "level");
                    var fee = ReadInt32(item, "fee");
                    var payed = ReadInt32(item, "payed");
                    var isFreeTrial = item.TryGetProperty("freeTrialInfo", out var trialElement) &&
                                      trialElement.ValueKind == JsonValueKind.Object;

                    int? cannotListenReason = null;
                    if (item.TryGetProperty("freeTrialPrivilege", out var trialPriv) &&
                        trialPriv.ValueKind == JsonValueKind.Object &&
                        TryReadInt32(trialPriv, "cannotListenReason", out var parsedReason))
                    {
                        cannotListenReason = parsedReason;
                    }

                    return new SongUrlData
                    {
                        Url = url.Trim(),
                        ActualLevel = level.Trim(),
                        Fee = fee,
                        Payed = payed,
                        IsFreeTrial = isFreeTrial,
                        CannotListenReason = cannotListenReason
                    };
                }
            }
            catch (JsonException)
            {
                return null;
            }

            return null;
        }

        private static void AddIfMissing(List<string> levels, string candidate)
        {
            if (!levels.Exists(l => string.Equals(l, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                levels.Add(candidate);
            }
        }

        /// <summary>
        /// Evaluates whether a /vip/info response indicates an active, unexpired VIP subscription.
        /// Note: redVipLevel is a historical growth level and stays &gt; 0 after expiration.
        /// </summary>
        public static bool EvaluateVipStatus(string? vipInfoJson, long? fallbackNowMs = null)
        {
            if (string.IsNullOrWhiteSpace(vipInfoJson)) return false;

            try
            {
                using var document = JsonDocument.Parse(vipInfoJson);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return false;
                if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                    return false;

                var now = TryReadInt64(data, "now", out var serverNow) && serverNow > 0
                    ? serverNow
                    : (fallbackNowMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

                return IsPackageActive(data, "associator", now) ||
                       IsPackageActive(data, "musicPackage", now) ||
                       IsPackageActive(data, "redplus", now);
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool IsPackageActive(JsonElement data, string packageName, long nowMs)
        {
            if (!data.TryGetProperty(packageName, out var package) || package.ValueKind != JsonValueKind.Object)
                return false;

            if (!TryReadInt64(package, "expireTime", out var expireTime) ||
                expireTime <= nowMs)
            {
                return false;
            }

            if (package.TryGetProperty("rights", out var rightsElement) &&
                (rightsElement.ValueKind == JsonValueKind.True || rightsElement.ValueKind == JsonValueKind.False))
            {
                return rightsElement.GetBoolean();
            }

            return true;
        }

        /// <summary>
        /// Returns true when a /song/url/v1 failure affects all quality levels for the request
        /// (network errors or backend risk-control -460), so further /song/url/v1 quality retries
        /// should be skipped immediately in favor of the legacy /song/url fallback.
        /// </summary>
        public static bool ShouldShortCircuitV1Fallback(ApiResult? failure) =>
            failure != null && (failure.Error == ApiErrorKind.Network || failure.IsRiskControl);

        /// <summary>
        /// Selects and formats the final failure result when both /song/url/v1 and /song/url fail
        /// to produce a playable URL, surfacing actionable guidance when blocked by -460 risk control.
        /// </summary>
        public static ApiResult ResolveFinalPlaybackUrlFailure(ApiResult? v1Failure, ApiResult? legacyFailure)
        {
            var failure = v1Failure ?? legacyFailure;
            if (legacyFailure != null && !legacyFailure.Success && legacyFailure.IsRiskControl)
            {
                failure = legacyFailure;
            }

            if (failure != null && failure.IsRiskControl)
            {
                return failure with
                {
                    Message = $"{failure.DisplayMessage}\n建议尝试关闭系统代理或 VPN，或在设置中切换 API 服务地址后重试。"
                };
            }

            return failure ?? new ApiResult(false, ApiErrorKind.Business, "获取歌曲播放地址失败，请稍后重试。");
        }

        /// <summary>
        /// Maps an audio quality tag to its user-facing display label.
        /// </summary>
        public static string GetQualityDisplayName(string? quality) => NormalizeQuality(quality) switch
        {
            "standard" => "标准",
            "higher" => "较高",
            "exhigh" => "极高",
            "lossless" => "无损",
            "hires" => "Hi-Res",
            "jyeffect" => "高清环绕声",
            "sky" => "沉浸环绕声",
            "dolby" => "杜比全景声",
            "jymaster" => "超清母带",
            _ => "标准"
        };
    }
}
