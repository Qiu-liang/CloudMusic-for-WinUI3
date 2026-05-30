using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;
using music.Models;

namespace music.Services
{
    public class MusicApiService
    {
        private readonly HttpClient _httpClient;
        private string _baseUrl;
        private string _cookie = string.Empty;
        private long _userId = 0;
        private bool _isInitialized = false;
        private string _deviceId = string.Empty;

        public bool IsLoggedIn => !string.IsNullOrEmpty(_cookie) && _userId > 0;
        public long UserId => _userId;

        public MusicApiService()
        {
            var settings = ApplicationData.Current.LocalSettings;
            _baseUrl = settings.Values["ServerAddress"]?.ToString() ?? "http://192.168.31.205:3000";
            
            // 获取或生成设备ID
            _deviceId = settings.Values["DeviceId"]?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(_deviceId))
            {
                _deviceId = Guid.NewGuid().ToString("N");
                settings.Values["DeviceId"] = _deviceId;
            }
            
            // 读取已保存的cookie和userId
            _cookie = settings.Values["Cookie"]?.ToString() ?? string.Empty;
            _userId = settings.Values.ContainsKey("UserId") ? Convert.ToInt64(settings.Values["UserId"]) : 0;
            if (!string.IsNullOrEmpty(_cookie))
            {
                _isInitialized = true;
            }
            
            _httpClient = new HttpClient();
            _httpClient.BaseAddress = new Uri(_baseUrl);
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            
            // 设置固定的User-Agent
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            
            System.Diagnostics.Debug.WriteLine($"[API] DeviceId: {_deviceId}");
            System.Diagnostics.Debug.WriteLine($"[API] HasCookie: {!string.IsNullOrEmpty(_cookie)}");
        }

        public void UpdateBaseUrl(string url)
        {
            _baseUrl = url;
            _httpClient.BaseAddress = new Uri(url);
            
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["ServerAddress"] = url;
        }

        public async void SetLoginCookie(string cookie)
        {
            _cookie = cookie;
            _isInitialized = true;

            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["Cookie"] = cookie;

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

        public async Task<string> GetAsync(string url)
        {
            try
            {
                // 添加设备ID参数
                var separator = url.Contains("?") ? "&" : "?";
                var fullUrl = $"{url}{separator}deviceId={_deviceId}";
                
                System.Diagnostics.Debug.WriteLine($"[API] Request: {_baseUrl}{fullUrl}");
                
                var request = new HttpRequestMessage(HttpMethod.Get, fullUrl);
                if (!string.IsNullOrEmpty(_cookie))
                {
                    request.Headers.Add("Cookie", _cookie);
                }
                
                var response = await _httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                
                // 检查响应中的set-cookie并更新
                if (response.Headers.Contains("Set-Cookie"))
                {
                    var setCookies = response.Headers.GetValues("Set-Cookie");
                    UpdateCookiesFromResponse(setCookies);
                }
                
                System.Diagnostics.Debug.WriteLine($"[API] Response: {json.Substring(0, Math.Min(200, json.Length))}...");
                return json;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] Error: {ex.Message}");
                return "{}";
            }
        }

        private void UpdateCookiesFromResponse(IEnumerable<string> setCookies)
        {
            var settings = ApplicationData.Current.LocalSettings;
            foreach (var cookie in setCookies)
            {
                // 提取cookie名称和值
                var parts = cookie.Split(';')[0].Split('=');
                if (parts.Length >= 2)
                {
                    var name = parts[0].Trim();
                    var value = string.Join("=", parts.Skip(1));
                    
                    // 保存重要的cookie
                    if (name == "MUSIC_U" || name == "__csrf" || name.StartsWith("MUSIC_"))
                    {
                        settings.Values[$"Cookie_{name}"] = cookie.Split(';')[0].Trim();
                    }
                }
            }
        }

        public async Task<string> GetWithoutCookieAsync(string url)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[API] Request (no cookie): {_baseUrl}{url}");
                var response = await _httpClient.GetAsync(url);
                var json = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"[API] Response: {json.Substring(0, Math.Min(200, json.Length))}...");
                return json;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] Error: {ex.Message}");
                return "{}";
            }
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
                        _cookie = cookieElement.GetString() ?? string.Empty;
                    }

                    var settings = ApplicationData.Current.LocalSettings;
                    settings.Values["Cookie"] = _cookie;
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
            try
            {
                var url = $"/login/cellphone?phone={phone}&password={Uri.EscapeDataString(password)}";
                System.Diagnostics.Debug.WriteLine($"[API] Login Request: {_baseUrl}{url}");
                
                var json = await GetWithoutCookieAsync(url);
                System.Diagnostics.Debug.WriteLine($"[API] Login Response: {json}");
                
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                if (result.TryGetProperty("code", out var code) && code.GetInt32() == 200)
                {
                    if (result.TryGetProperty("cookie", out var cookieElement))
                    {
                        _cookie = cookieElement.GetString() ?? string.Empty;
                    }
                    if (result.TryGetProperty("account", out var account) &&
                        account.TryGetProperty("id", out var id))
                    {
                        _userId = id.GetInt64();
                    }

                    var settings = ApplicationData.Current.LocalSettings;
                    settings.Values["Cookie"] = _cookie;
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

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetUserInfo Error: {ex.Message}");
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
            if (settings.Values.ContainsKey("Cookie"))
            {
                _cookie = settings.Values["Cookie"].ToString() ?? string.Empty;
                _userId = settings.Values.ContainsKey("UserId") ? (long)settings.Values["UserId"] : 0;
                _isInitialized = true;
                return;
            }

            await LoginAnonymouslyAsync();
        }

        public void ResetLogin()
        {
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values.Remove("Cookie");
            settings.Values.Remove("UserId");
            _cookie = string.Empty;
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
                var json = await GetAsync("/recommend/songs");
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                var songs = new List<Song>();
                if (result.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("dailySongs", out var dailySongs))
                {
                    foreach (var item in dailySongs.EnumerateArray())
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

        public async Task<List<PlaylistInfo>> GetUserPlaylistsAsync(long uid)
        {
            try
            {
                var json = await GetAsync($"/user/playlist?uid={uid}");
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
                            CreatorName = item.GetProperty("creator").GetProperty("nickname").GetString() ?? string.Empty
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

        public async Task<string> GetSongUrlAsync(string songId, int br = 320000)
        {
            try
            {
                // 获取用户设置的音质
                var settings = ApplicationData.Current.LocalSettings;
                var quality = settings.Values["AudioQuality"]?.ToString() ?? "standard";
                
                // 使用新版API获取歌曲URL
                var json = await GetAsync($"/song/url/v1?id={songId}&level={quality}");
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

        public async Task<List<SearchSuggestion>> GetSearchSuggestionsAsync(string keywords)
        {
            try
            {
                var json = await GetAsync($"/search/suggest?keywords={Uri.EscapeDataString(keywords)}");
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

        public async Task<LyricInfo?> GetLyricsAsync(string songId)
        {
            try
            {
                var json = await GetAsync($"/lyric?id={songId}");
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
            try
            {
                if (!IsLoggedIn)
                {
                    return new List<string>();
                }

                var json = await GetAsync($"/likelist?uid={_userId}");
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                var ids = new List<string>();
                if (result.TryGetProperty("ids", out var idsArray))
                {
                    foreach (var id in idsArray.EnumerateArray())
                    {
                        ids.Add(id.GetInt64().ToString());
                    }
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
                if (ids.Count == 0)
                {
                    return new List<Song>();
                }

                var idsParam = string.Join(",", ids.Take(500));
                var json = await GetAsync($"/song/detail?ids={idsParam}");
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
                System.Diagnostics.Debug.WriteLine($"[API] GetLikedSongs Error: {ex.Message}");
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
                            }
                        }

                        if (item.TryGetProperty("picUrl", out var picUrl))
                        {
                            song.CoverImgUrl = picUrl.GetString() ?? string.Empty;
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
                            AvatarUrl = item.TryGetProperty("avatarUrl", out var avatar) ? avatar.GetString() ?? string.Empty : string.Empty,
                            Followed = item.TryGetProperty("followed", out var followed) && followed.GetBoolean(),
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
                            AvatarUrl = item.TryGetProperty("avatarUrl", out var avatar) ? avatar.GetString() ?? string.Empty : string.Empty,
                            Followed = item.TryGetProperty("followed", out var followed) && followed.GetBoolean(),
                            Signature = item.TryGetProperty("signature", out var sig) ? sig.GetString() ?? string.Empty : string.Empty
                        });
                    }
                }

                return users;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetFolloweds Error: {ex.Message}");
                return new List<FollowUser>();
            }
        }

        public async Task<bool> FollowUserAsync(long userId, bool follow = true)
        {
            try
            {
                var t = follow ? 1 : 0;
                var json = await GetAsync($"/follow?id={userId}&t={t}");
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                if (result.TryGetProperty("code", out var code) && code.GetInt32() == 200)
                {
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] FollowUser Error: {ex.Message}");
                return false;
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
    }

    public class PlaylistInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string CoverImgUrl { get; set; } = string.Empty;
        public int TrackCount { get; set; }
        public string CreatorName { get; set; } = string.Empty;
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

    public class FollowUser
    {
        public long UserId { get; set; }
        public string Nickname { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
        public bool Followed { get; set; }
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
}