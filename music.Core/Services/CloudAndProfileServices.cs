using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using music.Core.Abstractions;
using music.Core.Models;
using music.Core.Netease;
using music.Core.Netease.Mapping;
using music.Core.Results;

namespace music.Core.Services
{
    public interface ICloudService
    {
        Task<ApiResult<List<CloudSongInfo>>> GetCloudSongsAsync(int limit = 200, int offset = 0, CancellationToken cancellationToken = default);
        Task<ApiResult> DeleteCloudSongAsync(string songId, CancellationToken cancellationToken = default);
        Task<ApiResult> UploadCloudSongAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default);
    }

    public sealed class CloudService : ICloudService
    {
        private readonly NeteaseHttpTransport _transport;
        private readonly ISessionService _sessionService;
        private readonly IApiLog _log;

        public CloudService(NeteaseHttpTransport transport, ISessionService sessionService, IApiLog? log = null)
        {
            _transport = transport;
            _sessionService = sessionService;
            _log = log ?? NullApiLog.Instance;
        }

        public async Task<ApiResult<List<CloudSongInfo>>> GetCloudSongsAsync(
            int limit = 200,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var resp = await _transport.GetAsync($"/user/cloud?limit={limit}&offset={offset}", cancellationToken).ConfigureAwait(false);
                if (!resp.Success)
                {
                    return ApiResult<List<CloudSongInfo>>.Fail(resp.Result, new List<CloudSongInfo>());
                }

                return ApiResult<List<CloudSongInfo>>.Ok(CloudJsonMapper.ParseCloudSongs(resp.Body, _log));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Write($"GetCloudSongs Error: {ex}");
                return ApiResult<List<CloudSongInfo>>.Fail(
                    new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message),
                    new List<CloudSongInfo>());
            }
        }

        public async Task<ApiResult> DeleteCloudSongAsync(string songId, CancellationToken cancellationToken = default)
        {
            try
            {
                var resp = await _transport.GetAsync($"/user/cloud/del?id={Uri.EscapeDataString(songId)}", cancellationToken).ConfigureAwait(false);
                if (!resp.Success) return resp.Result;

                return AuthJsonMapper.TryParseCode200(resp.Body)
                    ? new ApiResult(true, ApiErrorKind.None, string.Empty, 200, 200)
                    : new ApiResult(false, ApiErrorKind.Business, "删除失败。");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Write($"DeleteCloudSong Error: {ex}");
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
        Task<ApiResult<UserDetailInfo?>> GetUserDetailAsync(long uid, CancellationToken cancellationToken = default);
        Task<ApiResult<List<FollowUser>>> GetFollowsAsync(long uid, int limit = 30, int offset = 0, CancellationToken cancellationToken = default);
        Task<ApiResult<List<FollowUser>>> GetFollowedsAsync(long uid, int limit = 30, int offset = 0, CancellationToken cancellationToken = default);
        Task<ApiResult<List<RecentSong>>> GetRecentSongsAsync(int limit = 100, CancellationToken cancellationToken = default);
    }

    public sealed class ProfileService : IProfileService
    {
        private readonly NeteaseHttpTransport _transport;
        private readonly IApiLog _log;

        public ProfileService(NeteaseHttpTransport transport, IApiLog? log = null)
        {
            _transport = transport;
            _log = log ?? NullApiLog.Instance;
        }

        public async Task<ApiResult<UserDetailInfo?>> GetUserDetailAsync(long uid, CancellationToken cancellationToken = default)
        {
            try
            {
                var resp = await _transport.GetAsync($"/user/detail?uid={uid}", cancellationToken).ConfigureAwait(false);
                if (!resp.Success)
                {
                    return ApiResult<UserDetailInfo?>.Fail(resp.Result, null);
                }
                return ApiResult<UserDetailInfo?>.Ok(ProfileJsonMapper.ParseUserDetail(resp.Body));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Write($"GetUserDetail Error: {ex}");
                return ApiResult<UserDetailInfo?>.Fail(
                    new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message),
                    null);
            }
        }

        public async Task<ApiResult<List<FollowUser>>> GetFollowsAsync(
            long uid,
            int limit = 30,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var resp = await _transport.GetAsync($"/user/follows?uid={uid}&limit={limit}&offset={offset}", cancellationToken).ConfigureAwait(false);
                if (!resp.Success)
                {
                    return ApiResult<List<FollowUser>>.Fail(resp.Result, new List<FollowUser>());
                }
                return ApiResult<List<FollowUser>>.Ok(ProfileJsonMapper.ParseFollowUsers(resp.Body, "follow"));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Write($"GetFollows Error: {ex}");
                return ApiResult<List<FollowUser>>.Fail(
                    new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message),
                    new List<FollowUser>());
            }
        }

        public async Task<ApiResult<List<FollowUser>>> GetFollowedsAsync(
            long uid,
            int limit = 30,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var resp = await _transport.GetAsync($"/user/followeds?uid={uid}&limit={limit}&offset={offset}", cancellationToken).ConfigureAwait(false);
                if (!resp.Success)
                {
                    return ApiResult<List<FollowUser>>.Fail(resp.Result, new List<FollowUser>());
                }
                return ApiResult<List<FollowUser>>.Ok(ProfileJsonMapper.ParseFollowUsers(resp.Body, "followeds"));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Write($"GetFolloweds Error: {ex}");
                return ApiResult<List<FollowUser>>.Fail(
                    new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message),
                    new List<FollowUser>());
            }
        }

        public async Task<ApiResult<List<RecentSong>>> GetRecentSongsAsync(int limit = 100, CancellationToken cancellationToken = default)
        {
            try
            {
                var resp = await _transport.GetAsync($"/record/recent/song?limit={limit}", cancellationToken).ConfigureAwait(false);
                if (!resp.Success)
                {
                    return ApiResult<List<RecentSong>>.Fail(resp.Result, new List<RecentSong>());
                }
                return ApiResult<List<RecentSong>>.Ok(ProfileJsonMapper.ParseRecentSongs(resp.Body, _log));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Write($"GetRecentSongs Error: {ex}");
                return ApiResult<List<RecentSong>>.Fail(
                    new ApiResult(false, ApiErrorKind.InvalidResponse, ex.Message),
                    new List<RecentSong>());
            }
        }
    }
}
