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
