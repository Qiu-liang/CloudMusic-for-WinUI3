using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using music.Core.Abstractions;
using music.Core.Netease;
using music.Services;

namespace music.Core.Services
{
    public interface ISongSourceResolver
    {
        Task<(string Url, ApiResult Result)> ResolvePlaybackUrlAsync(
            string songId,
            CancellationToken cancellationToken = default);
    }

    public sealed class SongSourceResolver : ISongSourceResolver
    {
        private readonly NeteaseHttpTransport _transport;
        private readonly ISettingsStore _settings;

        public SongSourceResolver(NeteaseHttpTransport transport, ISettingsStore settings)
        {
            _transport = transport;
            _settings = settings;
        }

        public async Task<(string Url, ApiResult Result)> ResolvePlaybackUrlAsync(
            string songId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(songId))
            {
                return (string.Empty, new ApiResult(false, ApiErrorKind.Business, "无效的歌曲 ID，无法播放。"));
            }

            var quality = _settings.GetString("AudioQuality") ?? "standard";
            var levels = SongUrlPolicy.BuildQualityFallbackLevels(quality);
            var encodedId = Uri.EscapeDataString(songId);
            ApiResult? lastFailure = null;
            string? trialFallbackUrl = null;

            foreach (var level in levels)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var response = await _transport.SendAsync(
                    HttpMethod.Get,
                    $"/song/url/v1?id={encodedId}&level={Uri.EscapeDataString(level)}",
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                if (response.Success)
                {
                    var urlData = SongUrlPolicy.ExtractSongUrlData(response.Body);
                    if (urlData != null && !string.IsNullOrEmpty(urlData.Url))
                    {
                        if (!urlData.IsFreeTrial)
                        {
                            return (urlData.Url, new ApiResult(true, ApiErrorKind.None, string.Empty, 200));
                        }

                        trialFallbackUrl ??= urlData.Url;
                    }

                    lastFailure = new ApiResult(false, ApiErrorKind.Business, "当前音质没有可用的音频地址，可能受版权或 VIP 权限限制。");
                }
                else
                {
                    lastFailure = response.Result;
                    if (response.Result.Error == ApiErrorKind.Network)
                    {
                        return (string.Empty, response.Result);
                    }

                    if (SongUrlPolicy.ShouldShortCircuitV1Fallback(response.Result))
                    {
                        break;
                    }
                }
            }

            if (!string.IsNullOrEmpty(trialFallbackUrl))
            {
                return (trialFallbackUrl, new ApiResult(true, ApiErrorKind.None, string.Empty, 200));
            }

            // /song/url/v1 各降级音质均失败、遇风控(-460)或无 URL 时，立即回退旧版 /song/url 接口
            cancellationToken.ThrowIfCancellationRequested();
            var legacyResponse = await _transport.SendAsync(
                HttpMethod.Get,
                $"/song/url?id={encodedId}&br=320000",
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (legacyResponse.Success)
            {
                var legacyUrl = SongUrlPolicy.ExtractSongUrl(legacyResponse.Body);
                if (!string.IsNullOrEmpty(legacyUrl))
                {
                    return (legacyUrl, new ApiResult(true, ApiErrorKind.None, string.Empty, 200));
                }

                lastFailure ??= new ApiResult(false, ApiErrorKind.Business, "当前歌曲暂无可用音源，可能受版权或 VIP 权限限制。");
            }

            var finalFailure = SongUrlPolicy.ResolveFinalPlaybackUrlFailure(
                lastFailure,
                legacyResponse.Success ? null : legacyResponse.Result);
            return (string.Empty, finalFailure);
        }
    }
}
