using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using music.Core.Netease;
using music.Services;
using Xunit;

namespace music.Tests;

public class NeteaseHttpTransportTests
{
    [Fact]
    public async Task AppendsDeviceIdAndPersistsGeneratedDeviceId()
    {
        var settings = new InMemorySettingsStore();
        var log = new RecordingApiLog();
        var creds = new InMemoryCredentialStore();
        var endpoint = new ServerEndpoint(settings, log);
        var jar = new SessionCookieJar(creds, log);
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json("{\"code\":200}"));
        using var transport = new NeteaseHttpTransport(endpoint, jar, settings, log, handler);

        await transport.GetAsync("/test/ping", TestContext.Current.CancellationToken);
        await transport.GetAsync("/test/ping?foo=1", TestContext.Current.CancellationToken);

        var requests = handler.Requests.ToArray();
        Assert.Equal(2, requests.Length);
        var deviceId = settings.GetString("DeviceId");
        Assert.False(string.IsNullOrEmpty(deviceId));
        Assert.Equal($"/test/ping?deviceId={deviceId}", requests[0].RequestUri!.PathAndQuery);
        Assert.Equal($"/test/ping?foo=1&deviceId={deviceId}", requests[1].RequestUri!.PathAndQuery);
        Assert.Equal(TimeSpan.FromSeconds(30), transport.Timeout);
    }

    [Fact]
    public async Task LimitsConcurrencyToFourInFlightRequests()
    {
        var settings = new InMemorySettingsStore();
        var log = new RecordingApiLog();
        var creds = new InMemoryCredentialStore();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reachedFour = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;

        var handler = new FakeHttpMessageHandler(async (_, ct) =>
        {
            if (Interlocked.Increment(ref count) == 4)
                reachedFour.TrySetResult();
            await gate.Task.WaitAsync(ct);
            return FakeHttpMessageHandler.Json("{\"code\":200}");
        });

        using var transport = new NeteaseHttpTransport(
            new ServerEndpoint(settings, log),
            new SessionCookieJar(creds, log),
            settings,
            log,
            handler);

        var tasks = Enumerable.Range(0, 8).Select(i => transport.GetAsync($"/item?i={i}", TestContext.Current.CancellationToken)).ToArray();
        await reachedFour.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await Task.Delay(30, TestContext.Current.CancellationToken);
        Assert.Equal(4, handler.MaxObservedConcurrency);
        gate.SetResult();
        await Task.WhenAll(tasks);
        Assert.Equal(4, handler.MaxObservedConcurrency);
    }

    [Fact]
    public async Task SetCookieUpdatesJarFiltersAttributesDeletesEmptyAndDoesNotLogSecretValues()
    {
        var settings = new InMemorySettingsStore();
        var log = new RecordingApiLog();
        var creds = new InMemoryCredentialStore { StoredCookie = "MUSIC_U=oldtoken; MUSIC_A_T=devticket" };
        var jar = new SessionCookieJar(creds, log);
        jar.LoadFromStore();

        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(
            "{\"code\":200}",
            HttpStatusCode.OK,
            "MUSIC_U=secret_new_token; Path=/; HttpOnly",
            "MUSIC_A_T=; Path=/; Max-Age=0"));

        using var transport = new NeteaseHttpTransport(
            new ServerEndpoint(settings, log), jar, settings, log, handler);

        await transport.GetAsync("/user/account", TestContext.Current.CancellationToken);
        await transport.GetAsync("/user/detail", TestContext.Current.CancellationToken);

        var requests = handler.Requests.ToArray();
        Assert.Contains("MUSIC_U=oldtoken", requests[0].CookieHeader);
        Assert.Contains("os=pc", requests[0].CookieHeader);
        Assert.Contains("MUSIC_U=secret_new_token", requests[1].CookieHeader);
        Assert.DoesNotContain("MUSIC_A_T=", requests[1].CookieHeader);
        Assert.DoesNotContain("Path=", requests[1].CookieHeader);
        Assert.DoesNotContain("secret_new_token", string.Join("\n", log.Messages));
        Assert.Contains("MUSIC_U=secret_new_token", creds.StoredCookie);
    }

    [Fact]
    public async Task WithoutCookiesDoesNotSendOrPersistCookies()
    {
        var settings = new InMemorySettingsStore();
        var log = new RecordingApiLog();
        var creds = new InMemoryCredentialStore { StoredCookie = "MUSIC_U=mysecret" };
        var jar = new SessionCookieJar(creds, log);
        jar.LoadFromStore();

        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(
            "{\"code\":801}",
            HttpStatusCode.OK,
            "MUSIC_U=hijack"));

        using var transport = new NeteaseHttpTransport(
            new ServerEndpoint(settings, log), jar, settings, log, handler);

        var response = await transport.GetWithoutCookieAsync("/login/qr/check?key=123", TestContext.Current.CancellationToken);

        Assert.Null(handler.Requests.Single().CookieHeader);
        Assert.Equal("{\"code\":801}", response.Body);
        Assert.Equal(ApiErrorKind.Business, response.Result.Error);
        Assert.Equal(801, response.Result.BusinessCode);
        Assert.Equal("MUSIC_U=mysecret", creds.StoredCookie);
    }

    [Fact]
    public async Task UpdateBaseUrlNormalizesPersistsAndLeavesInFlightRequestsOnOldUri()
    {
        var settings = new InMemorySettingsStore();
        var log = new RecordingApiLog();
        var endpoint = new ServerEndpoint(settings, log);
        var inFlightEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var call = 0;

        var handler = new FakeHttpMessageHandler(async (_, ct) =>
        {
            if (Interlocked.Increment(ref call) == 1)
            {
                inFlightEntered.TrySetResult();
                await releaseFirst.Task.WaitAsync(ct);
            }
            return FakeHttpMessageHandler.Json("{\"code\":200}");
        });

        using var transport = new NeteaseHttpTransport(
            endpoint, new SessionCookieJar(new InMemoryCredentialStore(), log), settings, log, handler);

        var first = transport.GetAsync("/first", TestContext.Current.CancellationToken);
        await inFlightEntered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Throws<ArgumentException>(() => endpoint.UpdateBaseUrl("   "));
        Assert.Throws<ArgumentException>(() => endpoint.UpdateBaseUrl("https://"));
        endpoint.UpdateBaseUrl("192.168.31.205:3000/");
        Assert.Equal("http://192.168.31.205:3000", endpoint.BaseUrl);
        Assert.Equal("http://192.168.31.205:3000", settings.GetString("ServerAddress"));

        releaseFirst.SetResult();
        await first;
        await transport.GetAsync("/second", TestContext.Current.CancellationToken);

        var requests = handler.Requests.ToArray();
        Assert.Equal("localhost", requests[0].RequestUri!.Host);
        Assert.Equal("192.168.31.205", requests[1].RequestUri!.Host);
        Assert.Equal(3000, requests[1].RequestUri!.Port);
    }

    [Fact]
    public async Task PreservesHttp400DetailNetworkErrorAndCancellation()
    {
        var settings = new InMemorySettingsStore();
        var log = new RecordingApiLog();
        var endpoint = new ServerEndpoint(settings, log);
        var jar = new SessionCookieJar(new InMemoryCredentialStore(), log);

        var http400Handler = new FakeHttpMessageHandler(_ =>
            FakeHttpMessageHandler.Json("{\"code\":400,\"message\":\"参数错误\"}", HttpStatusCode.BadRequest));
        using (var t1 = new NeteaseHttpTransport(endpoint, jar, settings, log, http400Handler))
        {
            var r = await t1.GetAsync("/bad", TestContext.Current.CancellationToken);
            Assert.False(r.Success);
            Assert.Equal(ApiErrorKind.Http, r.Result.Error);
            Assert.Contains("参数错误", r.Result.DisplayMessage);
        }

        var netErrHandler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("offline"));
        using (var t2 = new NeteaseHttpTransport(endpoint, jar, settings, log, netErrHandler))
        {
            var r = await t2.GetAsync("/offline", TestContext.Current.CancellationToken);
            Assert.False(r.Success);
            Assert.Equal(ApiErrorKind.Network, r.Result.Error);
            Assert.Equal(string.Empty, r.Body);
        }

        var slowHandler = new FakeHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return FakeHttpMessageHandler.Json("{\"code\":200}");
        });
        using (var t3 = new NeteaseHttpTransport(endpoint, jar, settings, log, slowHandler))
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => t3.GetAsync("/cancel", cts.Token));
        }
    }
}
