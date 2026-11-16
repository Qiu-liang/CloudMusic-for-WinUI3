using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using music.Core.Abstractions;

namespace music.Tests;

internal sealed class InMemorySettingsStore : ISettingsStore
{
    private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);

    public bool ContainsKey(string key) => _values.ContainsKey(key);

    public string? GetString(string key) =>
        _values.TryGetValue(key, out var val) ? val?.ToString() : null;

    public long GetInt64(string key, long defaultValue = 0) =>
        _values.TryGetValue(key, out var val) && val != null ? Convert.ToInt64(val) : defaultValue;

    public bool GetBoolean(string key, bool defaultValue = false) =>
        _values.TryGetValue(key, out var val) && val is bool b ? b : defaultValue;

    public void SetString(string key, string value) => _values[key] = value;
    public void SetInt64(string key, long value) => _values[key] = value;
    public void SetBoolean(string key, bool value) => _values[key] = value;
    public void Remove(string key) => _values.Remove(key);
    public IDictionary<string, object> AsDictionary() => _values;
}

internal sealed class InMemoryCredentialStore : ICredentialStore
{
    public string StoredCookie { get; set; } = string.Empty;
    public int SaveCount { get; private set; }
    public int ClearCount { get; private set; }

    public string Load() => StoredCookie;

    public void Save(string cookie)
    {
        StoredCookie = cookie;
        SaveCount++;
    }

    public void Clear()
    {
        StoredCookie = string.Empty;
        ClearCount++;
    }
}

internal sealed class RecordingApiLog : IApiLog
{
    public ConcurrentQueue<string> Messages { get; } = new();

    public void Write(string message) => Messages.Enqueue(message);
}

internal sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public bool TryEnqueue(Action action)
    {
        action();
        return true;
    }
}

internal sealed record CapturedRequest(HttpMethod Method, Uri? RequestUri, string? CookieHeader, string? Body);

internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;
    private int _inFlight;
    private int _maxConcurrency;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : this((req, _) => Task.FromResult(responder(req)))
    {
    }

    public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        _responder = responder;
    }

    public ConcurrentQueue<CapturedRequest> Requests { get; } = new();
    public int MaxObservedConcurrency => Volatile.Read(ref _maxConcurrency);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var current = Interlocked.Increment(ref _inFlight);
        UpdateMaxConcurrency(current);
        try
        {
            string? cookieHeader = request.Headers.TryGetValues("Cookie", out var values)
                ? string.Join("; ", values)
                : null;
            string? body = request.Content != null
                ? await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)
                : null;

            Requests.Enqueue(new CapturedRequest(request.Method, request.RequestUri, cookieHeader, body));
            return await _responder(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    private void UpdateMaxConcurrency(int current)
    {
        while (true)
        {
            var observed = Volatile.Read(ref _maxConcurrency);
            if (current <= observed) return;
            if (Interlocked.CompareExchange(ref _maxConcurrency, current, observed) == observed) return;
        }
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK, params string[] setCookies)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        foreach (var cookie in setCookies)
            response.Headers.TryAddWithoutValidation("Set-Cookie", cookie);
        return response;
    }
}
