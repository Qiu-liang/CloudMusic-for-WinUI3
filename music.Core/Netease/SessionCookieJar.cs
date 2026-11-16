using System;
using System.Collections.Generic;
using System.Linq;
using music.Core.Abstractions;
using music.Services;

namespace music.Core.Netease
{
    public sealed class SessionCookieJar
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, string> _sessionCookies = new();
        private readonly ICredentialStore _credentials;
        private readonly IApiLog _log;
        private string _cookie = string.Empty;
        private bool _isInitialized;

        public SessionCookieJar(ICredentialStore credentials, IApiLog log)
        {
            _credentials = credentials;
            _log = log;
        }

        public bool HasCookie
        {
            get
            {
                lock (_lock) return !string.IsNullOrEmpty(_cookie);
            }
        }

        public bool IsInitialized
        {
            get
            {
                lock (_lock) return _isInitialized;
            }
        }

        public IReadOnlyCollection<string> GetCookieNamesSnapshot()
        {
            lock (_lock) return _sessionCookies.Keys.ToList();
        }

        public void LoadFromStore()
        {
            var stored = _credentials.Load();
            lock (_lock)
            {
                _sessionCookies.Clear();
                MergeCore(stored);
                if (!string.IsNullOrEmpty(_cookie))
                {
                    _isInitialized = true;
                }
            }
        }

        public void ReplaceAndMarkInitialized(string cookie, bool persist = false)
        {
            string? toSave = null;
            lock (_lock)
            {
                _sessionCookies.Clear();
                MergeCore(cookie);
                if (!string.IsNullOrEmpty(_cookie))
                {
                    _isInitialized = true;
                    if (persist) toSave = _cookie;
                }
            }

            if (toSave != null) _credentials.Save(toSave);
        }

        public void MergeAndSave(string cookie)
        {
            string toSave;
            lock (_lock)
            {
                MergeCore(cookie);
                _isInitialized = true;
                toSave = _cookie;
            }

            _credentials.Save(toSave);
        }

        public void MarkInitialized()
        {
            lock (_lock) _isInitialized = true;
        }

        public string BuildRequestHeader()
        {
            lock (_lock)
            {
                return CookieStringParser.BuildRequestHeader(_sessionCookies);
            }
        }

        public void ApplySetCookies(IEnumerable<string> setCookies)
        {
            string? cookieToSave = null;
            lock (_lock)
            {
                foreach (var cookie in setCookies)
                {
                    var firstSegment = cookie.Split(';')[0];
                    var idx = firstSegment.IndexOf('=');
                    if (idx <= 0) continue;

                    var name = firstSegment[..idx].Trim();
                    var value = firstSegment[(idx + 1)..].Trim();
                    if (name.Length == 0) continue;

                    if (name == "MUSIC_U" || name == "__csrf" || name.StartsWith("MUSIC_", StringComparison.Ordinal))
                        _log.Write($"Set-Cookie: {name} 已更新");

                    if (value.Length == 0) _sessionCookies.Remove(name);
                    else _sessionCookies[name] = value;
                }

                RebuildCookieStringCore();

                if (_isInitialized && !string.IsNullOrEmpty(_cookie))
                    cookieToSave = _cookie;
            }

            if (cookieToSave != null) _credentials.Save(cookieToSave);
        }

        public void Clear()
        {
            _credentials.Clear();
            lock (_lock)
            {
                _cookie = string.Empty;
                _sessionCookies.Clear();
                _isInitialized = false;
            }
        }

        private void MergeCore(string cookie)
        {
            foreach (var pair in CookieStringParser.Parse(cookie))
                _sessionCookies[pair.Key] = pair.Value;

            RebuildCookieStringCore();
        }

        private void RebuildCookieStringCore()
        {
            _cookie = string.Join("; ", _sessionCookies.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        }
    }
}
