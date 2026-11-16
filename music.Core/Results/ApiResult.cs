using System.Text.Json;
using music.Core.Netease;

namespace music.Core.Results
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

    public sealed record ApiResult(bool Success, ApiErrorKind Error, string Message, int Code = 0, int BusinessCode = 0)
    {
        public bool IsRiskControl => BusinessCode == -460 || Code == -460;

        public static ApiResult FromHttpResponse(int statusCode, string body, bool requireBusinessCode = false)
        {
            var isHttpSuccess = statusCode >= 200 && statusCode < 300;
            if (!isHttpSuccess)
            {
                return FromHttpFailure(statusCode, body);
            }

            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return new ApiResult(false, ApiErrorKind.InvalidResponse, "服务器返回了无法识别的结果。");

                var hasCode = root.TryGetProperty("code", out _);
                var hasNumericCode = SongUrlPolicy.TryReadInt32(root, "code", out var code);
                if (!hasCode)
                    return requireBusinessCode
                        ? new ApiResult(false, ApiErrorKind.InvalidResponse, "服务器未返回业务状态码。")
                        : new ApiResult(true, ApiErrorKind.None, string.Empty);
                if (!hasNumericCode)
                    return new ApiResult(false, ApiErrorKind.InvalidResponse, "服务器返回了无效的状态码。");
                if (code == 200)
                    return new ApiResult(true, ApiErrorKind.None, string.Empty, code, code);

                var message = ExtractErrorMessage(root);
                var error = code == 301 || code == 401 ? ApiErrorKind.Authentication : ApiErrorKind.Business;
                return new ApiResult(false, error, string.IsNullOrWhiteSpace(message) ? "操作未完成，请重试。" : message, code, code);
            }
            catch (JsonException)
            {
                return new ApiResult(false, ApiErrorKind.InvalidResponse, "服务器返回了无法识别的结果。");
            }
        }

        private static ApiResult FromHttpFailure(int statusCode, string body)
        {
            var error = statusCode == 401 ? ApiErrorKind.Authentication : ApiErrorKind.Http;
            if (string.IsNullOrWhiteSpace(body))
                return new ApiResult(false, error, $"服务器返回 HTTP {statusCode}。", statusCode);

            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    var bizCode = SongUrlPolicy.ReadInt32(root, "code");
                    var bizMessage = ExtractErrorMessage(root);
                    if (bizCode == 301 || bizCode == 401) error = ApiErrorKind.Authentication;

                    var detail = (bizCode != 0, string.IsNullOrWhiteSpace(bizMessage)) switch
                    {
                        (true, false) => $"code={bizCode}: {bizMessage}",
                        (true, true) => $"code={bizCode}",
                        (false, false) => bizMessage!,
                        _ => string.Empty
                    };

                    if (!string.IsNullOrEmpty(detail))
                        return new ApiResult(false, error, $"服务器返回 HTTP {statusCode}（{detail}）。", statusCode, bizCode);
                }
            }
            catch (JsonException)
            {
                // Ignore non-JSON error body and fall back to generic HTTP status message.
            }

            return new ApiResult(false, error, $"服务器返回 HTTP {statusCode}。", statusCode);
        }

        private static string? ExtractErrorMessage(JsonElement root)
        {
            var message = root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
                ? messageElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(message) &&
                root.TryGetProperty("msg", out var msgElement) &&
                msgElement.ValueKind == JsonValueKind.String)
            {
                message = msgElement.GetString();
            }

            return message;
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
