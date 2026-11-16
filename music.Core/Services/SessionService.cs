using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using music.Core.Abstractions;
using music.Core.Netease;
using music.Core.Netease.Mapping;
using music.Core.Results;
using music.Services;

namespace music.Core.Services
{
    public interface ISessionService
    {
        bool IsLoggedIn { get; }
        long UserId { get; }
        bool IsSecureConnection { get; }
        event EventHandler? SessionChanged;

        Task InitializeAsync();
        Task<bool> LoginAnonymouslyAsync();
        Task<(bool Success, string Message, int Code, string RawResponse)> LoginWithPhoneAsync(string phone, string password);
        Task<QrCodePayload?> GenerateQrCodeAsync(CancellationToken cancellationToken = default);
        Task<ApiResult<QrLoginStatusResult>> CheckQrStatusAsync(string key, CancellationToken cancellationToken = default);
        Task<bool> SetLoginCookieAsync(string cookie);
        Task<ApiResult<UserInfo?>> GetUserInfoAsync();
        UserInfo? GetCachedUserInfo();
        bool GetVipStatus();
        Task<bool> CheckVipStatusAsync();
        void ResetLogin();
    }

    public sealed class SessionService : ISessionService
    {
        private const string UserIdKey = "UserId";
        private const string NicknameKey = "Nickname";
        private const string AvatarUrlKey = "AvatarUrl";
        private const string IsVipKey = "IsVip";
        private const string AllowInsecureLoginKey = "AllowInsecureLogin";

        private readonly NeteaseHttpTransport _transport;
        private readonly ServerEndpoint _endpoint;
        private readonly SessionCookieJar _cookieJar;
        private readonly ISettingsStore _settings;
        private readonly IApiLog _log;
        private readonly Action _clearPrivilegeCache;
        private readonly Action _clearLikedState;
        private long _userId;

        public SessionService(
            NeteaseHttpTransport transport,
            ServerEndpoint endpoint,
            SessionCookieJar cookieJar,
            ISettingsStore settings,
            IApiLog log,
            Action clearPrivilegeCache,
            Action clearLikedState)
        {
            _transport = transport;
            _endpoint = endpoint;
            _cookieJar = cookieJar;
            _settings = settings;
            _log = log;
            _clearPrivilegeCache = clearPrivilegeCache;
            _clearLikedState = clearLikedState;

            _cookieJar.LoadFromStore();
            _userId = _settings.ContainsKey(UserIdKey) ? _settings.GetInt64(UserIdKey) : 0;

            _log.Write($"ctor: cookieNames=[{string.Join(",", _cookieJar.GetCookieNamesSnapshot())}] userId={_userId} baseUrl={_endpoint.BaseUrl}");
            System.Diagnostics.Debug.WriteLine($"[API] DeviceId: {_transport.DeviceId}");
            System.Diagnostics.Debug.WriteLine($"[API] HasCookie: {_cookieJar.HasCookie}");
        }

        public bool IsLoggedIn => _cookieJar.HasCookie && _userId > 0;
        public long UserId => _userId;
        public bool IsSecureConnection => _endpoint.IsSecureConnection;
        public bool IsInitialized => _cookieJar.IsInitialized;

        public event EventHandler? SessionChanged;

        public async Task InitializeAsync()
        {
            _cookieJar.LoadFromStore();
            if (_cookieJar.HasCookie)
            {
                _userId = _settings.ContainsKey(UserIdKey) ? _settings.GetInt64(UserIdKey) : 0;
                _log.Write($"InitializeAsync: 恢复登录 userId={_userId} cookieNames=[{string.Join(",", _cookieJar.GetCookieNamesSnapshot())}]");
                if (_userId == 0) await GetUserInfoAsync().ConfigureAwait(false);
                return;
            }

            _log.Write("InitializeAsync: 无 Cookie，走匿名登录");
            await LoginAnonymouslyAsync().ConfigureAwait(false);
        }

        public async Task<bool> LoginAnonymouslyAsync()
        {
            try
            {
                var response = await _transport.GetAsync("/register/anonimous").ConfigureAwait(false);
                if (!response.Success)
                {
                    System.Diagnostics.Debug.WriteLine("[API] Anonymous login failed");
                    return false;
                }

                var result = JsonSerializer.Deserialize<JsonElement>(response.Body);
                if (result.TryGetProperty("code", out var code) && code.GetInt32() == 200)
                {
                    var cookie = result.TryGetProperty("cookie", out var cookieElement)
                        ? cookieElement.GetString() ?? string.Empty
                        : string.Empty;
                    _cookieJar.MergeAndSave(cookie);
                    System.Diagnostics.Debug.WriteLine("[API] Anonymous login success");
                    return true;
                }

                System.Diagnostics.Debug.WriteLine("[API] Anonymous login failed");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] Anonymous Login Error: {ex.Message}");
                return false;
            }
        }

        public async Task<(bool Success, string Message, int Code, string RawResponse)> LoginWithPhoneAsync(string phone, string password)
        {
            var allowInsecure = _settings.GetBoolean(AllowInsecureLoginKey);
            if (!_endpoint.IsSecureConnection && !_endpoint.IsLoopbackServer && !allowInsecure)
                return (false, "远程 HTTP 登录默认被禁止。请改用 HTTPS，或在设置中显式允许不安全的 HTTP 登录。", -1, string.Empty);

            try
            {
                var url = $"/login/cellphone?phone={Uri.EscapeDataString(phone)}&password={Uri.EscapeDataString(password)}";
                var response = await _transport.GetWithoutCookieAsync(url).ConfigureAwait(false);
                var json = string.IsNullOrEmpty(response.Body) ? "{}" : response.Body;
                _log.Write("Phone login request completed without logging credentials.");

                var result = JsonSerializer.Deserialize<JsonElement>(json);
                if (result.TryGetProperty("code", out var code) && code.GetInt32() == 200)
                {
                    var cookie = result.TryGetProperty("cookie", out var cookieElement)
                        ? cookieElement.GetString() ?? string.Empty
                        : string.Empty;
                    if (result.TryGetProperty("account", out var account) &&
                        account.TryGetProperty("id", out var id))
                    {
                        _userId = id.GetInt64();
                    }

                    _cookieJar.MergeAndSave(cookie);
                    _settings.SetInt64(UserIdKey, _userId);
                    SessionChanged?.Invoke(this, EventArgs.Empty);

                    System.Diagnostics.Debug.WriteLine($"[API] Login success, UserId: {_userId}");
                    return (true, "登录成功", 200, json);
                }

                var message = result.TryGetProperty("message", out var msg) ? msg.GetString() ?? "未知错误" : "未知错误";
                var errorCode = result.TryGetProperty("code", out var errCode) ? errCode.GetInt32() : -1;
                System.Diagnostics.Debug.WriteLine($"[API] Login failed: {message} (Code: {errorCode})");
                return (false, message, errorCode, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] Login Error: {ex.Message}");
                return (false, ex.Message, -1, string.Empty);
            }
        }

        public async Task<QrCodePayload?> GenerateQrCodeAsync(CancellationToken cancellationToken = default)
        {
            var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var keyResponse = await _transport.GetWithoutCookieAsync($"/login/qr/key?timestamp={stamp}", cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(keyResponse.Body)) return null;

            var unikey = AuthJsonMapper.TryExtractQrUnikey(keyResponse.Body);
            if (string.IsNullOrEmpty(unikey)) return null;

            var qrResponse = await _transport.GetWithoutCookieAsync(
                $"/login/qr/create?key={Uri.EscapeDataString(unikey)}&qrimg=true&timestamp={stamp}",
                cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(qrResponse.Body)) return null;

            var base64 = AuthJsonMapper.TryExtractQrImageBase64(qrResponse.Body);
            return string.IsNullOrEmpty(base64) ? null : new QrCodePayload(unikey, base64);
        }

        public async Task<ApiResult<QrLoginStatusResult>> CheckQrStatusAsync(string key, CancellationToken cancellationToken = default)
        {
            var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            // noCookie：不携带本机已有的匿名/过期 Cookie，否则扫码后服务端可能返回 502
            var response = await _transport.GetWithoutCookieAsync(
                $"/login/qr/check?key={Uri.EscapeDataString(key)}&noCookie=true&timestamp={stamp}",
                cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrEmpty(response.Body))
            {
                return ApiResult<QrLoginStatusResult>.Fail(
                    response.Result,
                    new QrLoginStatusResult(QrLoginState.Failed, -1, string.Empty, response.Result.DisplayMessage));
            }

            try
            {
                var status = AuthJsonMapper.ParseQrCheckStatus(response.Body);
                return ApiResult<QrLoginStatusResult>.Ok(status);
            }
            catch (Exception ex)
            {
                return ApiResult<QrLoginStatusResult>.Fail(
                    new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message),
                    new QrLoginStatusResult(QrLoginState.Failed, -1, string.Empty, ex.Message));
            }
        }

        public async Task<bool> SetLoginCookieAsync(string cookie)
        {
            if (string.IsNullOrWhiteSpace(cookie)) return false;

            _clearLikedState();
            _cookieJar.MergeAndSave(cookie);

            for (var attempt = 0; attempt < 3; attempt++)
            {
                var infoResult = await GetUserInfoAsync().ConfigureAwait(false);
                if (infoResult.Value != null)
                {
                    _userId = infoResult.Value.UserId;
                    _settings.SetInt64(UserIdKey, _userId);
                    _settings.SetString(NicknameKey, infoResult.Value.Nickname);
                    _settings.SetString(AvatarUrlKey, infoResult.Value.AvatarUrl);
                    SessionChanged?.Invoke(this, EventArgs.Empty);
                    return true;
                }

                if (IsLoggedIn)
                {
                    SessionChanged?.Invoke(this, EventArgs.Empty);
                    return true;
                }

                if (attempt < 2) await Task.Delay(500).ConfigureAwait(false);
            }

            if (IsLoggedIn) SessionChanged?.Invoke(this, EventArgs.Empty);
            return IsLoggedIn;
        }

        public async Task<ApiResult<UserInfo?>> GetUserInfoAsync()
        {
            try
            {
                var accountResp = await _transport.GetAsync("/user/account").ConfigureAwait(false);
                if (!accountResp.Success)
                {
                    return ApiResult<UserInfo?>.Fail(accountResp.Result, null);
                }

                var accountResult = JsonSerializer.Deserialize<JsonElement>(accountResp.Body);
                if (accountResult.TryGetProperty("account", out var account) &&
                    account.TryGetProperty("id", out var idElement))
                {
                    var uid = idElement.GetInt64();
                    _userId = uid;
                    _log.Write($"/user/account: 认证成功 uid={uid} cookieNames=[{string.Join(",", _cookieJar.GetCookieNamesSnapshot())}]");

                    var detailResp = await _transport.GetAsync($"/user/detail?uid={uid}").ConfigureAwait(false);
                    if (!detailResp.Success)
                    {
                        return ApiResult<UserInfo?>.Fail(detailResp.Result, null);
                    }

                    var detailResult = JsonSerializer.Deserialize<JsonElement>(detailResp.Body);
                    if (detailResult.TryGetProperty("profile", out var profile))
                    {
                        var userInfo = new UserInfo
                        {
                            UserId = uid,
                            Nickname = profile.GetProperty("nickname").GetString() ?? string.Empty,
                            AvatarUrl = profile.GetProperty("avatarUrl").GetString() ?? string.Empty,
                            VipType = profile.TryGetProperty("vipType", out var vip) ? vip.GetInt32() : 0
                        };

                        _settings.SetInt64(UserIdKey, uid);
                        _settings.SetString(NicknameKey, userInfo.Nickname);
                        _settings.SetString(AvatarUrlKey, userInfo.AvatarUrl);

                        return ApiResult<UserInfo?>.Ok(userInfo);
                    }
                }

                _log.Write($"/user/account: 未认证（无 account.id）cookieNames=[{string.Join(",", _cookieJar.GetCookieNamesSnapshot())}]");
                return ApiResult<UserInfo?>.Fail(
                    new ApiResult(false, ApiErrorKind.Authentication, "未认证"),
                    null);
            }
            catch (Exception ex)
            {
                _log.Write($"/user/account 异常: {ex.Message}");
                return ApiResult<UserInfo?>.Fail(
                    new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message),
                    null);
            }
        }

        public UserInfo? GetCachedUserInfo()
        {
            if (_settings.ContainsKey(NicknameKey) && _settings.ContainsKey(AvatarUrlKey))
            {
                return new UserInfo
                {
                    UserId = _userId,
                    Nickname = _settings.GetString(NicknameKey) ?? string.Empty,
                    AvatarUrl = _settings.GetString(AvatarUrlKey) ?? string.Empty
                };
            }
            return null;
        }

        public bool GetVipStatus() => _settings.GetBoolean(IsVipKey);

        public async Task<bool> CheckVipStatusAsync()
        {
            try
            {
                var resp = await _transport.GetAsync("/vip/info").ConfigureAwait(false);
                var json = resp.Success ? resp.Body : "{}";
                var isVip = SongUrlPolicy.EvaluateVipStatus(json);

                _settings.SetBoolean(IsVipKey, isVip);
                _clearPrivilegeCache();

                System.Diagnostics.Debug.WriteLine($"[API] VIP Status: {isVip}");
                return isVip;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] CheckVipStatus Error: {ex.Message}");
                return false;
            }
        }

        public void ResetLogin()
        {
            _cookieJar.Clear();
            _settings.Remove(UserIdKey);
            _settings.Remove(IsVipKey);
            _userId = 0;
            _clearPrivilegeCache();
            _clearLikedState();
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
