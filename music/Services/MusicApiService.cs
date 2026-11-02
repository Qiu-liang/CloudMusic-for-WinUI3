using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using music.Models;

namespace music.Services
{
    public class MusicApiService
    {
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _requestSemaphore = new SemaphoreSlim(1, 1);
        // 会话 Cookie 罐（name → value）：UseCookies=false 时请求统一携带由它拼出的
        // Cookie 头，Set-Cookie 响应也回灌到罐里，内容所见即所得、可持久化恢复
        private readonly Dictionary<string, string> _sessionCookies = new Dictionary<string, string>();
        // API 服务器地址只保存在这两个字段里，刻意不写入 HttpClient.BaseAddress：
        // BaseAddress 一旦发出过第一个请求就永久只读（再赋值抛 InvalidOperationException），
        // 所以每个请求都用 _baseUri 现场拼绝对地址，运行期切换地址随时生效
        private string _baseUrl;
        private volatile Uri _baseUri;
        private string _cookie = string.Empty;
        private long _userId = 0;
        private bool _isInitialized = false;
        private string _deviceId = string.Empty;

        public bool IsLoggedIn => !string.IsNullOrEmpty(_cookie) && _userId > 0;
        public long UserId => _userId;

        // 最近一次 GetAsync 是否因网络等原因失败（用于区分"认证失败"与"服务器不可达"，
        // 避免网络抖动时误清有效的本地登录态）
        public bool LastRequestFailed { get; private set; }
        public ApiErrorKind LastError { get; private set; }
        public string LastErrorMessage { get; private set; } = string.Empty;
        public bool IsSecureConnection => _baseUri.Scheme == Uri.UriSchemeHttps;
        private bool IsLoopbackServer => _baseUri.IsLoopback;
        public static string DefaultServerAddress => "http://localhost:3000";

        public MusicApiService() : this(null, ApplicationData.Current.LocalSettings) { }

        internal MusicApiService(HttpMessageHandler? handler, ApplicationDataContainer settings)
        {
            _baseUrl = TryNormalizeBaseUrl(settings.Values["ServerAddress"]?.ToString(), out var storedBaseUrl)
                ? storedBaseUrl
                : DefaultServerAddress;
            _baseUri = new Uri(_baseUrl, UriKind.Absolute);
            _deviceId = settings.Values["DeviceId"]?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(_deviceId))
            {
                _deviceId = Guid.NewGuid().ToString("N");
                settings.Values["DeviceId"] = _deviceId;
            }

            _cookie = CredentialStorage.Load();
            _userId = settings.Values.ContainsKey("UserId") ? Convert.ToInt64(settings.Values["UserId"]) : 0;
            if (!string.IsNullOrEmpty(_cookie))
            {
                _isInitialized = true;
            }
            
            // 关闭 HttpClient 自动 Cookie 管理：其内存容器会覆盖手动 Cookie 头且
            // 随进程结束清空。改为完全手动管理——请求统一携带 _sessionCookies
            // 拼出的 Cookie 头，Set-Cookie 响应回灌罐中，行为完全可预测
            handler ??= new SocketsHttpHandler { UseCookies = false };
            _httpClient = new HttpClient(handler);
            _httpClient.Timeout = TimeSpan.FromSeconds(30);

            // 从磁盘恢复的登录 Cookie 解析进罐，重启后登录态随请求发出
            LoadCookieString(_cookie);
            DebugLog($"ctor: cookieNames=[{string.Join(",", _sessionCookies.Keys)}] userId={_userId} baseUrl={_baseUrl}");
            
            // 设置固定的User-Agent
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            
            System.Diagnostics.Debug.WriteLine($"[API] DeviceId: {_deviceId}");
            System.Diagnostics.Debug.WriteLine($"[API] HasCookie: {!string.IsNullOrEmpty(_cookie)}");
        }

        // 运行期切换 API 地址：只更新自己的字段并落盘，不碰 HttpClient 的只读属性，
        // 因此无论此刻有没有请求在飞都可以安全调用，也不会再抛 InvalidOperationException
        public void UpdateBaseUrl(string url)
        {
            if (!TryNormalizeBaseUrl(url, out var normalized))
                throw new ArgumentException("服务器地址必须是有效的 HTTP 或 HTTPS 绝对地址。", nameof(url));

            // 先写 Uri 再写字符串：请求线程只读 _baseUri，读到的永远是完整的绝对地址
            _baseUri = new Uri(normalized, UriKind.Absolute);
            _baseUrl = normalized;

            ApplicationData.Current.LocalSettings.Values["ServerAddress"] = normalized;
            DebugLog($"baseUrl 已切换为 {normalized}");
        }

        // 规范化服务器地址：补 http:// 前缀、只保留 scheme://host[:port][/path]、去掉尾部斜杠；
        // 无法解析或协议非法时返回 false，由调用方决定回退还是报错
        private static bool TryNormalizeBaseUrl(string? url, out string normalized)
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

        // 用当前基地址拼出请求的绝对地址（语义与原来的 HttpClient.BaseAddress 相对解析一致）
        private static Uri CreateRequestUri(Uri baseUri, string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var absolute) &&
                (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
            {
                return absolute;
            }

            return new Uri(baseUri, url);
        }

        // 解析 "name=value; name2=value2" 形式的 Cookie 字符串到会话罐（整体替换）
        private void LoadCookieString(string cookie)
        {
            _sessionCookies.Clear();
            if (string.IsNullOrEmpty(cookie)) return;

            foreach (var pair in cookie.Split(';'))
            {
                var idx = pair.IndexOf('=');
                if (idx <= 0) continue;
                var name = pair[..idx].Trim();
                var value = pair[(idx + 1)..].Trim();
                if (name.Length > 0) _sessionCookies[name] = value;
            }

            RebuildCookieString();
        }

        // 合并 Cookie 字符串到会话罐（不清空罐，同名覆盖）：登录成功时用，
        // 保留匿名设备票据（MUSIC_A_T 等），否则每次扫码都会被服务器视为新设备
        private void MergeCookieString(string cookie)
        {
            if (string.IsNullOrEmpty(cookie)) return;

            foreach (var pair in cookie.Split(';'))
            {
                var idx = pair.IndexOf('=');
                if (idx <= 0) continue;
                var name = pair[..idx].Trim();
                var value = pair[(idx + 1)..].Trim();
                if (name.Length > 0) _sessionCookies[name] = value;
            }

            RebuildCookieString();
        }

        private void RebuildCookieString()
        {
            _cookie = string.Join("; ", _sessionCookies.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        }

        // 处理单个 Set-Cookie 响应头（取 name=value 部分；value 为空视为删除该 Cookie）
        private void MergeSetCookie(string setCookieValue)
        {
            var firstSegment = setCookieValue.Split(';')[0];
            var idx = firstSegment.IndexOf('=');
            if (idx <= 0) return;

            var name = firstSegment[..idx].Trim();
            var value = firstSegment[(idx + 1)..].Trim();
            if (name.Length == 0) return;

            if (value.Length == 0) _sessionCookies.Remove(name);
            else _sessionCookies[name] = value;

            RebuildCookieString();
        }

        // 诊断日志：只记录 Cookie 名与结果，不记录值（避免泄露登录凭据）
        private void DebugLog(string message)
        {
            try
            {
                var path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "api_debug.log");
                File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {message}\r\n");
                System.Diagnostics.Debug.WriteLine($"[API] {message}");
            }
            catch { /* 日志失败不影响主流程 */ }
        }

        public async void SetLoginCookie(string cookie)
        {
            // 合并而非替换：保留匿名设备票据，避免每次扫码被服务器视为新设备
            MergeCookieString(cookie);
            _isInitialized = true;

            var settings = ApplicationData.Current.LocalSettings;
            CredentialStorage.Save(_cookie);

            // 获取用户信息
            var userInfo = await GetUserInfoAsync();
            if (userInfo != null)
            {
                _userId = userInfo.UserId;
                settings.Values["UserId"] = _userId;
                settings.Values["Nickname"] = userInfo.Nickname;
                settings.Values["AvatarUrl"] = userInfo.AvatarUrl;
            }
        }

        public async Task<string> GetAsync(string url, CancellationToken cancellationToken = default)
        {
            var result = await SendWithResultAsync(HttpMethod.Get, url, null, cancellationToken);
            return result.Success ? result.Body : "{}";
        }

        internal async Task<(bool Success, string Body)> SendWithResultAsync(
            HttpMethod method, string url, HttpContent? content, CancellationToken cancellationToken = default)
        {
            await _requestSemaphore.WaitAsync(cancellationToken);
            try
            {
                var baseUri = _baseUri; // 只捕获一次，切换地址时不会把一次请求劈成两半
                var separator = url.Contains("?") ? "&" : "?";
                var fullUrl = $"{url}{separator}deviceId={Uri.EscapeDataString(_deviceId)}";
                using var request = new HttpRequestMessage(method, CreateRequestUri(baseUri, fullUrl)) { Content = content };
                if (!string.IsNullOrEmpty(_cookie)) request.Headers.Add("Cookie", _cookie);

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                LastRequestFailed = false;
                LastError = ApiErrorKind.None;
                LastErrorMessage = string.Empty;
                if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
                    UpdateCookiesFromResponse(cookies);

                var result = ApiResult.FromHttpResponse((int)response.StatusCode, body);
                if (!result.Success)
                {
                    LastError = result.Error;
                    LastErrorMessage = result.DisplayMessage;
                    DebugLog($"{method} {request.RequestUri?.AbsolutePath}: {result.Error} code={result.Code}");
                }
                return (result.Success, body);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LastRequestFailed = true;
                LastError = ApiErrorKind.Network;
                LastErrorMessage = "无法连接服务器，请检查 API 地址和网络。";
                System.Diagnostics.Debug.WriteLine($"[API] Error: {ex.Message}");
                return (false, string.Empty);
            }
            finally
            {
                _requestSemaphore.Release();
            }
        }

        internal async Task<ApiResult> SendForApiResultAsync(
            HttpMethod method, string url, HttpContent content, CancellationToken cancellationToken = default)
        {
            var result = await SendWithResultAsync(method, url, content, cancellationToken);
            if (result.Success) return new ApiResult(true, ApiErrorKind.None, string.Empty, 200);
            return LastRequestFailed ? ApiResult.NetworkError() : new ApiResult(false, LastError, LastErrorMessage);
        }

        public async Task<ApiResult> UploadCloudSongAsync(StorageFile file, CancellationToken cancellationToken = default)
        {
            if (!IsLoggedIn) return new ApiResult(false, ApiErrorKind.Authentication, "请先登录后再上传。", 301);
            var stream = await file.OpenStreamForReadAsync();
            await using (stream.ConfigureAwait(false))
            {
                using var content = new StreamContent(stream);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                using var formData = new MultipartFormDataContent();
                formData.Add(content, "songFile", file.Name);
                var sent = await SendWithResultAsync(HttpMethod.Post, "/cloud", formData, cancellationToken);
                if (!sent.Success)
                    return LastRequestFailed ? ApiResult.NetworkError() : new ApiResult(false, LastError, LastErrorMessage);
                return ApiResult.FromHttpResponse(200, sent.Body, requireBusinessCode: true);
            }
        }

        private void UpdateCookiesFromResponse(IEnumerable<string> setCookies)
        {
            foreach (var cookie in setCookies)
            {
                var firstSegment = cookie.Split(';')[0];
                var name = firstSegment.Split('=')[0].Trim();
                if (name == "MUSIC_U" || name == "__csrf" || name.StartsWith("MUSIC_"))
                    DebugLog($"Set-Cookie: {name} 已更新");
                MergeSetCookie(cookie);
            }

            if (_isInitialized && !string.IsNullOrEmpty(_cookie)) CredentialStorage.Save(_cookie);
        }

        public async Task<string> GetWithoutCookieAsync(string url, CancellationToken cancellationToken = default)
        {
            var result = await SendWithResultAsync(HttpMethod.Get, url, null, cancellationToken);
            return result.Success ? result.Body : "{}";
        }

        public async Task<bool> LoginAnonymouslyAsync()
        {
            try
            {
                var json = await GetAsync("/register/anonimous");
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                if (result.TryGetProperty("code", out var code) && code.GetInt32() == 200)
                {
                    if (result.TryGetProperty("cookie", out var cookieElement))
                    {
                        // 合并而非替换，保留匿名设备票据
                        MergeCookieString(cookieElement.GetString() ?? string.Empty);
                    }

                    var settings = ApplicationData.Current.LocalSettings;
                    CredentialStorage.Save(_cookie);
                    _isInitialized = true;

                    System.Diagnostics.Debug.WriteLine($"[API] Anonymous login success");
                    return true;
                }

                System.Diagnostics.Debug.WriteLine($"[API] Anonymous login failed");
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
            var allowInsecure = ApplicationData.Current.LocalSettings.Values["AllowInsecureLogin"] as bool? ?? false;
            if (!IsSecureConnection && !IsLoopbackServer && !allowInsecure)
                return (false, "远程 HTTP 登录默认被禁止。请改用 HTTPS，或在设置中显式允许不安全的 HTTP 登录。", -1, string.Empty);

            try
            {
                var url = $"/login/cellphone?phone={Uri.EscapeDataString(phone)}&password={Uri.EscapeDataString(password)}";
                var json = await GetWithoutCookieAsync(url);
                DebugLog("Phone login request completed without logging credentials.");
                
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                if (result.TryGetProperty("code", out var code) && code.GetInt32() == 200)
                {
                    if (result.TryGetProperty("cookie", out var cookieElement))
                    {
                        // 合并而非替换，保留匿名设备票据
                        MergeCookieString(cookieElement.GetString() ?? string.Empty);
                    }
                    if (result.TryGetProperty("account", out var account) &&
                        account.TryGetProperty("id", out var id))
                    {
                        _userId = id.GetInt64();
                    }

                    var settings = ApplicationData.Current.LocalSettings;
                    CredentialStorage.Save(_cookie);
                    settings.Values["UserId"] = _userId;
                    _isInitialized = true;

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

        public async Task<UserInfo?> GetUserInfoAsync()
        {
            try
            {
                var accountJson = await GetAsync("/user/account");
                var accountResult = JsonSerializer.Deserialize<JsonElement>(accountJson);

                if (accountResult.TryGetProperty("account", out var account) &&
                    account.TryGetProperty("id", out var idElement))
                {
                    var uid = idElement.GetInt64();
                    _userId = uid;
                    DebugLog($"/user/account: 认证成功 uid={uid} cookieNames=[{string.Join(",", _sessionCookies.Keys)}]");

                    var detailJson = await GetAsync($"/user/detail?uid={uid}");
                    var detailResult = JsonSerializer.Deserialize<JsonElement>(detailJson);

                    if (detailResult.TryGetProperty("profile", out var profile))
                    {
                        var userInfo = new UserInfo
                        {
                            UserId = uid,
                            Nickname = profile.GetProperty("nickname").GetString() ?? string.Empty,
                            AvatarUrl = profile.GetProperty("avatarUrl").GetString() ?? string.Empty,
                            VipType = profile.TryGetProperty("vipType", out var vip) ? vip.GetInt32() : 0
                        };

                        var settings = ApplicationData.Current.LocalSettings;
                        settings.Values["UserId"] = uid;
                        settings.Values["Nickname"] = userInfo.Nickname;
                        settings.Values["AvatarUrl"] = userInfo.AvatarUrl;

                        return userInfo;
                    }
                }

                DebugLog($"/user/account: 未认证（无 account.id）cookieNames=[{string.Join(",", _sessionCookies.Keys)}]");
                return null;
            }
            catch (Exception ex)
            {
                DebugLog($"/user/account 异常: {ex.Message}");
                return null;
            }
        }

        public UserInfo? GetCachedUserInfo()
        {
            var settings = ApplicationData.Current.LocalSettings;
            if (settings.Values.ContainsKey("Nickname") && settings.Values.ContainsKey("AvatarUrl"))
            {
                return new UserInfo
                {
                    UserId = _userId,
                    Nickname = settings.Values["Nickname"].ToString() ?? string.Empty,
                    AvatarUrl = settings.Values["AvatarUrl"].ToString() ?? string.Empty
                };
            }
            return null;
        }

        public bool GetVipStatus()
        {
            var settings = ApplicationData.Current.LocalSettings;
            return settings.Values.ContainsKey("IsVip") && (bool)settings.Values["IsVip"];
        }

        public async Task<bool> CheckVipStatusAsync()
        {
            try
            {
                var json = await GetAsync("/vip/info");
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                var isVip = false;
                if (result.TryGetProperty("data", out var data))
                {
                    if (data.TryGetProperty("redVipLevel", out var redVipLevel))
                    {
                        isVip = redVipLevel.GetInt32() > 0;
                    }
                }

                var settings = ApplicationData.Current.LocalSettings;
                settings.Values["IsVip"] = isVip;

                System.Diagnostics.Debug.WriteLine($"[API] VIP Status: {isVip}");
                return isVip;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] CheckVipStatus Error: {ex.Message}");
                return false;
            }
        }

        public async Task InitializeAsync()
        {
            var settings = ApplicationData.Current.LocalSettings;
            _cookie = CredentialStorage.Load();
            LoadCookieString(_cookie);
            if (!string.IsNullOrEmpty(_cookie))
            {
                _userId = settings.Values.ContainsKey("UserId") ? Convert.ToInt64(settings.Values["UserId"]) : 0;
                _isInitialized = true;
                DebugLog($"InitializeAsync: 恢复登录 userId={_userId} cookieNames=[{string.Join(",", _sessionCookies.Keys)}]");
                if (_userId == 0) await GetUserInfoAsync();
                return;
            }

            DebugLog("InitializeAsync: 无 Cookie，走匿名登录");
            await LoginAnonymouslyAsync();
        }

        public void ResetLogin()
        {
            var settings = ApplicationData.Current.LocalSettings;
            CredentialStorage.Clear();
            settings.Values.Remove("UserId");
            settings.Values.Remove("IsVip");
            _cookie = string.Empty;
            _sessionCookies.Clear();
            _userId = 0;
            _isInitialized = false;
        }

        private Song ParseSongFromJson(JsonElement item)
        {
            var song = new Song
            {
                Id = item.GetProperty("id").GetInt64().ToString(),
                Name = item.GetProperty("name").GetString() ?? string.Empty,
                Duration = item.GetProperty("dt").GetInt64()
            };

            // 解析VIP信息
            if (item.TryGetProperty("fee", out var fee))
            {
                song.Fee = fee.GetInt32();
                System.Diagnostics.Debug.WriteLine($"[API] Song '{song.Name}' fee={song.Fee}");
            }
            if (item.TryGetProperty("vipFlag", out var vipFlag))
                song.VipFlag = vipFlag.GetBoolean();
            if (item.TryGetProperty("vipPlayFlag", out var vipPlayFlag))
                song.VipPlayFlag = vipPlayFlag.GetBoolean();
            if (item.TryGetProperty("payPlayFlag", out var payPlayFlag))
                song.PayPlayFlag = payPlayFlag.GetBoolean();

            // 设置IsVip和IsPaid
            song.IsVip = song.Fee == 1 || song.VipFlag || song.VipPlayFlag;
            song.IsPaid = song.Fee == 4 || song.PayPlayFlag;

            if (song.IsVip)
                System.Diagnostics.Debug.WriteLine($"[API] Song '{song.Name}' is VIP");

            if (item.TryGetProperty("ar", out var artists))
            {
                foreach (var artist in artists.EnumerateArray())
                {
                    song.Artists.Add(new Artist
                    {
                        Id = artist.GetProperty("id").GetInt64().ToString(),
                        Name = artist.GetProperty("name").GetString() ?? string.Empty
                    });
                }
            }

            if (item.TryGetProperty("al", out var album))
            {
                song.Album = new Album
                {
                    Id = album.GetProperty("id").GetInt64().ToString(),
                    Name = album.GetProperty("name").GetString() ?? string.Empty
                };
                if (album.TryGetProperty("picUrl", out var picUrl))
                {
                    song.CoverImgUrl = picUrl.GetString() ?? string.Empty;
                }
            }

            return song;
        }

        public async Task<List<Song>> GetDailyRecommendSongsAsync(int limit = 30)
        {
            if (!_isInitialized)
            {
                await InitializeAsync();
            }

            try
            {
                var json = await GetAsync($"/recommend/songs?limit={Math.Max(0, limit)}");
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                var songs = new List<Song>();
                if (result.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("dailySongs", out var dailySongs))
                {
                    foreach (var item in dailySongs.EnumerateArray().Take(Math.Max(0, limit)))
                    {
                        songs.Add(ParseSongFromJson(item));
                    }
                }

                return songs;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] Recommend Error: {ex.Message}");
                return new List<Song>();
            }
        }

        public async Task<List<PlaylistInfo>> GetUserPlaylistsAsync(long uid, bool bypassCache = false)
        {
            try
            {
                var url = $"/user/playlist?uid={uid}";
                if (bypassCache)
                {
                    url += $"&timestamp={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                }
                var json = await GetAsync(url);
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                var playlists = new List<PlaylistInfo>();
                if (result.TryGetProperty("playlist", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        playlists.Add(new PlaylistInfo
                        {
                            Id = item.GetProperty("id").GetInt64().ToString(),
                            Name = item.GetProperty("name").GetString() ?? string.Empty,
                            CoverImgUrl = item.GetProperty("coverImgUrl").GetString() ?? string.Empty,
                            TrackCount = item.GetProperty("trackCount").GetInt32(),
                            PlayCount = item.TryGetProperty("playCount", out var pc) ? pc.GetInt64() : 0,
                            CreatorName = item.GetProperty("creator").GetProperty("nickname").GetString() ?? string.Empty,
                            CreatorId = item.GetProperty("creator").GetProperty("userId").GetInt64()
                        });
                    }
                }

                return playlists;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetPlaylists Error: {ex.Message}");
                return new List<PlaylistInfo>();
            }
        }

        public async Task<List<Song>> GetPlaylistDetailAsync(string playlistId)
        {
            try
            {
                var json = await GetAsync($"/playlist/track/all?id={playlistId}");
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                var songs = new List<Song>();
                if (result.TryGetProperty("songs", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        songs.Add(ParseSongFromJson(item));
                    }
                }

                return songs;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetPlaylistDetail Error: {ex.Message}");
                return new List<Song>();
            }
        }

        public async Task<PlaylistDetailInfo?> GetPlaylistInfoAsync(string playlistId)
        {
            try
            {
                var json = await GetAsync($"/playlist/detail?id={playlistId}");
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                if (result.TryGetProperty("playlist", out var playlist))
                {
                    var info = new PlaylistDetailInfo
                    {
                        Id = playlist.GetProperty("id").GetInt64().ToString(),
                        Name = playlist.GetProperty("name").GetString() ?? string.Empty,
                        CoverImgUrl = playlist.TryGetProperty("coverImgUrl", out var coverImgUrl) ? coverImgUrl.GetString() ?? string.Empty : string.Empty,
                        Description = playlist.TryGetProperty("description", out var desc) ? desc.GetString() ?? string.Empty : string.Empty,
                        TrackCount = playlist.TryGetProperty("trackCount", out var trackCount) ? trackCount.GetInt32() : 0,
                        PlayCount = playlist.TryGetProperty("playCount", out var playCount) ? playCount.GetInt64() : 0,
                        SubscribedCount = playlist.TryGetProperty("subscribedCount", out var subscribedCount) ? subscribedCount.GetInt64() : 0,
                        ShareCount = playlist.TryGetProperty("shareCount", out var shareCount) ? shareCount.GetInt64() : 0,
                        CommentCount = playlist.TryGetProperty("commentCount", out var commentCount) ? commentCount.GetInt64() : 0,
                        Privacy = playlist.TryGetProperty("privacy", out var privacy) ? privacy.GetInt32() : 0,
                        Subscribed = playlist.TryGetProperty("subscribed", out var subscribed) && subscribed.GetBoolean()
                    };

                    if (playlist.TryGetProperty("creator", out var creator))
                    {
                        info.CreatorName = creator.TryGetProperty("nickname", out var nickname) ? nickname.GetString() ?? string.Empty : string.Empty;
                        info.CreatorAvatarUrl = creator.TryGetProperty("avatarUrl", out var avatarUrl) ? avatarUrl.GetString() ?? string.Empty : string.Empty;
                        info.CreatorId = creator.TryGetProperty("userId", out var creatorId) ? creatorId.GetInt64() : 0;
                    }

                    if (playlist.TryGetProperty("tags", out var tags))
                    {
                        foreach (var tag in tags.EnumerateArray())
                        {
                            info.Tags.Add(tag.GetString() ?? string.Empty);
                        }
                    }

                    return info;
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetPlaylistInfo Error: {ex.Message}");
                return null;
            }
        }

        public async Task<AlbumDetailInfo?> GetAlbumInfoAsync(string albumId)
        {
            try
            {
                var json = await GetAsync($"/album?id={albumId}");
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                if (result.TryGetProperty("album", out var album))
                {
                    var info = new AlbumDetailInfo
                    {
                        Id = album.GetProperty("id").GetInt64().ToString(),
                        Name = album.GetProperty("name").GetString() ?? string.Empty,
                        PicUrl = album.TryGetProperty("picUrl", out var picUrl) ? picUrl.GetString() ?? string.Empty : string.Empty,
                        Description = album.TryGetProperty("description", out var desc) ? desc.GetString() ?? string.Empty : string.Empty,
                        Size = album.TryGetProperty("size", out var size) ? size.GetInt32() : 0,
                        SubType = album.TryGetProperty("subType", out var subType) ? subType.GetString() ?? string.Empty : string.Empty,
                        Company = album.TryGetProperty("company", out var company) ? company.GetString() ?? string.Empty : string.Empty
                    };

                    if (album.TryGetProperty("artist", out var artist))
                    {
                        info.ArtistName = artist.TryGetProperty("name", out var artistName) ? artistName.GetString() ?? string.Empty : string.Empty;
                        info.ArtistId = artist.TryGetProperty("id", out var artistId) ? artistId.GetInt64().ToString() : string.Empty;
                    }

                    if (album.TryGetProperty("publishTime", out var publishTime))
                    {
                        var timestamp = publishTime.GetInt64();
                        if (timestamp > 0)
                        {
                            var dateTime = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).LocalDateTime;
                            info.PublishTime = dateTime.ToString("yyyy-MM-dd");
                        }
                    }

                    return info;
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetAlbumInfo Error: {ex.Message}");
                return null;
            }
        }

        public async Task<string> GetSongUrlAsync(string songId, CancellationToken cancellationToken = default)
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                var quality = settings.Values["AudioQuality"]?.ToString() ?? "standard";
                
                // 使用新版API获取歌曲URL
                var json = await GetAsync($"/song/url/v1?id={songId}&level={quality}", cancellationToken);
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                if (result.TryGetProperty("data", out var data) &&
                    data.ValueKind == JsonValueKind.Array)
                {
                    var first = data[0];
                    if (first.TryGetProperty("url", out var url) && url.ValueKind != JsonValueKind.Null)
                    {
                        return url.GetString() ?? string.Empty;
                    }
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetSongUrl Error: {ex.Message}");
                return string.Empty;
            }
        }

        public async Task<ApiResult> DownloadSongAsync(Song song, CancellationToken cancellationToken = default)
        {
            var url = await GetSongUrlAsync(song.Id, cancellationToken);
            if (string.IsNullOrEmpty(url))
            {
                LastError = ApiErrorKind.Business;
                LastErrorMessage = "当前音质没有可用的音频地址，请尝试切换音质或确认账号权限。";
                return new ApiResult(false, LastError, LastErrorMessage);
            }

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    return new ApiResult(false, ApiErrorKind.Http, $"下载服务器返回 HTTP {(int)response.StatusCode}。", (int)response.StatusCode);

                var folder = Path.Combine(ApplicationData.Current.LocalFolder.Path, "Downloads");
                Directory.CreateDirectory(folder);
                var invalid = Path.GetInvalidFileNameChars();
                var baseName = new string($"{song.ArtistNames} - {song.Name}".Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
                if (string.IsNullOrWhiteSpace(baseName)) baseName = "download";
                var extension = Path.GetExtension(new Uri(url).AbsolutePath);
                if (string.IsNullOrEmpty(extension) || extension.Length > 5) extension = ".mp3";
                var destination = Path.Combine(folder, baseName + extension);
                var temporary = destination + ".download";
                await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var target = File.Create(temporary))
                    await source.CopyToAsync(target, cancellationToken);
                File.Move(temporary, destination, true);
                return new ApiResult(true, ApiErrorKind.None, destination);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] DownloadSong Error: {ex.Message}");
                return ApiResult.NetworkError();
            }
        }

        public async Task<List<SearchSuggestion>> GetSearchSuggestionsAsync(string keywords, CancellationToken cancellationToken = default)
        {
            try
            {
                var json = await GetAsync($"/search/suggest?keywords={Uri.EscapeDataString(keywords)}", cancellationToken);
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                var suggestions = new List<SearchSuggestion>();
                if (result.TryGetProperty("result", out var searchResult))
                {
                    if (searchResult.TryGetProperty("songs", out var songs))
                    {
                        foreach (var song in songs.EnumerateArray())
                        {
                            suggestions.Add(new SearchSuggestion
                            {
                                Id = song.GetProperty("id").GetInt64().ToString(),
                                Name = song.GetProperty("name").GetString() ?? string.Empty,
                                Type = "song",
                                Artist = song.TryGetProperty("artists", out var artists) && artists.GetArrayLength() > 0
                                    ? artists[0].GetProperty("name").GetString() ?? string.Empty
                                    : string.Empty
                            });
                        }
                    }

                    if (searchResult.TryGetProperty("artists", out var artistsList))
                    {
                        foreach (var artist in artistsList.EnumerateArray())
                        {
                            suggestions.Add(new SearchSuggestion
                            {
                                Id = artist.GetProperty("id").GetInt64().ToString(),
                                Name = artist.GetProperty("name").GetString() ?? string.Empty,
                                Type = "artist"
                            });
                        }
                    }

                    if (searchResult.TryGetProperty("playlists", out var playlists))
                    {
                        foreach (var playlist in playlists.EnumerateArray())
                        {
                            suggestions.Add(new SearchSuggestion
                            {
                                Id = playlist.GetProperty("id").GetInt64().ToString(),
                                Name = playlist.GetProperty("name").GetString() ?? string.Empty,
                                Type = "playlist"
                            });
                        }
                    }
                }

                return suggestions;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] SearchSuggestions Error: {ex.Message}");
                return new List<SearchSuggestion>();
            }
        }

        public async Task<List<Song>> SearchSongsAsync(string keywords, int limit = 30, int offset = 0)
        {
            try
            {
                var json = await GetAsync($"/cloudsearch?keywords={Uri.EscapeDataString(keywords)}&limit={limit}&offset={offset}");
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                var songs = new List<Song>();
                if (result.TryGetProperty("result", out var searchResult) &&
                    searchResult.TryGetProperty("songs", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        songs.Add(ParseSongFromJson(item));
                    }
                }

                return songs;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] Search Error: {ex.Message}");
                return new List<Song>();
            }
        }

        public async Task<List<SearchArtistInfo>> SearchArtistsAsync(string keywords, int limit = 10)
        {
            try
            {
                var json = await GetAsync($"/cloudsearch?keywords={Uri.EscapeDataString(keywords)}&type=100&limit={limit}");
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                var artists = new List<SearchArtistInfo>();
                if (result.TryGetProperty("result", out var searchResult) &&
                    searchResult.TryGetProperty("artists", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        artists.Add(new SearchArtistInfo
                        {
                            Id = item.GetProperty("id").GetInt64().ToString(),
                            Name = item.GetProperty("name").GetString() ?? string.Empty,
                            PicUrl = item.TryGetProperty("picUrl", out var picUrl) ? picUrl.GetString() ?? string.Empty : string.Empty,
                            SongCount = item.TryGetProperty("musicSize", out var musicSize) ? musicSize.GetInt32() : 0
                        });
                    }
                }

                return artists;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] SearchArtists Error: {ex.Message}");
                return new List<SearchArtistInfo>();
            }
        }

        public async Task<List<RecommendedPlaylist>> SearchPlaylistsAsync(string keywords, int limit = 10)
        {
            try
            {
                var json = await GetAsync($"/cloudsearch?keywords={Uri.EscapeDataString(keywords)}&type=1000&limit={limit}");
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                var playlists = new List<RecommendedPlaylist>();
                if (result.TryGetProperty("result", out var searchResult) &&
                    searchResult.TryGetProperty("playlists", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        playlists.Add(new RecommendedPlaylist
                        {
                            Id = item.GetProperty("id").GetInt64().ToString(),
                            Name = item.GetProperty("name").GetString() ?? string.Empty,
                            PicUrl = item.TryGetProperty("coverImgUrl", out var coverImgUrl) ? coverImgUrl.GetString() ?? string.Empty : string.Empty,
                            PlayCount = item.TryGetProperty("playCount", out var playCount) ? playCount.GetInt64() : 0,
                            TrackCount = item.TryGetProperty("trackCount", out var trackCount) ? trackCount.GetInt32() : 0
                        });
                    }
                }

                return playlists;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] SearchPlaylists Error: {ex.Message}");
                return new List<RecommendedPlaylist>();
            }
        }

        public async Task<List<SearchAlbumInfo>> SearchAlbumsAsync(string keywords, int limit = 10)
        {
            try
            {
                var json = await GetAsync($"/cloudsearch?keywords={Uri.EscapeDataString(keywords)}&type=10&limit={limit}");
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                var albums = new List<SearchAlbumInfo>();
                if (result.TryGetProperty("result", out var searchResult) &&
                    searchResult.TryGetProperty("albums", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        var album = new SearchAlbumInfo
                        {
                            Id = item.GetProperty("id").GetInt64().ToString(),
                            Name = item.GetProperty("name").GetString() ?? string.Empty,
                            PicUrl = item.TryGetProperty("picUrl", out var picUrl) ? picUrl.GetString() ?? string.Empty : string.Empty,
                            Size = item.TryGetProperty("size", out var size) ? size.GetInt32() : 0
                        };

                        if (item.TryGetProperty("artist", out var artist))
                        {
                            album.ArtistName = artist.TryGetProperty("name", out var artistName) ? artistName.GetString() ?? string.Empty : string.Empty;
                        }

                        if (item.TryGetProperty("publishTime", out var publishTime))
                        {
                            var timestamp = publishTime.GetInt64();
                            if (timestamp > 0)
                            {
                                var dateTime = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).LocalDateTime;
                                album.PublishTime = dateTime.ToString("yyyy-MM-dd");
                            }
                        }

                        albums.Add(album);
                    }
                }

                return albums;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] SearchAlbums Error: {ex.Message}");
                return new List<SearchAlbumInfo>();
            }
        }

        public async Task<LyricInfo?> GetLyricsAsync(string songId, CancellationToken cancellationToken = default)
        {
            try
            {
                var json = await GetAsync($"/lyric?id={songId}", cancellationToken);
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                var lyricInfo = new LyricInfo();

                if (result.TryGetProperty("lrc", out var lrc) &&
                    lrc.TryGetProperty("lyric", out var lyric))
                {
                    lyricInfo.LrcLyric = lyric.GetString() ?? string.Empty;
                }

                if (result.TryGetProperty("tlyric", out var tlyric) &&
                    tlyric.TryGetProperty("lyric", out var tLyric))
                {
                    lyricInfo.TranslatedLyric = tLyric.GetString() ?? string.Empty;
                }

                return lyricInfo;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetLyrics Error: {ex.Message}");
                return null;
            }
        }

        public async Task<List<string>> GetLikedSongIdsAsync()
        {
            if (!IsLoggedIn)
            {
                LastError = ApiErrorKind.Authentication;
                LastErrorMessage = "请先登录。";
                return new List<string>();
            }

            try
            {
                var json = await GetAsync($"/likelist?uid={_userId}");
                var result = JsonSerializer.Deserialize<JsonElement>(json);
                var ids = new List<string>();
                if (result.TryGetProperty("ids", out var idsArray))
                {
                    foreach (var id in idsArray.EnumerateArray())
                        ids.Add(id.GetInt64().ToString());
                }
                return ids;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetLikedSongIds Error: {ex.Message}");
                return new List<string>();
            }
        }

        public async Task<List<Song>> GetLikedSongsAsync()
        {
            try
            {
                var ids = await GetLikedSongIdsAsync();
                if (ids.Count == 0) return new List<Song>();

                var songsById = new Dictionary<string, Song>();
                foreach (var batch in ApiBatching.Chunk(ids, 200))
                {
                    var json = await GetAsync($"/song/detail?ids={string.Join(",", batch)}");
                    if (LastError != ApiErrorKind.None) return new List<Song>();
                    var result = JsonSerializer.Deserialize<JsonElement>(json);
                    if (result.TryGetProperty("songs", out var items))
                    {
                        foreach (var item in items.EnumerateArray())
                        {
                            var song = ParseSongFromJson(item);
                            song.Liked = true;
                            songsById[song.Id] = song;
                        }
                    }
                }

                return ids.Distinct().Where(songsById.ContainsKey).Select(id => songsById[id]).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetLikedSongs Error: {ex.Message}");
                return new List<Song>();
            }
        }

        public async Task<List<Song>> GetIntelligenceSongsAsync()
        {
            if (!IsLoggedIn)
            {
                LastError = ApiErrorKind.Authentication;
                LastErrorMessage = "请先登录。";
                return new List<Song>();
            }

            try
            {
                var likedIds = await GetLikedSongIdsAsync();
                if (likedIds.Count == 0) return new List<Song>();
                var seed = likedIds[0];
                var json = await GetAsync($"/playmode/intelligence/list?id={seed}&sid={seed}");
                var result = JsonSerializer.Deserialize<JsonElement>(json);
                var songs = new List<Song>();
                if (result.TryGetProperty("data", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        var songElement = item.TryGetProperty("songInfo", out var songInfo) ? songInfo : item;
                        if (songElement.TryGetProperty("id", out _) && songElement.TryGetProperty("name", out _))
                            songs.Add(ParseSongFromJson(songElement));
                    }
                }
                return songs;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetIntelligenceSongs Error: {ex.Message}");
                return new List<Song>();
            }
        }

        public async Task<bool> LikeSongAsync(string songId, bool like = true)
        {
            try
            {
                var json = await GetAsync($"/like?id={songId}&like={like.ToString().ToLower()}");
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                if (result.TryGetProperty("code", out var code) && code.GetInt32() == 200)
                {
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] LikeSong Error: {ex.Message}");
                return false;
            }
        }

        public async Task<List<Song>> GetArtistSongsAsync(string artistId, int limit = 50)
        {
            try
            {
                var json = await GetAsync($"/artist/songs?id={artistId}&limit={limit}");
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                var songs = new List<Song>();
                if (result.TryGetProperty("songs", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        songs.Add(ParseSongFromJson(item));
                    }
                }

                return songs;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetArtistSongs Error: {ex.Message}");
                return new List<Song>();
            }
        }

        public async Task<List<Song>> GetAlbumSongsAsync(string albumId)
        {
            try
            {
                var json = await GetAsync($"/album?id={albumId}");
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                var songs = new List<Song>();
                if (result.TryGetProperty("songs", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        songs.Add(ParseSongFromJson(item));
                    }
                }

                return songs;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetAlbumSongs Error: {ex.Message}");
                return new List<Song>();
            }
        }

        public Task<List<Song>> GetLikedSongsListAsync() => GetLikedSongsAsync();

        // 每日推荐歌单 - 雷达歌单
        public async Task<List<RecommendedPlaylist>> GetRecommendResourceAsync()
        {
            try
            {
                // 如果未登录，使用推荐歌单接口替代
                if (!IsLoggedIn)
                {
                    return await GetRecommendedPlaylistsAsync(10);
                }

                var json = await GetAsync("/recommend/resource");
                System.Diagnostics.Debug.WriteLine($"[API] RecommendResource Response: {json}");
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                // 检查是否需要登录
                if (result.TryGetProperty("code", out var code) && code.GetInt32() == 301)
                {
                    // 未登录，使用推荐歌单替代
                    return await GetRecommendedPlaylistsAsync(10);
                }

                var playlists = new List<RecommendedPlaylist>();
                if (result.TryGetProperty("recommend", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        // 调试输出每个字段
                        System.Diagnostics.Debug.WriteLine($"[API] Recommend item: {item}");
                        
                        var playCount = 0L;
                        // 尝试多种可能的字段名
                        if (item.TryGetProperty("playCount", out var pc))
                            playCount = pc.GetInt64();
                        else if (item.TryGetProperty("played", out var played))
                            playCount = played.GetInt64();
                        else if (item.TryGetProperty("playcount", out var playcount))
                            playCount = playcount.GetInt64();
                        
                        playlists.Add(new RecommendedPlaylist
                        {
                            Id = item.GetProperty("id").GetInt64().ToString(),
                            Name = item.GetProperty("name").GetString() ?? string.Empty,
                            PicUrl = item.TryGetProperty("picUrl", out var picUrl) ? picUrl.GetString() ?? string.Empty : string.Empty,
                            PlayCount = playCount,
                            TrackCount = item.TryGetProperty("trackCount", out var trackCount) ? trackCount.GetInt32() : 0
                        });
                    }
                }

                return playlists;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetRecommendResource Error: {ex.Message}");
                return new List<RecommendedPlaylist>();
            }
        }

        public async Task<List<RecommendedPlaylist>> GetRecommendedPlaylistsAsync(int limit = 10)
        {
            try
            {
                var json = await GetAsync($"/personalized?limit={limit}");
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                var playlists = new List<RecommendedPlaylist>();
                if (result.TryGetProperty("result", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        playlists.Add(new RecommendedPlaylist
                        {
                            Id = item.GetProperty("id").GetInt64().ToString(),
                            Name = item.GetProperty("name").GetString() ?? string.Empty,
                            PicUrl = item.GetProperty("picUrl").GetString() ?? string.Empty,
                            PlayCount = item.TryGetProperty("playCount", out var playCount) ? playCount.GetInt64() : 0,
                            TrackCount = item.TryGetProperty("trackCount", out var trackCount) ? trackCount.GetInt32() : 0
                        });
                    }
                }

                return playlists;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetRecommendedPlaylists Error: {ex.Message}");
                return new List<RecommendedPlaylist>();
            }
        }

        public async Task<List<Song>> GetPersonalizedSongsAsync(int limit = 10)
        {
            try
            {
                var json = await GetAsync($"/personalized/newsong?limit={limit}");
                System.Diagnostics.Debug.WriteLine($"[API] PersonalizedSongs Response: {json}");
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                var songs = new List<Song>();
                if (result.TryGetProperty("result", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        var song = new Song
                        {
                            Id = item.GetProperty("id").GetInt64().ToString(),
                            Name = item.GetProperty("name").GetString() ?? string.Empty
                        };

                        if (item.TryGetProperty("song", out var songObj))
                        {
                            if (songObj.TryGetProperty("artists", out var artists))
                            {
                                foreach (var artist in artists.EnumerateArray())
                                {
                                    song.Artists.Add(new Artist
                                    {
                                        Id = artist.GetProperty("id").GetInt64().ToString(),
                                        Name = artist.GetProperty("name").GetString() ?? string.Empty
                                    });
                                }
                            }

                            if (songObj.TryGetProperty("album", out var album))
                            {
                                song.Album = new Album
                                {
                                    Id = album.GetProperty("id").GetInt64().ToString(),
                                    Name = album.GetProperty("name").GetString() ?? string.Empty
                                };
                                // 从 album 获取封面
                                if (album.TryGetProperty("picUrl", out var albumPicUrl))
                                {
                                    song.CoverImgUrl = albumPicUrl.GetString() ?? string.Empty;
                                }
                            }

                            // 解析时长 - 字段名是 duration
                            if (songObj.TryGetProperty("duration", out var duration))
                            {
                                song.Duration = duration.GetInt64();
                            }
                        }

                        // 从外层获取封面（如果 song 对象没有）
                        if (string.IsNullOrEmpty(song.CoverImgUrl) && item.TryGetProperty("picUrl", out var outerPicUrl))
                        {
                            song.CoverImgUrl = outerPicUrl.GetString() ?? string.Empty;
                        }

                        songs.Add(song);
                    }
                }

                return songs;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetPersonalizedSongs Error: {ex.Message}");
                return new List<Song>();
            }
        }

        public async Task<List<CloudSongInfo>> GetCloudSongsAsync(int limit = 200, int offset = 0)
        {
            try
            {
                var json = await GetAsync($"/user/cloud?limit={limit}&offset={offset}");
                System.Diagnostics.Debug.WriteLine($"[API] Cloud Response: {json.Substring(0, Math.Min(500, json.Length))}");
                
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                var songs = new List<CloudSongInfo>();
                
                // 尝试不同的响应格式
                JsonElement dataArray;
                if (result.TryGetProperty("data", out dataArray))
                {
                    // 格式1: { "data": [...] }
                }
                else if (result.TryGetProperty("cloudData", out dataArray))
                {
                    // 格式2: { "cloudData": [...] }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[API] Cloud: No data found in response");
                    return songs;
                }

                foreach (var item in dataArray.EnumerateArray())
                {
                    try
                    {
                        var song = new CloudSongInfo();
                        
                        // 数据可能在 privateCloud 子对象中
                        JsonElement cloudItem = item;
                        if (item.TryGetProperty("privateCloud", out var privateCloud))
                        {
                            cloudItem = privateCloud;
                        }
                        
                        // songId 可能是 long 或 string
                        if (cloudItem.TryGetProperty("songId", out var songId))
                        {
                            song.SongId = songId.ValueKind == JsonValueKind.Number 
                                ? songId.GetInt64().ToString() 
                                : songId.GetString() ?? string.Empty;
                        }
                        
                        // 歌曲名可能是 songName 或 song 或 name
                        if (cloudItem.TryGetProperty("songName", out var songName))
                            song.SongName = songName.GetString() ?? string.Empty;
                        else if (cloudItem.TryGetProperty("song", out var song2))
                            song.SongName = song2.GetString() ?? string.Empty;
                        else if (item.TryGetProperty("simpleSong", out var ss) && ss.TryGetProperty("name", out var name))
                            song.SongName = name.GetString() ?? string.Empty;
                        
                        // 艺术家
                        if (cloudItem.TryGetProperty("artist", out var artist))
                            song.Artist = artist.GetString() ?? string.Empty;
                        else if (item.TryGetProperty("simpleSong", out var simpleSong) && 
                                 simpleSong.TryGetProperty("ar", out var ar) && 
                                 ar.GetArrayLength() > 0)
                        {
                            var artists = new List<string>();
                            foreach (var a in ar.EnumerateArray())
                            {
                                if (a.TryGetProperty("name", out var aName))
                                    artists.Add(aName.GetString() ?? string.Empty);
                            }
                            song.Artist = string.Join(" / ", artists);
                        }
                        
                        // 专辑
                        if (cloudItem.TryGetProperty("album", out var album))
                            song.Album = album.GetString() ?? string.Empty;
                        else if (item.TryGetProperty("simpleSong", out var ss2) && 
                                 ss2.TryGetProperty("al", out var al))
                        {
                            song.Album = al.TryGetProperty("name", out var alName) ? alName.GetString() ?? string.Empty : string.Empty;
                            if (al.TryGetProperty("picUrl", out var picUrl))
                                song.CoverUrl = picUrl.GetString() ?? string.Empty;
                        }
                        
                        // 文件名
                        if (cloudItem.TryGetProperty("fileName", out var fileName))
                            song.FileName = fileName.GetString() ?? string.Empty;
                        
                        // 时间和大小
                        if (cloudItem.TryGetProperty("addTime", out var addTime))
                            song.AddTime = addTime.GetInt64();
                            
                        if (cloudItem.TryGetProperty("fileSize", out var fileSize))
                            song.FileSize = fileSize.GetInt64();
                            
                        // 时长
                        if (item.TryGetProperty("simpleSong", out var simpleSong2) && 
                            simpleSong2.TryGetProperty("dt", out var dt))
                        {
                            song.Duration = dt.GetInt64();
                        }
                        
                        songs.Add(song);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[API] Cloud item parse error: {ex.Message}");
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[API] Cloud: Found {songs.Count} songs");
                return songs;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetCloudSongs Error: {ex.Message}");
                return new List<CloudSongInfo>();
            }
        }

        public async Task<bool> DeleteCloudSongAsync(string songId)
        {
            try
            {
                var json = await GetAsync($"/user/cloud/del?id={songId}");
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                if (result.TryGetProperty("code", out var code) && code.GetInt32() == 200)
                {
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] DeleteCloudSong Error: {ex.Message}");
                return false;
            }
        }

        public async Task<string> GetCloudSongUrlAsync(string songId)
        {
            return await GetSongUrlAsync(songId);
        }

        public async Task<List<RecentSong>> GetRecentSongsAsync(int limit = 100)
        {
            try
            {
                var json = await GetAsync($"/record/recent/song?limit={limit}");
                System.Diagnostics.Debug.WriteLine($"[API] Recent songs response: {json.Substring(0, Math.Min(500, json.Length))}");
                
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                var songs = new List<RecentSong>();
                if (result.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("list", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        try
                        {
                            var song = new RecentSong();
                            
                            if (item.TryGetProperty("playTime", out var playTime))
                                song.PlayTime = playTime.GetInt64();
                            
                            if (item.TryGetProperty("resourceId", out var resourceId))
                                song.ResourceId = resourceId.GetString() ?? string.Empty;
                            
                            if (item.TryGetProperty("resourceType", out var resourceType))
                                song.ResourceType = resourceType.GetString() ?? string.Empty;
                            
                            // 解析歌曲信息
                            if (item.TryGetProperty("data", out var songData))
                            {
                                song.Id = songData.GetProperty("id").GetInt64().ToString();
                                song.Name = songData.GetProperty("name").GetString() ?? string.Empty;
                                
                                if (songData.TryGetProperty("ar", out var artists))
                                {
                                    var artistNames = new List<string>();
                                    foreach (var artist in artists.EnumerateArray())
                                    {
                                        artistNames.Add(artist.GetProperty("name").GetString() ?? string.Empty);
                                    }
                                    song.Artist = string.Join(" / ", artistNames);
                                }
                                
                                if (songData.TryGetProperty("al", out var album))
                                    song.AlbumName = album.GetProperty("name").GetString() ?? string.Empty;
                                
                                if (songData.TryGetProperty("dt", out var duration))
                                    song.Duration = duration.GetInt64();
                                
                                // VIP信息
                                if (songData.TryGetProperty("fee", out var fee))
                                    song.Fee = fee.GetInt32();
                            }
                            
                            songs.Add(song);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[API] Recent song parse error: {ex.Message}");
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[API] Recent songs: {songs.Count}");
                return songs;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetRecentSongs Error: {ex.Message}");
                return new List<RecentSong>();
            }
        }

        public async Task<UserDetailInfo?> GetUserDetailAsync(long uid)
        {
            try
            {
                var json = await GetAsync($"/user/detail?uid={uid}");
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                if (result.TryGetProperty("profile", out var profile))
                {
                    return new UserDetailInfo
                    {
                        UserId = profile.GetProperty("userId").GetInt64(),
                        Nickname = profile.GetProperty("nickname").GetString() ?? string.Empty,
                        AvatarUrl = profile.TryGetProperty("avatarUrl", out var avatar) ? avatar.GetString() ?? string.Empty : string.Empty,
                        Signature = profile.TryGetProperty("signature", out var sig) ? sig.GetString() ?? string.Empty : string.Empty,
                        FollowCount = profile.TryGetProperty("follows", out var follows) ? follows.GetInt32() : 0,
                        FollowedCount = profile.TryGetProperty("followeds", out var followeds) ? followeds.GetInt32() : 0,
                        EventCount = profile.TryGetProperty("eventCount", out var events) ? events.GetInt32() : 0
                    };
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetUserDetail Error: {ex.Message}");
                return null;
            }
        }

        public async Task<List<FollowUser>> GetFollowsAsync(long uid, int limit = 30, int offset = 0)
        {
            try
            {
                var json = await GetAsync($"/user/follows?uid={uid}&limit={limit}&offset={offset}");
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                var users = new List<FollowUser>();
                if (result.TryGetProperty("follow", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        users.Add(new FollowUser
                        {
                            UserId = item.GetProperty("userId").GetInt64(),
                            Nickname = item.GetProperty("nickname").GetString() ?? string.Empty,
                            AvatarUrl = item.TryGetProperty("avatarUrl", out var av) ? av.GetString() ?? string.Empty : string.Empty,
                            Signature = item.TryGetProperty("signature", out var sig) ? sig.GetString() ?? string.Empty : string.Empty
                        });
                    }
                }
                return users;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetFollows Error: {ex.Message}");
                return new List<FollowUser>();
            }
        }

        public async Task<List<FollowUser>> GetFollowedsAsync(long uid, int limit = 30, int offset = 0)
        {
            try
            {
                var json = await GetAsync($"/user/followeds?uid={uid}&limit={limit}&offset={offset}");
                System.Diagnostics.Debug.WriteLine($"[API] GetFolloweds Response: {json}");
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                var users = new List<FollowUser>();
                if (result.TryGetProperty("followeds", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        users.Add(new FollowUser
                        {
                            UserId = item.GetProperty("userId").GetInt64(),
                            Nickname = item.GetProperty("nickname").GetString() ?? string.Empty,
                            AvatarUrl = item.TryGetProperty("avatarUrl", out var av) ? av.GetString() ?? string.Empty : string.Empty,
                            Signature = item.TryGetProperty("signature", out var sig) ? sig.GetString() ?? string.Empty : string.Empty
                        });
                    }
                }
                System.Diagnostics.Debug.WriteLine($"[API] GetFolloweds Parsed: {users.Count} users");
                return users;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetFolloweds Error: {ex.Message}");
                return new List<FollowUser>();
            }
        }

        public async Task<bool> CreatePlaylistAsync(string name)
        {
            try
            {
                var url = $"/playlist/create?name={Uri.EscapeDataString(name)}";
                System.Diagnostics.Debug.WriteLine($"[API] CreatePlaylist Request: {_baseUrl}{url}");

                var json = await GetAsync(url);
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                if (result.TryGetProperty("code", out var code) && code.GetInt32() == 200)
                {
                    System.Diagnostics.Debug.WriteLine($"[API] CreatePlaylist success");
                    return true;
                }

                var message = result.TryGetProperty("message", out var msg) ? msg.GetString() ?? "未知错误" : "未知错误";
                System.Diagnostics.Debug.WriteLine($"[API] CreatePlaylist failed: {message}");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] CreatePlaylist Error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DeletePlaylistAsync(string playlistId)
        {
            try
            {
                var url = $"/playlist/delete?id={playlistId}";
                System.Diagnostics.Debug.WriteLine($"[API] DeletePlaylist Request: {_baseUrl}{url}");

                var json = await GetAsync(url);
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                if (result.TryGetProperty("code", out var code) && code.GetInt32() == 200)
                {
                    System.Diagnostics.Debug.WriteLine($"[API] DeletePlaylist success");
                    return true;
                }

                var message = result.TryGetProperty("message", out var msg) ? msg.GetString() ?? "未知错误" : "未知错误";
                System.Diagnostics.Debug.WriteLine($"[API] DeletePlaylist failed: {message}");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] DeletePlaylist Error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SetPlaylistSubscriptionAsync(string playlistId, bool subscribe)
        {
            try
            {
                var json = await GetAsync($"/playlist/subscribe?t={(subscribe ? 1 : 2)}&id={playlistId}");
                var result = JsonSerializer.Deserialize<JsonElement>(json);
                return result.TryGetProperty("code", out var code) && code.GetInt32() == 200;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] SetPlaylistSubscription Error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> UnsubscribePlaylistAsync(string playlistId)
        {
            try
            {
                var url = $"/playlist/subscribe?t=2&id={playlistId}";
                System.Diagnostics.Debug.WriteLine($"[API] UnsubscribePlaylist Request: {_baseUrl}{url}");

                var json = await GetAsync(url);
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                if (result.TryGetProperty("code", out var code) && code.GetInt32() == 200)
                {
                    System.Diagnostics.Debug.WriteLine($"[API] UnsubscribePlaylist success");
                    return true;
                }

                var message = result.TryGetProperty("message", out var msg) ? msg.GetString() ?? "未知错误" : "未知错误";
                System.Diagnostics.Debug.WriteLine($"[API] UnsubscribePlaylist failed: {message}");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] UnsubscribePlaylist Error: {ex.Message}");
                return false;
            }
        }
    }

    public class PlaylistInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string CoverImgUrl { get; set; } = string.Empty;
        public int TrackCount { get; set; }
        public long PlayCount { get; set; }
        public string CreatorName { get; set; } = string.Empty;
        public long CreatorId { get; set; }

        public string PlayCountFormatted
        {
            get
            {
                if (PlayCount >= 100000000)
                    return $"{PlayCount / 100000000.0:F1}亿";
                if (PlayCount >= 10000)
                    return $"{PlayCount / 10000.0:F1}万";
                return PlayCount.ToString();
            }
        }
    }

    public class SearchSuggestion
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
    }

    public class UserInfo
    {
        public long UserId { get; set; }
        public string Nickname { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
        public int VipType { get; set; }
    }

    public class CloudSongInfo
    {
        public string SongId { get; set; } = string.Empty;
        public string SongName { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string CoverUrl { get; set; } = string.Empty;
        public long AddTime { get; set; }
        public long FileSize { get; set; }
        public long Duration { get; set; }

        public string FileSizeFormatted
        {
            get
            {
                if (FileSize >= 1073741824)
                    return $"{FileSize / 1073741824.0:F2} GB";
                if (FileSize >= 1048576)
                    return $"{FileSize / 1048576.0:F2} MB";
                if (FileSize >= 1024)
                    return $"{FileSize / 1024.0:F2} KB";
                return $"{FileSize} B";
            }
        }

        public string DurationFormatted
        {
            get
            {
                var seconds = Duration / 1000;
                var minutes = seconds / 60;
                seconds = seconds % 60;
                return $"{minutes:D2}:{seconds:D2}";
            }
        }

        public string AddTimeFormatted
        {
            get
            {
                var dateTime = DateTimeOffset.FromUnixTimeMilliseconds(AddTime).LocalDateTime;
                return dateTime.ToString("yyyy-MM-dd");
            }
        }
    }

    public class RecommendedPlaylist
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string PicUrl { get; set; } = string.Empty;
        public long PlayCount { get; set; }
        public int TrackCount { get; set; }

        public string PlayCountFormatted
        {
            get
            {
                if (PlayCount >= 100000000)
                    return $"{PlayCount / 100000000.0:F1}亿";
                if (PlayCount >= 10000)
                    return $"{PlayCount / 10000.0:F1}万";
                return PlayCount.ToString();
            }
        }
    }

    public class UserDetailInfo
    {
        public long UserId { get; set; }
        public string Nickname { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
        public int FollowCount { get; set; }
        public int FollowedCount { get; set; }
        public int EventCount { get; set; }
    }

    public class FollowUser
    {
        public long UserId { get; set; }
        public string Nickname { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
    }

    public class RecentSong
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string AlbumName { get; set; } = string.Empty;
        public long PlayTime { get; set; }
        public string ResourceId { get; set; } = string.Empty;
        public string ResourceType { get; set; } = string.Empty;
        public long Duration { get; set; }
        public int Fee { get; set; } = 0;

        public string DurationFormatted
        {
            get
            {
                var seconds = Duration / 1000;
                var minutes = seconds / 60;
                seconds = seconds % 60;
                return $"{minutes:D2}:{seconds:D2}";
            }
        }

        public string PlayTimeFormatted
        {
            get
            {
                var dateTime = DateTimeOffset.FromUnixTimeMilliseconds(PlayTime).LocalDateTime;
                var now = DateTime.Now;
                var diff = now - dateTime;

                if (diff.TotalMinutes < 1)
                    return "刚刚";
                if (diff.TotalHours < 1)
                    return $"{(int)diff.TotalMinutes}分钟前";
                if (diff.TotalDays < 1)
                    return $"{(int)diff.TotalHours}小时前";
                if (diff.TotalDays < 7)
                    return $"{(int)diff.TotalDays}天前";
                return dateTime.ToString("MM-dd HH:mm");
            }
        }

        public string PlatformName
        {
            get
            {
                return ResourceType switch
                {
                    "android" => "手机",
                    "iphone" => "手机",
                    "pc" => "PC",
                    "web" => "PC",
                    "linux" => "PC",
                    _ => "手机"
                };
            }
        }

        public bool IsVip => Fee == 1;
    }

    public class LyricInfo
    {
        public string LrcLyric { get; set; } = string.Empty;
        public string TranslatedLyric { get; set; } = string.Empty;
    }

    public class SearchArtistInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string PicUrl { get; set; } = string.Empty;
        public int SongCount { get; set; }
    }

    public class SearchAlbumInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string PicUrl { get; set; } = string.Empty;
        public string ArtistName { get; set; } = string.Empty;
        public string PublishTime { get; set; } = string.Empty;
        public int Size { get; set; }
    }

    public class PlaylistDetailInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string CoverImgUrl { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string CreatorName { get; set; } = string.Empty;
        public string CreatorAvatarUrl { get; set; } = string.Empty;
        public long CreatorId { get; set; }
        public bool Subscribed { get; set; }
        public int TrackCount { get; set; }
        public long PlayCount { get; set; }
        public long SubscribedCount { get; set; }
        public long ShareCount { get; set; }
        public long CommentCount { get; set; }
        public int Privacy { get; set; }
        public List<string> Tags { get; set; } = new();

        public string PlayCountFormatted
        {
            get
            {
                if (PlayCount >= 100000000)
                    return $"{PlayCount / 100000000.0:F1}亿";
                if (PlayCount >= 10000)
                    return $"{PlayCount / 10000.0:F1}万";
                return PlayCount.ToString();
            }
        }

        public string SubscribedCountFormatted
        {
            get
            {
                if (SubscribedCount >= 100000000)
                    return $"{SubscribedCount / 100000000.0:F1}亿";
                if (SubscribedCount >= 10000)
                    return $"{SubscribedCount / 10000.0:F1}万";
                return SubscribedCount.ToString();
            }
        }

        public string TagsText => string.Join(" / ", Tags);
    }

    public class AlbumDetailInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string PicUrl { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ArtistName { get; set; } = string.Empty;
        public string ArtistId { get; set; } = string.Empty;
        public string PublishTime { get; set; } = string.Empty;
        public int Size { get; set; }
        public string SubType { get; set; } = string.Empty;
        public string Company { get; set; } = string.Empty;
    }
}