using System;
using System.Collections.Generic;

namespace music.Services
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
    }
}
