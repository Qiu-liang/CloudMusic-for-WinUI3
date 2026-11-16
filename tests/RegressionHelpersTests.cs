using music.Models;
using music.Services;
using Xunit;

namespace music.Tests;

public class RegressionHelpersTests
{
    [Fact]
    public void LikedIdsAreSplitWithoutDroppingItems()
    {
        var ids = Enumerable.Range(1, 501).Select(i => i.ToString()).ToList();
        var batches = ApiBatching.Chunk(ids, 200);

        Assert.Equal(new[] { 200, 200, 101 }, batches.Select(batch => batch.Count));
        Assert.Equal(ids, batches.SelectMany(batch => batch));
    }

    [Theory]
    [InlineData(200, "{\"code\":400,\"message\":\"bad\"}", false, ApiErrorKind.Business)]
    [InlineData(200, "{\"code\":301,\"message\":\"login\"}", false, ApiErrorKind.Authentication)]
    [InlineData(200, "{\"result\":{}}", true, ApiErrorKind.None)]
    [InlineData(500, "{}", false, ApiErrorKind.Http)]
    public void ApiResponseDistinguishesTransportAndBusinessFailure(
        int status, string body, bool expectedSuccess, ApiErrorKind expectedError)
    {
        var result = ApiResult.FromHttpResponse(status, body);
        Assert.Equal(expectedSuccess, result.Success);
        Assert.Equal(expectedError, result.Error);
    }

    [Fact]
    public void UploadRequiresBusinessCode()
    {
        var result = ApiResult.FromHttpResponse(200, "{\"data\":{}}", requireBusinessCode: true);
        Assert.False(result.Success);
    }

    [Fact]
    public void OutOfOrderLyricsCannotApplyAfterNewerRequest()
    {
        var gate = new LatestLyricRequest();
        var oldRequest = gate.Next("old");
        var newRequest = gate.Next("new");

        Assert.False(gate.IsCurrent(oldRequest, "old", "new"));
        Assert.True(gate.IsCurrent(newRequest, "new", "new"));
    }

    [Fact]
    public void ProgrammaticProgressAndCaptureLossDoNotSeek()
    {
        var state = new ProgressDragState();
        state.ValueChanged(50, TimeSpan.FromSeconds(100));
        Assert.Null(state.PointerReleased());

        state.PointerPressed();
        state.ValueChanged(25, TimeSpan.FromSeconds(100));
        Assert.Equal(TimeSpan.FromSeconds(25), state.PointerCaptureLost());
        Assert.False(state.IsDragging);
    }

    [Fact]
    public void ReleaseCommitsLatestDragPosition()
    {
        var state = new ProgressDragState();
        state.PointerPressed();
        state.ValueChanged(10, TimeSpan.FromSeconds(100));
        state.ValueChanged(80, TimeSpan.FromSeconds(100));

        Assert.Equal(TimeSpan.FromSeconds(80), state.PointerReleased());
        Assert.Null(state.PointerReleased());
    }

    [Theory]
    [InlineData(null, ThemeMode.System)]
    [InlineData("Light", ThemeMode.Light)]
    [InlineData("Dark", ThemeMode.Dark)]
    [InlineData("Unknown", ThemeMode.System)]
    public void SavedThemeIsRestored(string? saved, ThemeMode expected)
    {
        Assert.Equal(expected, ThemeSettings.Parse(saved));
    }

    [Fact]
    public void SystemThemeUsesActualSystemDarkState()
    {
        Assert.True(ThemeSettings.IsDark(ThemeMode.System, true));
        Assert.False(ThemeSettings.IsDark(ThemeMode.System, false));
        Assert.False(ThemeSettings.IsDark(ThemeMode.Light, true));
    }

    [Fact]
    public void FindActiveLyricIndexHandlesFastPathSeekAndDuplicateTimestamps()
    {
        var lyrics = new List<LyricLine>
        {
            new() { Time = TimeSpan.FromSeconds(5), Text = "L0" },
            new() { Time = TimeSpan.FromSeconds(10), Text = "L1" },
            new() { Time = TimeSpan.FromSeconds(10), Text = "L2" },
            new() { Time = TimeSpan.FromSeconds(20), Text = "L3" },
            new() { Time = TimeSpan.FromSeconds(35), Text = "L4" },
        };

        Assert.Equal(-1, LyricWindowHelpers.FindActiveLyricIndex(lyrics, 4999, -1));
        Assert.Equal(0, LyricWindowHelpers.FindActiveLyricIndex(lyrics, 5000, -1));
        Assert.Equal(2, LyricWindowHelpers.FindActiveLyricIndex(lyrics, 10000, 0));
        Assert.Equal(2, LyricWindowHelpers.FindActiveLyricIndex(lyrics, 15000, 2));
        Assert.Equal(4, LyricWindowHelpers.FindActiveLyricIndex(lyrics, 40000, 0));
        // Seek backward
        Assert.Equal(0, LyricWindowHelpers.FindActiveLyricIndex(lyrics, 7000, 4));
    }

    [Fact]
    public void HitTestLyricLineFindsMatchingLineOrNegativeOne()
    {
        var offsets = new float[] { 0f, 90f, 180f };
        var heights = new float[] { 80f, 80f, 80f };

        Assert.Equal(-1, LyricWindowHelpers.HitTestLyricLine(offsets, heights, -1f));
        Assert.Equal(0, LyricWindowHelpers.HitTestLyricLine(offsets, heights, 0f));
        Assert.Equal(0, LyricWindowHelpers.HitTestLyricLine(offsets, heights, 80f));
        Assert.Equal(-1, LyricWindowHelpers.HitTestLyricLine(offsets, heights, 85f));
        Assert.Equal(1, LyricWindowHelpers.HitTestLyricLine(offsets, heights, 120f));
        Assert.Equal(2, LyricWindowHelpers.HitTestLyricLine(offsets, heights, 260f));
        Assert.Equal(-1, LyricWindowHelpers.HitTestLyricLine(offsets, heights, 261f));
    }

    [Fact]
    public void CriticallyDampedSpringConvergesWithoutOvershoot()
    {
        float val = 0f;
        float vel = 0f;
        bool settled = false;

        for (int i = 0; i < 60 && !settled; i++)
        {
            (val, vel, settled) = LyricWindowHelpers.StepCriticallyDampedSpring(
                val, 100f, vel, omega: 16f, dt: 1f / 60f, posEpsilon: 0.3f, velEpsilon: 2f);
            Assert.InRange(val, 0f, 100.001f);
        }

        Assert.True(settled);
        Assert.Equal(100f, val);
        Assert.Equal(0f, vel);
    }

    [Fact]
    public void Http400PreservesBackendJsonErrorDetail()
    {
        var result = ApiResult.FromHttpResponse(400, "{\"code\":-460,\"msg\":\"Cheating\"}");
        Assert.False(result.Success);
        Assert.Equal(ApiErrorKind.Http, result.Error);
        Assert.Equal(400, result.Code);
        Assert.Contains("-460", result.Message);
        Assert.Contains("Cheating", result.Message);

        var authResult = ApiResult.FromHttpResponse(400, "{\"code\":301,\"message\":\"需要登录\"}");
        Assert.Equal(ApiErrorKind.Authentication, authResult.Error);
        Assert.Contains("需要登录", authResult.Message);
    }

    [Theory]
    [InlineData("jymaster", new[] { "jymaster", "exhigh", "standard" })]
    [InlineData("lossless", new[] { "lossless", "exhigh", "standard" })]
    [InlineData("exhigh", new[] { "exhigh", "standard" })]
    [InlineData("higher", new[] { "higher", "standard" })]
    [InlineData("standard", new[] { "standard" })]
    [InlineData(null, new[] { "standard" })]
    public void QualityFallbackLevelsDegradeToStandard(string? requested, string[] expected)
    {
        var levels = SongUrlPolicy.BuildQualityFallbackLevels(requested);
        Assert.Equal(expected, levels);
    }

    [Theory]
    [InlineData("{\"code\":200,\"data\":[{\"id\":1,\"url\":\"https://example.com/a.mp3\"}]}", "https://example.com/a.mp3")]
    [InlineData("{\"code\":200,\"data\":[]}", "")]
    [InlineData("{\"code\":200,\"data\":[{\"id\":1,\"url\":null}]}", "")]
    [InlineData("not-json", "")]
    [InlineData("", "")]
    public void ExtractSongUrlHandlesValidAndEmptyPayloadsSafely(string json, string expected)
    {
        Assert.Equal(expected, SongUrlPolicy.ExtractSongUrl(json));
    }

    [Fact]
    public void RequestCookieHeaderEnsuresOsPcAndSanitizesNewlines()
    {
        var emptyHeader = CookieStringParser.BuildRequestHeader(new Dictionary<string, string>());
        Assert.Equal("os=pc", emptyHeader);

        var withSession = CookieStringParser.BuildRequestHeader(new Dictionary<string, string>
        {
            ["MUSIC_U"] = "abc\r\n",
            ["__csrf"] = "xyz"
        });
        Assert.Contains("MUSIC_U=abc", withSession);
        Assert.Contains("__csrf=xyz", withSession);
        Assert.Contains("os=pc", withSession);

        var withCustomOs = CookieStringParser.BuildRequestHeader(new Dictionary<string, string>
        {
            ["OS"] = "pc",
            ["MUSIC_U"] = "abc"
        });
        Assert.DoesNotContain("os=pc", withCustomOs);
        Assert.Contains("OS=pc", withCustomOs);
    }

    [Fact]
    public void EvaluateVipStatusDistinguishesExpiredFromActiveVip()
    {
        const long now = 1_750_000_000_000L;

        // 曾开通过 VIP（redVipLevel > 0），但所有会员包均已过期 -> false
        var expiredVipJson = """
        {
          "code": 200,
          "data": {
            "redVipLevel": 3,
            "now": 1750000000000,
            "associator": { "vipCode": 100, "rights": true, "expireTime": 1700000000000 },
            "musicPackage": { "vipCode": 220, "rights": true, "expireTime": 1700000000000 },
            "redplus": { "vipCode": 300, "rights": false, "expireTime": 0 }
          }
        }
        """;
        Assert.False(SongUrlPolicy.EvaluateVipStatus(expiredVipJson, now));

        // 从未开通 VIP -> false
        var nonVipJson = """
        {
          "code": 200,
          "data": {
            "redVipLevel": 0,
            "now": 1750000000000,
            "associator": { "expireTime": 0 }
          }
        }
        """;
        Assert.False(SongUrlPolicy.EvaluateVipStatus(nonVipJson, now));

        // 黑胶 VIP 未过期 -> true
        var activeAssociatorJson = """
        {
          "code": 200,
          "data": {
            "redVipLevel": 3,
            "now": 1750000000000,
            "associator": { "vipCode": 100, "rights": true, "expireTime": 1800000000000 },
            "musicPackage": { "vipCode": 220, "rights": true, "expireTime": 1700000000000 }
          }
        }
        """;
        Assert.True(SongUrlPolicy.EvaluateVipStatus(activeAssociatorJson, now));

        // 音乐包未过期 -> true
        var activeMusicPackageJson = """
        {
          "code": 200,
          "data": {
            "redVipLevel": 0,
            "associator": { "expireTime": 0 },
            "musicPackage": { "vipCode": 220, "expireTime": 1800000000000 }
          }
        }
        """;
        Assert.True(SongUrlPolicy.EvaluateVipStatus(activeMusicPackageJson, now));
    }

    [Fact]
    public void SongQualityPrivilegeEvaluatesVipRequirementFromApiChargeListAndFlLevel()
    {
        // 场景 1：fee=8 且 API 返回 flLevel="standard"，chargeInfoList 中 192k/320k/无损均为 chargeType=1
        // 此时连 higher、exhigh 也由 API 判定为需要 VIP，仅 standard 免费
        var fee8StandardOnlyJson = """
        {
          "songs": [
            {
              "id": 1001,
              "name": "仅标准免费歌曲",
              "fee": 8,
              "l": { "br": 128000 },
              "m": { "br": 192000 },
              "h": { "br": 320000 },
              "sq": { "br": 999000 },
              "jm": { "br": 1999000 }
            }
          ],
          "privileges": [
            {
              "id": 1001,
              "fee": 8,
              "payed": 0,
              "flLevel": "standard",
              "plLevel": "standard",
              "maxBrLevel": "jymaster",
              "chargeInfoList": [
                { "rate": 128000, "chargeType": 0 },
                { "rate": 192000, "chargeType": 1 },
                { "rate": 320000, "chargeType": 1 },
                { "rate": 999000, "chargeType": 1 }
              ]
            }
          ]
        }
        """;

        var priv1 = SongQualityPrivilege.FromSongDetailJson(fee8StandardOnlyJson, "1001");
        Assert.NotNull(priv1);
        Assert.False(priv1!.RequiresVip("standard"));
        Assert.True(priv1.RequiresVip("higher"));
        Assert.True(priv1.RequiresVip("exhigh"));
        Assert.True(priv1.RequiresVip("lossless"));
        Assert.True(priv1.RequiresVip("jymaster"));

        // 非 VIP 用户尝试选择 exhigh，API 判定不可用并退回 standard
        Assert.False(priv1.CanUserPlay("exhigh", isVip: false));
        Assert.Equal("standard", priv1.ResolveFallbackQuality("exhigh", isVip: false, previousQuality: "exhigh"));

        // 场景 2：fee=0 免费无损歌曲，API 返回 flLevel="lossless"，chargeInfoList 中 999000 的 chargeType=0
        var freeLosslessJson = """
        {
          "songs": [
            {
              "id": 1002,
              "name": "免费无损歌曲",
              "fee": 0,
              "l": { "br": 128000 },
              "h": { "br": 320000 },
              "sq": { "br": 999000 }
            }
          ],
          "privileges": [
            {
              "id": 1002,
              "fee": 0,
              "payed": 0,
              "flLevel": "lossless",
              "plLevel": "lossless",
              "maxBrLevel": "lossless",
              "chargeInfoList": [
                { "rate": 128000, "chargeType": 0 },
                { "rate": 192000, "chargeType": 0 },
                { "rate": 320000, "chargeType": 0 },
                { "rate": 999000, "chargeType": 0 }
              ]
            }
          ]
        }
        """;

        var priv2 = SongQualityPrivilege.FromSongDetailJson(freeLosslessJson, "1002");
        Assert.NotNull(priv2);
        Assert.False(priv2!.RequiresVip("standard"));
        Assert.False(priv2.RequiresVip("exhigh"));
        Assert.False(priv2.RequiresVip("lossless"));
        Assert.True(priv2.CanUserPlay("lossless", isVip: false));
        Assert.Equal("lossless", priv2.ResolveFallbackQuality("lossless", isVip: false, previousQuality: "standard"));
    }

    [Fact]
    public void SongQualityPrivilegeResolvesFallbackUsingApiFlLevelAndPreviousQuality()
    {
        var exhighFreeJson = """
        {
          "songs": [{ "id": 1003, "name": "极高免费无损VIP", "fee": 8 }],
          "privileges": [
            {
              "id": 1003,
              "fee": 8,
              "payed": 0,
              "flLevel": "exhigh",
              "plLevel": "exhigh",
              "maxBrLevel": "jymaster",
              "chargeInfoList": [
                { "rate": 128000, "chargeType": 0 },
                { "rate": 192000, "chargeType": 0 },
                { "rate": 320000, "chargeType": 0 },
                { "rate": 999000, "chargeType": 1 }
              ]
            }
          ]
        }
        """;

        var priv = SongQualityPrivilege.FromSongDetailJson(exhighFreeJson, "1003")!;
        Assert.False(priv.RequiresVip("standard"));
        Assert.False(priv.RequiresVip("higher"));
        Assert.False(priv.RequiresVip("exhigh"));
        Assert.True(priv.RequiresVip("lossless"));
        Assert.True(priv.RequiresVip("hires"));
        Assert.True(priv.RequiresVip("jymaster"));

        // 非 VIP 选择 lossless，优先退回此前可用的 higher
        Assert.Equal("higher", priv.ResolveFallbackQuality("lossless", isVip: false, previousQuality: "higher"));
        // 非 VIP 选择 jymaster，若此前缓存的也是需 VIP 的 lossless，则退回 API 返回的 plLevel/flLevel ("exhigh")
        Assert.Equal("exhigh", priv.ResolveFallbackQuality("jymaster", isVip: false, previousQuality: "lossless"));
        // VIP 选择 jymaster，正常保留
        Assert.Equal("jymaster", priv.ResolveFallbackQuality("jymaster", isVip: true, previousQuality: "standard"));
    }

    [Fact]
    public void ExtractSongUrlDataParsesTrialAndLevelFromApiPayload()
    {
        var trialPayload = """
        {
          "code": 200,
          "data": [
            {
              "id": 1983427941,
              "url": "https://m8.music.126.net/trial.mp3",
              "level": "standard",
              "fee": 1,
              "payed": 0,
              "freeTrialInfo": { "start": 0, "end": 30 },
              "freeTrialPrivilege": { "cannotListenReason": 1 }
            }
          ]
        }
        """;

        var data = SongUrlPolicy.ExtractSongUrlData(trialPayload);
        Assert.NotNull(data);
        Assert.Equal("https://m8.music.126.net/trial.mp3", data!.Url);
        Assert.Equal("standard", data.ActualLevel);
        Assert.Equal(1, data.Fee);
        Assert.Equal(0, data.Payed);
        Assert.True(data.IsFreeTrial);
        Assert.Equal(1, data.CannotListenReason);
    }

    [Fact]
    public void ExtractSongUrlDataToleratesNullNumericFieldsInFreeSongPayload()
    {
        var freeSongPayload = """
        {
          "code": 200,
          "data": [
            {
              "id": 123456,
              "url": "https://m8.music.126.net/free.mp3",
              "level": null,
              "fee": null,
              "payed": null,
              "freeTrialInfo": null,
              "freeTrialPrivilege": {
                "resConsumable": false,
                "userConsumable": false,
                "listenType": null,
                "cannotListenReason": null
              }
            }
          ]
        }
        """;

        var data = SongUrlPolicy.ExtractSongUrlData(freeSongPayload);
        Assert.NotNull(data);
        Assert.Equal("https://m8.music.126.net/free.mp3", data!.Url);
        Assert.Equal(0, data.Fee);
        Assert.Equal(0, data.Payed);
        Assert.False(data.IsFreeTrial);
        Assert.Null(data.CannotListenReason);
        Assert.Equal("https://m8.music.126.net/free.mp3", SongUrlPolicy.ExtractSongUrl(freeSongPayload));
    }

    [Fact]
    public void NullNumericFieldsDoNotThrowInPrivilegeVipOrApiResultParsers()
    {
        var detailWithNulls = """
        {
          "songs": [{ "id": 2001, "name": "Free", "fee": null }],
          "privileges": [
            {
              "id": 2001,
              "fee": null,
              "payed": null,
              "fl": null,
              "pl": null,
              "maxbr": null,
              "flLevel": "exhigh",
              "plLevel": "exhigh",
              "chargeInfoList": [
                { "rate": null, "chargeType": null },
                { "rate": 320000, "chargeType": 0 }
              ]
            }
          ]
        }
        """;

        var priv = SongQualityPrivilege.FromSongDetailJson(detailWithNulls, "2001");
        Assert.NotNull(priv);
        Assert.False(priv!.RequiresVip("exhigh"));

        var vipNullJson = """
        {
          "code": 200,
          "data": {
            "now": null,
            "associator": { "expireTime": null, "rights": true },
            "musicPackage": { "expireTime": 2000000000000, "rights": null }
          }
        }
        """;
        Assert.True(SongUrlPolicy.EvaluateVipStatus(vipNullJson, fallbackNowMs: 1700000000000));

        var nullCodeOk = ApiResult.FromHttpResponse(200, "{\"code\":null,\"msg\":\"weird\"}");
        Assert.False(nullCodeOk.Success);
        Assert.Equal(ApiErrorKind.InvalidResponse, nullCodeOk.Error);

        var nullCodeHttp = ApiResult.FromHttpResponse(400, "{\"code\":null,\"msg\":\"bad\"}");
        Assert.False(nullCodeHttp.Success);
        Assert.Equal(ApiErrorKind.Http, nullCodeHttp.Error);
    }

    [Fact]
    public void RiskControl460TriggersImmediateV1ShortCircuitAndActionableGuidance()
    {
        var v1Risk = ApiResult.FromHttpResponse(400, "{\"code\":-460,\"message\":\"检测到您的网络环境存在风险，请稍后再试\"}");
        Assert.True(v1Risk.IsRiskControl);
        Assert.Equal(-460, v1Risk.BusinessCode);
        Assert.Equal(400, v1Risk.Code);
        Assert.True(SongUrlPolicy.ShouldShortCircuitV1Fallback(v1Risk));

        var nonRiskBusiness = new ApiResult(false, ApiErrorKind.Business, "无版权", 404, 404);
        Assert.False(SongUrlPolicy.ShouldShortCircuitV1Fallback(nonRiskBusiness));

        var finalFailure = SongUrlPolicy.ResolveFinalPlaybackUrlFailure(v1Risk, v1Risk);
        Assert.Contains("-460", finalFailure.DisplayMessage);
        Assert.Contains("代理或 VPN", finalFailure.DisplayMessage);
    }

    [Fact]
    public void DownloadFolderPolicyDefaultsToCloudMusicSubfolderInWindowsMusic()
    {
        var defaultFromMyMusic = DownloadFolderPolicy.GetDefaultDownloadFolder(
            myMusicFolder: @"C:\Users\Alice\Music\",
            userProfileFolder: @"C:\Users\Alice",
            localAppFolder: @"C:\AppData");
        Assert.Equal(@"C:\Users\Alice\Music\CloudMusic for WinUI", defaultFromMyMusic);

        var fallbackToProfile = DownloadFolderPolicy.GetDefaultDownloadFolder(
            myMusicFolder: "",
            userProfileFolder: @"C:\Users\Alice",
            localAppFolder: @"C:\AppData");
        Assert.Equal(@"C:\Users\Alice\Music\CloudMusic for WinUI", fallbackToProfile);

        var fallbackToLocal = DownloadFolderPolicy.GetDefaultDownloadFolder(
            myMusicFolder: "",
            userProfileFolder: "",
            localAppFolder: @"C:\AppData");
        Assert.Equal(@"C:\AppData\Downloads", fallbackToLocal);
    }

    [Fact]
    public void DownloadFolderPolicyValidatesAndPersistsCustomPathsSafely()
    {
        var defaultFolder = @"C:\Users\Alice\Music\CloudMusic for WinUI";

        // Valid absolute path normalizes trailing slashes
        Assert.Equal(@"D:\MyMusic\Cloud", DownloadFolderPolicy.ResolveDownloadFolder(@"  D:\MyMusic\Cloud\  ", defaultFolder));
        // Relative, empty, or invalid paths fall back to default
        Assert.Equal(defaultFolder, DownloadFolderPolicy.ResolveDownloadFolder(null, defaultFolder));
        Assert.Equal(defaultFolder, DownloadFolderPolicy.ResolveDownloadFolder("   ", defaultFolder));
        Assert.Equal(defaultFolder, DownloadFolderPolicy.ResolveDownloadFolder(@"relative\folder", defaultFolder));
        Assert.Equal(defaultFolder, DownloadFolderPolicy.ResolveDownloadFolder("D:\\bad\0path", defaultFolder));

        // Case-insensitive default equality
        Assert.True(DownloadFolderPolicy.IsDefaultDownloadFolder(@"c:\users\alice\music\cloudmusic for winui\", defaultFolder));
        Assert.False(DownloadFolderPolicy.IsDefaultDownloadFolder(@"D:\CustomMusic", defaultFolder));

        // Settings dictionary save/reset roundtrip
        var settings = new Dictionary<string, object>();
        var actualDefault = DownloadFolderPolicy.GetCurrentDownloadFolder(settings);
        Assert.Equal(DownloadFolderPolicy.GetDefaultDownloadFolder(), actualDefault);

        DownloadFolderPolicy.SaveDownloadFolder(settings, @"D:\CustomMusic\");
        Assert.Equal(@"D:\CustomMusic", settings[DownloadFolderPolicy.SettingsKey]);
        Assert.Equal(@"D:\CustomMusic", DownloadFolderPolicy.GetCurrentDownloadFolder(settings));

        // Saving default removes override key
        DownloadFolderPolicy.SaveDownloadFolder(settings, actualDefault + @"\");
        Assert.False(settings.ContainsKey(DownloadFolderPolicy.SettingsKey));

        // Reset removes override key
        DownloadFolderPolicy.SaveDownloadFolder(settings, @"D:\CustomMusic");
        var reset = DownloadFolderPolicy.ResetDownloadFolder(settings);
        Assert.False(settings.ContainsKey(DownloadFolderPolicy.SettingsKey));
        Assert.Equal(actualDefault, reset);
    }

    [Fact]
    public void MarqueeLayoutPolicyScrollsOnlyWhenTextExceedsViewportAndComputesConstantSpeed()
    {
        // Fits within viewport or within subpixel epsilon -> static
        Assert.False(MarqueeLayoutPolicy.ShouldScroll(120, 160));
        Assert.False(MarqueeLayoutPolicy.ShouldScroll(160.3, 160));
        Assert.False(MarqueeLayoutPolicy.ShouldScroll(160, 0));

        var staticMetrics = MarqueeLayoutPolicy.ComputeMetrics(120, 160);
        Assert.False(staticMetrics.ShouldScroll);
        Assert.Equal(0, staticMetrics.TravelDistance);
        Assert.Equal(TimeSpan.Zero, staticMetrics.TotalDuration);

        // Overflowing text -> continuous loop with gap and constant speed
        Assert.True(MarqueeLayoutPolicy.ShouldScroll(280, 160));
        var metrics = MarqueeLayoutPolicy.ComputeMetrics(
            textWidth: 284,
            viewportWidth: 160,
            gap: 36,
            speedPxPerSecond: 32,
            initialPauseSeconds: 1.5);
        Assert.True(metrics.ShouldScroll);
        Assert.Equal(320, metrics.TravelDistance, precision: 3);
        Assert.Equal(TimeSpan.FromSeconds(1.5), metrics.InitialPause);
        Assert.Equal(TimeSpan.FromSeconds(10), metrics.ScrollDuration);
        Assert.Equal(TimeSpan.FromSeconds(11.5), metrics.TotalDuration);
    }

    [Theory]
    [InlineData("/likelist?uid=123", 1700000000000L, "/likelist?uid=123&timestamp=1700000000000")]
    [InlineData("/playlist/detail?id=456", 1700000000000L, "/playlist/detail?id=456&timestamp=1700000000000")]
    [InlineData("/recommend/songs", 1700000000000L, "/recommend/songs?timestamp=1700000000000")]
    [InlineData("", 1700000000000L, "")]
    public void AppendTimestampPreservesExistingQueryParameters(string url, long timestampMs, string expected)
    {
        Assert.Equal(expected, LikedMusicSyncHelper.AppendTimestamp(url, timestampMs));
    }

    [Fact]
    public void ExtractPlaylistTrackIdsPreservesOrderDeduplicatesAndSupportsLargePlaylists()
    {
        var trackEntries = Enumerable.Range(1, 1205)
            .Select(i => $"{{\"id\":{i}}}")
            .Concat(new[] { "{\"id\":5}", "{\"id\":null}", "{}" });
        var json = $$"""
        {
          "code": 200,
          "playlist": {
            "id": 999,
            "trackIds": [{{string.Join(",", trackEntries)}}]
          }
        }
        """;

        var ids = LikedMusicSyncHelper.ExtractPlaylistTrackIds(json);
        Assert.Equal(1205, ids.Count);
        Assert.Equal("1", ids[0]);
        Assert.Equal("1205", ids[^1]);

        var batches = ApiBatching.Chunk(ids, 200);
        Assert.Equal(7, batches.Count);
        Assert.Equal(ids, batches.SelectMany(b => b));

        Assert.Empty(LikedMusicSyncHelper.ExtractPlaylistTrackIds("not-json"));
        Assert.Empty(LikedMusicSyncHelper.ExtractPlaylistTrackIds("{\"code\":200,\"playlist\":{\"trackIds\":null}}"));
    }

    [Fact]
    public void ExtractLikedIdsAndApplyLikeChangeKeepInMemorySetInSync()
    {
        var likelistJson = """
        {
          "code": 200,
          "ids": [101, "102", 101, null, 103]
        }
        """;

        var ids = LikedMusicSyncHelper.ExtractLikedIds(likelistJson);
        Assert.Equal(new[] { "101", "102", "103" }, ids);

        var likedSet = new HashSet<string>(ids, StringComparer.Ordinal);
        Assert.True(LikedMusicSyncHelper.ApplyLikeChange(likedSet, "104", true));
        Assert.False(LikedMusicSyncHelper.ApplyLikeChange(likedSet, "104", true));
        Assert.Contains("104", likedSet);

        Assert.True(LikedMusicSyncHelper.ApplyLikeChange(likedSet, "102", false));
        Assert.False(LikedMusicSyncHelper.ApplyLikeChange(likedSet, "102", false));
        Assert.DoesNotContain("102", likedSet);

        Assert.False(LikedMusicSyncHelper.ApplyLikeChange(likedSet, "", true));
    }
}
