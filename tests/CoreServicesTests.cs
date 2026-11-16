using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using music.Core.Netease;
using music.Core.Services;
using music.Models;
using music.Services;
using Xunit;

namespace music.Tests;

public class CoreServicesTests
{
    private sealed record TestHarness(
        InMemorySettingsStore Settings,
        InMemoryCredentialStore Credentials,
        RecordingApiLog Log,
        ServerEndpoint Endpoint,
        SessionCookieJar CookieJar,
        NeteaseHttpTransport Transport,
        QualityService Quality,
        SongSourceResolver SongSource,
        LibraryService Library,
        SessionService Session,
        PlaylistService Playlist,
        RecommendationService Recommendation) : IDisposable
    {
        public void Dispose() => Transport.Dispose();
    }

    private static TestHarness CreateHarness(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var settings = new InMemorySettingsStore();
        var creds = new InMemoryCredentialStore();
        var log = new RecordingApiLog();
        var endpoint = new ServerEndpoint(settings, log);
        var jar = new SessionCookieJar(creds, log);
        var transport = new NeteaseHttpTransport(endpoint, jar, settings, log, new FakeHttpMessageHandler(responder));
        var quality = new QualityService(transport, settings);
        var songSource = new SongSourceResolver(transport, settings);

        var session = new SessionService(transport, endpoint, jar, settings, log);
        session.SessionChanged += (_, _) => quality.ClearCache();
        session.VipStatusRefreshed += (_, _) => quality.ClearCache();

        var library = new LibraryService(transport, quality, session);
        var playlist = new PlaylistService(transport, library, quality);
        var recommendation = new RecommendationService(transport, session, library, quality, jar);

        return new TestHarness(settings, creds, log, endpoint, jar, transport, quality, songSource, library, session, playlist, recommendation);
    }

    [Fact]
    public async Task RemoteHttpLoginBlockedUnlessAllowedLoopbackOrHttps()
    {
        using var h = CreateHarness(_ => FakeHttpMessageHandler.Json("{\"code\":200,\"cookie\":\"MUSIC_U=ok\",\"account\":{\"id\":123}}"));
        h.Endpoint.UpdateBaseUrl("http://192.168.31.205:3000");

        var blocked = await h.Session.LoginWithPhoneAsync("13800138000", "secret_pw");
        Assert.False(blocked.Success);
        Assert.Equal(-1, blocked.Code);
        Assert.Contains("远程 HTTP 登录默认被禁止", blocked.Message);

        h.Settings.SetBoolean("AllowInsecureLogin", true);
        var allowed = await h.Session.LoginWithPhoneAsync("13800138000", "secret_pw");
        Assert.True(allowed.Success);
        Assert.Equal(123L, h.Session.UserId);
        Assert.DoesNotContain("secret_pw", string.Join("\n", h.Log.Messages));
    }

    [Fact]
    public async Task LoopbackHttpLoginAllowedAndMergesAnonymousTicket()
    {
        using var h = CreateHarness(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/register/anonimous")
                return FakeHttpMessageHandler.Json("{\"code\":200,\"cookie\":\"MUSIC_A_T=anon1\"}");
            return FakeHttpMessageHandler.Json("{\"code\":200,\"cookie\":\"MUSIC_U=user1\",\"account\":{\"id\":42}}");
        });

        Assert.True(await h.Session.LoginAnonymouslyAsync());
        var login = await h.Session.LoginWithPhoneAsync("13800000000", "pw");
        Assert.True(login.Success);
        Assert.Contains("MUSIC_A_T=anon1", h.Credentials.StoredCookie);
        Assert.Contains("MUSIC_U=user1", h.Credentials.StoredCookie);
    }

    [Fact]
    public async Task QrCheckPreservesBusinessStatesAndExtractsArrayCookie()
    {
        var step = 0;
        using var h = CreateHarness(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/login/qr/check")
            {
                step++;
                return step switch
                {
                    1 => FakeHttpMessageHandler.Json("{\"code\":801}"),
                    2 => FakeHttpMessageHandler.Json("{\"code\":802}"),
                    _ => FakeHttpMessageHandler.Json("{\"code\":803,\"cookie\":[\"MUSIC_U=u99; Path=/\",\"__csrf=c1\"]}")
                };
            }
            return FakeHttpMessageHandler.Json("{\"code\":200}");
        });

        var r1 = await h.Session.CheckQrStatusAsync("k1", TestContext.Current.CancellationToken);
        var r2 = await h.Session.CheckQrStatusAsync("k1", TestContext.Current.CancellationToken);
        var r3 = await h.Session.CheckQrStatusAsync("k1", TestContext.Current.CancellationToken);

        Assert.Equal(QrLoginState.Waiting, r1.Value.State);
        Assert.Equal(QrLoginState.Scanned, r2.Value.State);
        Assert.Equal(QrLoginState.Confirmed, r3.Value.State);
        Assert.Equal("MUSIC_U=u99; __csrf=c1", r3.Value.Cookie);
    }

    [Fact]
    public async Task SongSourceResolvesDegradeTrialRiskControlAndLegacyFallback()
    {
        var requestedUrls = new List<string>();
        using var h = CreateHarness(req =>
        {
            requestedUrls.Add(req.RequestUri!.PathAndQuery);
            if (req.RequestUri.AbsolutePath == "/song/url/v1")
            {
                if (req.RequestUri.Query.Contains("level=lossless"))
                    return FakeHttpMessageHandler.Json("{\"code\":-460,\"message\":\"风控\"}");
                if (req.RequestUri.Query.Contains("level=exhigh"))
                    return FakeHttpMessageHandler.Json("{\"code\":200,\"data\":[{\"url\":\"http://cdn/trial.mp3\",\"freeTrialInfo\":{\"start\":0,\"end\":30}}]}");
                return FakeHttpMessageHandler.Json("{\"code\":200,\"data\":[{\"url\":\"http://cdn/ok.mp3\"}]}");
            }
            return FakeHttpMessageHandler.Json("{\"code\":200,\"data\":[{\"url\":\"http://cdn/legacy.mp3\"}]}");
        });

        h.Settings.SetString("AudioQuality", "lossless");
        var (url, result) = await h.SongSource.ResolvePlaybackUrlAsync("100", TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal("http://cdn/legacy.mp3", url);
        Assert.Equal(2, requestedUrls.Count); // lossless(-460短路) -> legacy /song/url
    }

    [Fact]
    public async Task PlaylistDetailBatchesTrackIdsBy200PreservesOrderAndHandlesEmpty()
    {
        var detailBatches = new List<int>();
        using var h = CreateHarness(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/playlist/detail")
            {
                if (req.RequestUri.Query.Contains("id=empty"))
                    return FakeHttpMessageHandler.Json("{\"code\":200,\"playlist\":{\"id\":1,\"trackCount\":0,\"trackIds\":[]}}");

                var trackIdsJson = string.Join(",", Enumerable.Range(1, 450).Select(i => $"{{\"id\":{i}}}").Append("{\"id\":1}"));
                return FakeHttpMessageHandler.Json($"{{\"code\":200,\"playlist\":{{\"id\":2,\"trackCount\":450,\"trackIds\":[{trackIdsJson}]}}}}");
            }

            if (req.RequestUri.AbsolutePath == "/song/detail")
            {
                var idsParam = req.RequestUri.Query.Split("ids=")[1].Split('&')[0];
                var ids = idsParam.Split(',');
                detailBatches.Add(ids.Length);
                var songsJson = string.Join(",", ids.Reverse().Select(id => $"{{\"id\":{id},\"name\":\"S{id}\",\"dt\":1000}}"));
                return FakeHttpMessageHandler.Json($"{{\"code\":200,\"songs\":[{songsJson}]}}");
            }

            return FakeHttpMessageHandler.Json("{\"code\":200}");
        });

        var emptyRes = await h.Playlist.GetPlaylistDetailAsync("empty");
        Assert.True(emptyRes.Success);
        Assert.Empty(emptyRes.Value);

        var fullRes = await h.Playlist.GetPlaylistDetailAsync("full");
        Assert.True(fullRes.Success);
        Assert.Equal(new[] { 200, 200, 50 }, detailBatches);
        Assert.Equal(450, fullRes.Value.Count);
        Assert.Equal("1", fullRes.Value[0].Id);
        Assert.Equal("450", fullRes.Value[449].Id);
    }

    [Fact]
    public async Task LikeAndFavoritePlaylistStayInSync()
    {
        using var h = CreateHarness(req =>
        {
            return req.RequestUri!.AbsolutePath switch
            {
                "/login/cellphone" => FakeHttpMessageHandler.Json("{\"code\":200,\"cookie\":\"MUSIC_U=u\",\"account\":{\"id\":9}}"),
                "/user/playlist" => FakeHttpMessageHandler.Json("{\"code\":200,\"playlist\":[{\"id\":999,\"name\":\"我喜欢的音乐\",\"coverImgUrl\":\"\",\"trackCount\":1,\"specialType\":5,\"creator\":{\"nickname\":\"u\",\"userId\":9}}]}"),
                "/likelist" => FakeHttpMessageHandler.Json("{\"code\":200,\"ids\":[10,20]}"),
                "/like" => FakeHttpMessageHandler.Json("{\"code\":200}"),
                _ => FakeHttpMessageHandler.Json("{\"code\":200}")
            };
        });

        await h.Session.LoginWithPhoneAsync("138", "pw");
        await h.Library.EnsureLikedSongIdsLoadedAsync();
        Assert.True(h.Library.IsSongLiked("10"));
        Assert.True(await h.Library.IsFavoritePlaylistAsync("999"));

        SongLikeChangedEventArgs? fired = null;
        h.Library.SongLikeChanged += (_, e) => fired = e;
        var res = await h.Library.LikeSongAsync("30", true);
        Assert.True(res.Success);
        Assert.True(h.Library.IsSongLiked("30"));
        Assert.NotNull(fired);
        Assert.Equal("30", fired!.SongId);
        Assert.True(fired.IsLiked);
    }

    [Fact]
    public async Task RecommendationFallsBackOnUnauthenticatedAndEmptyIntelligence()
    {
        using var h = CreateHarness(req =>
        {
            return req.RequestUri!.AbsolutePath switch
            {
                "/personalized" => FakeHttpMessageHandler.Json("{\"code\":200,\"result\":[{\"id\":1,\"name\":\"P1\",\"picUrl\":\"\"}]}"),
                "/user/account" => FakeHttpMessageHandler.Json("{\"code\":200,\"account\":{\"id\":1}}"),
                "/user/detail" => FakeHttpMessageHandler.Json("{\"code\":200,\"profile\":{\"nickname\":\"u\",\"avatarUrl\":\"\"}}"),
                "/likelist" => FakeHttpMessageHandler.Json("{\"code\":200,\"ids\":[7]}"),
                "/user/playlist" => FakeHttpMessageHandler.Json("{\"code\":200,\"playlist\":[]}"),
                "/song/detail" => FakeHttpMessageHandler.Json("{\"code\":200,\"songs\":[{\"id\":7,\"name\":\"Fav7\",\"dt\":120000}]}"),
                _ => FakeHttpMessageHandler.Json("{\"code\":200}")
            };
        });

        var resPlaylists = await h.Recommendation.GetRecommendResourceAsync();
        Assert.True(resPlaylists.Success);
        Assert.Single(resPlaylists.Value);
        Assert.Equal("P1", resPlaylists.Value[0].Name);

        Assert.True(await h.Session.SetLoginCookieAsync("MUSIC_U=u"));
        var intel = await h.Recommendation.GetIntelligenceSongsAsync();
        Assert.True(intel.Success);
        Assert.Single(intel.Value);
        Assert.Equal("7", intel.Value[0].Id);
    }

    [Fact]
    public async Task DownloadServiceSanitizesNamesWritesAtomicallyAndDistinguishesIoErrors()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "CloudMusicDownloadTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var settings = new InMemorySettingsStore();
            settings.SetString("DownloadFolder", tempRoot);
            var log = new RecordingApiLog();
            var transport = new NeteaseHttpTransport(
                new ServerEndpoint(settings, log),
                new SessionCookieJar(new InMemoryCredentialStore(), log),
                settings,
                log,
                new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json("{\"code\":200,\"data\":[{\"url\":\"http://cdn.test/track.flac\"}]}")));

            using var dl = new DownloadService(
                new SongSourceResolver(transport, settings),
                settings,
                tempRoot,
                new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(new byte[] { 1, 2, 3, 4 })
                }));

            var song = new Song
            {
                Id = "55",
                Name = "A:B*C",
                Artists = new List<Artist> { new() { Name = "Art/1" } }
            };

            var res = await dl.DownloadSongAsync(song, TestContext.Current.CancellationToken);
            Assert.True(res.Success);
            Assert.True(File.Exists(res.Message));
            Assert.EndsWith("Art_1 - A_B_C.flac", res.Message);
            Assert.False(File.Exists(res.Message + ".download"));
            transport.Dispose();
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public void LrcParsersPreserveSimpleAndMultiTimestampTranslationSemantics()
    {
        var lrc = "[00:10.50][00:30.00]Chorus\n[00:05.00]Intro";
        var tlyric = "[00:10.52]副歌\n[00:30.00]副歌2";

        var simple = LrcParser.ParseSimple(lrc);
        Assert.Equal(2, simple.Count);
        Assert.Equal("Intro", simple[0].Text);
        // ParseSimple 只取第一个 ]
        Assert.Equal("[00:30.00]Chorus", simple[1].Text);

        var rich = LrcParser.ParseWithTranslations(lrc, tlyric);
        Assert.True(rich.Count == 3);
        Assert.Equal("Intro", rich[0].Text);
        Assert.Equal("Chorus", rich[1].Text);
        Assert.Equal("副歌", rich[1].Translation);
        Assert.Equal("Chorus", rich[2].Text);
        Assert.Equal("副歌2", rich[2].Translation);
    }

    [Fact]
    public async Task ServicesReturnFailInsteadOfOkEmptyOnNetworkAndParseErrors()
    {
        // 1. 断网场景
        using (var offline = CreateHarness(_ => throw new HttpRequestException("offline")))
        {
            await offline.Session.SetLoginCookieAsync("MUSIC_U=test");
            var cloudRes = await new CloudService(offline.Transport, offline.Session).GetCloudSongsAsync();
            Assert.False(cloudRes.Success);
            Assert.Equal(ApiErrorKind.Network, cloudRes.Status.Error);

            var dailyRes = await offline.Recommendation.GetDailyRecommendSongsAsync();
            Assert.False(dailyRes.Success);
            Assert.Equal(ApiErrorKind.Network, dailyRes.Status.Error);

            var detailRes = await offline.Playlist.GetPlaylistDetailAsync("123");
            Assert.False(detailRes.Success);
            Assert.Equal(ApiErrorKind.Network, detailRes.Status.Error);

            var recentRes = await new ProfileService(offline.Transport).GetRecentSongsAsync();
            Assert.False(recentRes.Success);
            Assert.Equal(ApiErrorKind.Network, recentRes.Status.Error);

            var searchRes = await new SearchService(offline.Transport, offline.Library, offline.Quality).SearchSongsAsync("test");
            Assert.False(searchRes.Success);
            Assert.Equal(ApiErrorKind.Network, searchRes.Status.Error);
        }

        // 2. 坏 JSON 与结构解析异常场景（HTTP 200 但字段类型异常触发解析异常）
        using (var badSchema = CreateHarness(req =>
        {
            return req.RequestUri!.AbsolutePath switch
            {
                "/user/account" => FakeHttpMessageHandler.Json("{\"code\":200,\"account\":{\"id\":7}}"),
                "/user/detail" => FakeHttpMessageHandler.Json("{\"code\":200,\"profile\":{\"nickname\":\"u\",\"avatarUrl\":\"\"}}"),
                "/likelist" => FakeHttpMessageHandler.Json("{\"code\":200,\"ids\":[1]}"),
                "/user/playlist" => FakeHttpMessageHandler.Json("{\"code\":200,\"playlist\":\"not-an-array\"}"),
                "/song/detail" => FakeHttpMessageHandler.Json("{\"code\":200,\"songs\":\"not-an-array\"}"),
                "/user/cloud" => FakeHttpMessageHandler.Json("{\"code\":200,\"data\":\"not-an-array\"}"),
                "/recommend/songs" => FakeHttpMessageHandler.Json("{\"code\":200,\"data\":{\"dailySongs\":\"not-an-array\"}}"),
                "/personalized" => FakeHttpMessageHandler.Json("{\"code\":200,\"result\":\"not-an-array\"}"),
                _ => FakeHttpMessageHandler.Json("{\"code\":200}")
            };
        }))
        {
            await badSchema.Session.SetLoginCookieAsync("MUSIC_U=test");

            var likedRes = await badSchema.Library.GetLikedSongsAsync();
            Assert.False(likedRes.Success);
            Assert.Equal(ApiErrorKind.InvalidResponse, likedRes.Status.Error);

            var userPlaylistsRes = await badSchema.Playlist.GetUserPlaylistsAsync(7);
            Assert.False(userPlaylistsRes.Success);
            Assert.Equal(ApiErrorKind.InvalidResponse, userPlaylistsRes.Status.Error);

            var cloudRes = await new CloudService(badSchema.Transport, badSchema.Session).GetCloudSongsAsync();
            Assert.False(cloudRes.Success);
            Assert.Equal(ApiErrorKind.InvalidResponse, cloudRes.Status.Error);

            var dailyRes = await badSchema.Recommendation.GetDailyRecommendSongsAsync();
            Assert.False(dailyRes.Success);
            Assert.Equal(ApiErrorKind.InvalidResponse, dailyRes.Status.Error);

            var recPlaylistsRes = await badSchema.Recommendation.GetRecommendedPlaylistsAsync();
            Assert.False(recPlaylistsRes.Success);
            Assert.Equal(ApiErrorKind.InvalidResponse, recPlaylistsRes.Status.Error);
        }
    }
}
