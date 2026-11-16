using System;
using music.Core.Abstractions;

namespace music.Core.Netease
{
    public sealed class ServerEndpoint
    {
        public const string DefaultServerAddress = "http://localhost:3000";
        private const string ServerAddressKey = "ServerAddress";

        private readonly ISettingsStore _settings;
        private readonly IApiLog _log;
        private string _baseUrl;
        private volatile Uri _baseUri;

        public ServerEndpoint(ISettingsStore settings, IApiLog log)
        {
            _settings = settings;
            _log = log;
            _baseUrl = TryNormalizeBaseUrl(settings.GetString(ServerAddressKey), out var stored)
                ? stored
                : DefaultServerAddress;
            _baseUri = new Uri(_baseUrl, UriKind.Absolute);
        }

        public string BaseUrl => _baseUrl;
        public Uri BaseUri => _baseUri;
        public bool IsSecureConnection => _baseUri.Scheme == Uri.UriSchemeHttps;
        public bool IsLoopbackServer => _baseUri.IsLoopback;

        public void UpdateBaseUrl(string url)
        {
            if (!TryNormalizeBaseUrl(url, out var normalized))
                throw new ArgumentException("服务器地址必须是有效的 HTTP 或 HTTPS 绝对地址。", nameof(url));

            // 先写 Uri 再写字符串，保证飞行中的请求要么拿旧 Uri，要么拿新 Uri，不出现撕裂状态
            _baseUri = new Uri(normalized, UriKind.Absolute);
            _baseUrl = normalized;

            _settings.SetString(ServerAddressKey, normalized);
            _log.Write($"baseUrl 已切换为 {normalized}");
        }

        public static bool TryNormalizeBaseUrl(string? url, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(url)) return false;

            var trimmed = url.Trim();
            if (!trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                trimmed = "http://" + trimmed;

            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
            if (string.IsNullOrEmpty(uri.Host)) return false;

            normalized = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
            return normalized.Length > 0;
        }

        public static Uri CreateRequestUri(Uri baseUri, string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var absolute) &&
                (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
            {
                return absolute;
            }

            return new Uri(baseUri, url);
        }
    }
}
