using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace Lunixi.Sdk.Tests;

internal sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    public static HttpResponseMessage Json(string body = "{\"data\":{}}", HttpStatusCode code = HttpStatusCode.OK)
        => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

internal static class TestOptions
{
    public static readonly LunixiKeyPair Keys = Ed25519Signer.GenerateKeyPair();
    public static LunixiOptions Create(TimeProvider? clock = null, int retries = 2, int maxResponseBytes = 1024 * 1024) => new()
    {
        BaseUri = new Uri("https://gateway.example"),
        KeyId = "mk_test_synthetic",
        PrivateKeyPem = Keys.PrivateKeyPem,
        TimeProvider = clock ?? TimeProvider.System,
        RetryDelay = TimeSpan.Zero,
        MaxRetries = retries,
        MaxResponseBytes = maxResponseBytes,
    };
}

internal sealed class MemoryReplayStore : IWebhookReplayStore
{
    private readonly ConcurrentDictionary<string, (string Hash, bool Completed, string Lease)> events = new();
    public bool Busy { get; set; }
    public ValueTask<WebhookReservation> AcquireAsync(string eventId, string bodySha256, DateTimeOffset now, DateTimeOffset leaseExpiresAt, CancellationToken cancellationToken)
    {
        if (Busy) return ValueTask.FromResult(new WebhookReservation(WebhookReservationStatus.Busy));
        var lease = Guid.NewGuid().ToString();
        var value = events.GetOrAdd(eventId, (bodySha256, false, lease));
        return ValueTask.FromResult(value.Hash != bodySha256 ? new WebhookReservation(WebhookReservationStatus.Conflict) :
            value.Completed ? new WebhookReservation(WebhookReservationStatus.Completed) :
            value.Lease == lease ? new WebhookReservation(WebhookReservationStatus.Acquired, lease) : new WebhookReservation(WebhookReservationStatus.Busy));
    }
    public ValueTask CompleteAsync(string eventId, string leaseToken, CancellationToken cancellationToken)
    {
        var value = events[eventId];
        if (value.Lease != leaseToken) throw new InvalidOperationException();
        events[eventId] = (value.Hash, true, leaseToken); return ValueTask.CompletedTask;
    }
    public ValueTask ReleaseAsync(string eventId, string leaseToken, CancellationToken cancellationToken)
    {
        if (events.TryGetValue(eventId, out var value) && value.Lease == leaseToken) events.TryRemove(eventId, out _);
        return ValueTask.CompletedTask;
    }
}

/// <summary>One request seen by <see cref="RecordingClient"/>, with every request and content header name.</summary>
internal sealed record RecordedRequest(string Method, Uri Uri, string? Body, byte[]? Bytes, IReadOnlyDictionary<string, string> Headers)
{
    public string PathAndQuery => Uri.PathAndQuery;
    public bool Signed => Headers.ContainsKey("X-Signature") && Headers.ContainsKey("X-Key-Id");
    public string? IdempotencyKey => Headers.TryGetValue("Idempotency-Key", out var key) ? key : null;
}

/// <summary>A client on a fake transport that answers the token call itself and records every other request.</summary>
internal static class RecordingClient
{
    public static (LunixiClient Client, List<RecordedRequest> Requests) Create(Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        var requests = new List<RecordedRequest>();
        var client = new LunixiClient(TestOptions.Create(retries: 0), new FakeHandler(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v1/auth/token") return FakeHandler.Json("{\"access_token\":\"token\"}");
            var headers = request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
                .ToDictionary(h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase);
            var bytes = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(ct);
            requests.Add(new(request.Method.Method, request.RequestUri, bytes is null ? null : Encoding.UTF8.GetString(bytes), bytes, headers));
            return respond?.Invoke(request) ?? FakeHandler.Json();
        }));
        return (client, requests);
    }
}
