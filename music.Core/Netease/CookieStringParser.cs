using System;
using System.Collections.Generic;
using System.Linq;

namespace music.Core.Netease
{
    // 解析 Cookie 字符串：兼容夹在其中的 Set-Cookie 属性（Path/Expires/Max-Age 等），
    // 只保留真正的 name=value 对，避免属性名混进请求的 Cookie 头
    public static class CookieStringParser
    {
        private static readonly HashSet<string> Attributes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Path", "Domain", "Expires", "Max-Age", "SameSite", "Secure",
            "HttpOnly", "Priority", "Partitioned", "Version", "Comment"
        };

        public static IEnumerable<KeyValuePair<string, string>> Parse(string? cookie)
        {
            if (string.IsNullOrEmpty(cookie)) yield break;

            foreach (var fragment in cookie.Split(new[] { ';', ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = fragment.Trim();
                var separator = pair.IndexOf('=');
                if (separator <= 0) continue;

                var name = pair[..separator].Trim();
                var value = pair[(separator + 1)..].Trim();
                if (name.Length == 0 || value.Length == 0) continue;
                if (Attributes.Contains(name)) continue;

                yield return new KeyValuePair<string, string>(name, value);
            }
        }

        /// <summary>
        /// Builds the outbound Cookie HTTP header from session cookies, ensuring os=pc is present
        /// as required by /song/url/v1 unless the session already specifies an os value.
        /// </summary>
        public static string BuildRequestHeader(IReadOnlyDictionary<string, string> sessionCookies)
        {
            var pairs = new List<string>(sessionCookies.Count + 1);
            var hasOs = false;

            foreach (var kvp in sessionCookies)
            {
                var name = SanitizeHeaderToken(kvp.Key);
                var value = SanitizeHeaderToken(kvp.Value);
                if (name.Length == 0 || value.Length == 0) continue;

                if (string.Equals(name, "os", StringComparison.OrdinalIgnoreCase))
                {
                    hasOs = true;
                }

                pairs.Add($"{name}={value}");
            }

            if (!hasOs)
            {
                pairs.Add("os=pc");
            }

            return string.Join("; ", pairs);
        }

        private static string SanitizeHeaderToken(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
        }
    }
}
