using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using music.Core.Abstractions;
using music.Core.Results;

namespace music.Core.Netease
{
    public sealed record ApiResponse(ApiResult Result, string Body)
    {
        public bool Success => Result.Success;
    }

    public sealed class NeteaseHttpTransport : IDisposable
    {
        private const string DeviceIdKey = "DeviceId";
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _requestSemaphore = new(4, 4);
        private readonly ServerEndpoint _endpoint;
        private readonly SessionCookieJar _cookieJar;
        private readonly IApiLog _log;
        private readonly string _deviceId;

        public NeteaseHttpTransport(
            ServerEndpoint endpoint,
            SessionCookieJar cookieJar,
            ISettingsStore settings,
            IApiLog log,
            HttpMessageHandler? handler = null)
        {
            _endpoint = endpoint;
            _cookieJar = cookieJar;
            _log = log;

            var deviceId = settings.GetString(DeviceIdKey) ?? string.Empty;
            if (string.IsNullOrEmpty(deviceId))
            {
                deviceId = Guid.NewGuid().ToString("N");
                settings.SetString(DeviceIdKey, deviceId);
            }
            _deviceId = deviceId;

            handler ??= new SocketsHttpHandler { UseCookies = false };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        }

        public string DeviceId => _deviceId;
        internal TimeSpan Timeout => _httpClient.Timeout;

        public async Task<ApiResponse> SendAsync(
            HttpMethod method,
            string url,
            HttpContent? content = null,
            bool includeCookies = true,
            bool requireBusinessCode = false,
            CancellationToken cancellationToken = default)
        {
            await _requestSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var baseUri = _endpoint.BaseUri;
                var separator = url.Contains('?') ? "&" : "?";
                var fullUrl = $"{url}{separator}deviceId={Uri.EscapeDataString(_deviceId)}";
                using var request = new HttpRequestMessage(method, ServerEndpoint.CreateRequestUri(baseUri, fullUrl))
                {
                    Content = content
                };

                if (includeCookies)
                {
                    var cookieHeader = _cookieJar.BuildRequestHeader();
                    if (!string.IsNullOrEmpty(cookieHeader))
                        request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
                }

                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (includeCookies && response.Headers.TryGetValues("Set-Cookie", out var cookies))
                    _cookieJar.ApplySetCookies(cookies);

                var result = ApiResult.FromHttpResponse((int)response.StatusCode, body, requireBusinessCode);
                if (!result.Success)
                {
                    _log.Write($"{method} {request.RequestUri?.AbsolutePath}: {result.Error} code={result.Code} {result.DisplayMessage}");
                }

                return new ApiResponse(result, body);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Write($"{method} {url} 网络异常: {ex.Message}");
                return new ApiResponse(ApiResult.NetworkError(), string.Empty);
            }
            finally
            {
                _requestSemaphore.Release();
            }
        }

        public async Task<ApiResponse> GetAsync(string url, CancellationToken cancellationToken = default) =>
            await SendAsync(HttpMethod.Get, url, null, includeCookies: true, requireBusinessCode: false, cancellationToken).ConfigureAwait(false);

        public async Task<ApiResponse> GetWithoutCookieAsync(string url, CancellationToken cancellationToken = default) =>
            await SendAsync(HttpMethod.Get, url, null, includeCookies: false, requireBusinessCode: false, cancellationToken).ConfigureAwait(false);

        public void Dispose()
        {
            _httpClient.Dispose();
            _requestSemaphore.Dispose();
        }
    }
}
