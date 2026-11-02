using System.Text.Json;

namespace music.Services
{
    public enum ApiErrorKind
    {
        None,
        Network,
        Http,
        Business,
        Authentication,
        InvalidResponse
    }

    public sealed record ApiResult(bool Success, ApiErrorKind Error, string Message, int Code = 0)
    {
        public static ApiResult FromHttpResponse(int statusCode, string body, bool requireBusinessCode = false)
        {
            if (statusCode < 200 || statusCode >= 300)
                return new ApiResult(false, ApiErrorKind.Http, $"服务器返回 HTTP {statusCode}。", statusCode);

            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return new ApiResult(false, ApiErrorKind.InvalidResponse, "服务器返回了无法识别的结果。");

                var hasCode = root.TryGetProperty("code", out var codeElement);
                var code = hasCode && codeElement.TryGetInt32(out var value) ? value : 0;
                if (!hasCode)
                    return requireBusinessCode
                        ? new ApiResult(false, ApiErrorKind.InvalidResponse, "服务器未返回业务状态码。")
                        : new ApiResult(true, ApiErrorKind.None, string.Empty);
                if (code == 200)
                    return new ApiResult(true, ApiErrorKind.None, string.Empty, code);

                var message = root.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : null;
                message ??= root.TryGetProperty("msg", out var msgElement) ? msgElement.GetString() : null;
                var error = code == 301 || code == 401 ? ApiErrorKind.Authentication : ApiErrorKind.Business;
                return new ApiResult(false, error, string.IsNullOrWhiteSpace(message) ? "操作未完成，请重试。" : message, code);
            }
            catch (JsonException)
            {
                return new ApiResult(false, ApiErrorKind.InvalidResponse, "服务器返回了无法识别的结果。");
            }
        }

        public static ApiResult NetworkError() =>
            new(false, ApiErrorKind.Network, "无法连接服务器，请检查 API 地址和网络。");

        public string DisplayMessage => Error switch
        {
            ApiErrorKind.Authentication => string.IsNullOrEmpty(Message) ? "登录已失效，请重新登录。" : Message,
            _ => Message
        };
    }
}
