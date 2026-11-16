using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using music.Core.Netease;
using music.Core.Netease.Mapping;
using music.Core.Results;
using music.Services;

namespace music.Core.Services
{
    public interface ICloudService
    {
        Task<ApiResult<List<CloudSongInfo>>> GetCloudSongsAsync(int limit = 200, int offset = 0);
        Task<ApiResult> DeleteCloudSongAsync(string songId);
        Task<ApiResult> UploadCloudSongAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default);
    }

    public sealed class CloudService : ICloudService
    {
        private readonly NeteaseHttpTransport _transport;
        private readonly ISessionService _sessionService;

        public CloudService(NeteaseHttpTransport transport, ISessionService sessionService)
        {
            _transport = transport;
            _sessionService = sessionService;
        }

        public async Task<ApiResult<List<CloudSongInfo>>> GetCloudSongsAsync(int limit = 200, int offset = 0)
        {
            try
            {
                var resp = await _transport.GetAsync($"/user/cloud?limit={limit}&offset={offset}").ConfigureAwait(false);
                if (!resp.Success)
                {
                    return ApiResult<List<CloudSongInfo>>.Fail(resp.Result, new List<CloudSongInfo>());
                }

                return ApiResult<List<CloudSongInfo>>.Ok(CloudJsonMapper.ParseCloudSongs(resp.Body));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetCloudSongs Error: {ex.Message}");
                return ApiResult<List<CloudSongInfo>>.Ok(new List<CloudSongInfo>());
            }
        }

        public async Task<ApiResult> DeleteCloudSongAsync(string songId)
        {
            try
            {
                var resp = await _transport.GetAsync($"/user/cloud/del?id={Uri.EscapeDataString(songId)}").ConfigureAwait(false);
                if (!resp.Success) return resp.Result;

                return AuthJsonMapper.TryParseCode200(resp.Body)
                    ? new ApiResult(true, ApiErrorKind.None, string.Empty, 200, 200)
                    : new ApiResult(false, ApiErrorKind.Business, "删除失败。");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] DeleteCloudSong Error: {ex.Message}");
                return new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message);
            }
        }

        public async Task<ApiResult> UploadCloudSongAsync(
            Stream fileStream,
            string fileName,
            CancellationToken cancellationToken = default)
        {
            if (!_sessionService.IsLoggedIn)
                return new ApiResult(false, ApiErrorKind.Authentication, "请先登录后再上传。", 301);

            using var content = new StreamContent(fileStream);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using var formData = new MultipartFormDataContent();
            formData.Add(content, "songFile", fileName);
            var sent = await _transport.SendAsync(
                HttpMethod.Post,
                "/cloud",
                formData,
                includeCookies: true,
                requireBusinessCode: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return !sent.Success
                ? sent.Result
                : ApiResult.FromHttpResponse(200, sent.Body, requireBusinessCode: true);
        }
    }

    public interface IProfileService
    {
        Task<UserDetailInfo?> GetUserDetailAsync(long uid);
        Task<List<FollowUser>> GetFollowsAsync(long uid, int limit = 30, int offset = 0);
        Task<List<FollowUser>> GetFollowedsAsync(long uid, int limit = 30, int offset = 0);
        Task<List<RecentSong>> GetRecentSongsAsync(int limit = 100);
    }

    public sealed class ProfileService : IProfileService
    {
        private readonly NeteaseHttpTransport _transport;

        public ProfileService(NeteaseHttpTransport transport)
        {
            _transport = transport;
        }

        public async Task<UserDetailInfo?> GetUserDetailAsync(long uid)
        {
            var resp = await _transport.GetAsync($"/user/detail?uid={uid}").ConfigureAwait(false);
            return resp.Success ? ProfileJsonMapper.ParseUserDetail(resp.Body) : null;
        }

        public async Task<List<FollowUser>> GetFollowsAsync(long uid, int limit = 30, int offset = 0)
        {
            var resp = await _transport.GetAsync($"/user/follows?uid={uid}&limit={limit}&offset={offset}").ConfigureAwait(false);
            return resp.Success ? ProfileJsonMapper.ParseFollowUsers(resp.Body, "follow") : new List<FollowUser>();
        }

        public async Task<List<FollowUser>> GetFollowedsAsync(long uid, int limit = 30, int offset = 0)
        {
            var resp = await _transport.GetAsync($"/user/followeds?uid={uid}&limit={limit}&offset={offset}").ConfigureAwait(false);
            return resp.Success ? ProfileJsonMapper.ParseFollowUsers(resp.Body, "followeds") : new List<FollowUser>();
        }

        public async Task<List<RecentSong>> GetRecentSongsAsync(int limit = 100)
        {
            var resp = await _transport.GetAsync($"/record/recent/song?limit={limit}").ConfigureAwait(false);
            return resp.Success ? ProfileJsonMapper.ParseRecentSongs(resp.Body) : new List<RecentSong>();
        }
    }
}
