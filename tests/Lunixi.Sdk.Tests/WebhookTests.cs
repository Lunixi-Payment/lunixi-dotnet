using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lunixi.Sdk.Tests;

public class WebhookTests
{
    private const string Secret = "whsec_testsecret_0123456789abcdef";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-31T08:00:00Z");
    private static string Raw(string id = "event-1", string type = "payment.captured") => "{\"id\":\"" + id + "\",\"type\":\"" + type + "\",\"data\":{\"capturedAmountMinor\":\"10050\",\"currency\":\"TRY\"}}";
    private static Dictionary<string, string> Headers(string raw, string id = "event-1", DateTimeOffset? timestamp = null)
    {
        var time = (timestamp ?? Now).ToString("O");
        var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(id + "." + time + "." + raw))).ToLowerInvariant();
        return new() { ["X-Lunixi-Signature"] = "v2=" + signature, ["X-Lunixi-Signature-Timestamp"] = time, ["X-Lunixi-Event-Id"] = id, ["X-Lunixi-Event-Type"] = "payment.captured" };
    }

    [Fact]
    public void ExistingNodeAndPhpWebhookFixturesVerify()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/webhook-v2-vectors.json")));
        foreach (var fixture in document.RootElement.EnumerateArray())
        {
            var time = fixture.GetProperty("timestamp").GetString()!;
            var verifier = new WebhookVerifier(fixture.GetProperty("secret").GetString()!, new TestClock(DateTimeOffset.Parse(time)));
            var result = verifier.Verify(Encoding.UTF8.GetBytes(fixture.GetProperty("rawBody").GetString()!), new Dictionary<string, string>
            {
                ["x-lunixi-signature"] = "v1=ignored,v2=" + new string('0', 64) + ",v2=" + fixture.GetProperty("expectedV2").GetString(),
                ["x-lunixi-signature-timestamp"] = time,
                ["x-lunixi-event-id"] = fixture.GetProperty("eventId").GetString()!,
            });
            Assert.Equal(fixture.GetProperty("eventId").GetString(), result.Id);
            Assert.DoesNotContain("merchantName", result.ToString());
        }
    }

    [Fact]
    public void TamperingWithBodyEventIdTimestampOrTypeIsRejected()
    {
        var raw = Raw(); var headers = Headers(raw); var verifier = new WebhookVerifier(Secret, new TestClock(Now));
        Assert.Throws<LunixiWebhookException>(() => verifier.Verify(Encoding.UTF8.GetBytes(raw + " "), headers));
        var renamed = Headers(raw, "event-2");
        Assert.Throws<LunixiWebhookException>(() => verifier.Verify(Encoding.UTF8.GetBytes(raw), renamed));
        var type = Headers(raw); type["X-Lunixi-Event-Type"] = "payment.refunded";
        Assert.Throws<LunixiWebhookException>(() => verifier.Verify(Encoding.UTF8.GetBytes(raw), type));
        var timestamp = Headers(raw); timestamp["X-Lunixi-Signature-Timestamp"] = Now.AddSeconds(1).ToString("O");
        Assert.Throws<LunixiWebhookException>(() => verifier.Verify(Encoding.UTF8.GetBytes(raw), timestamp));
    }

    [Theory]
    [InlineData(-301)]
    [InlineData(301)]
    public void OldAndFutureSignedDeliveriesAreRejected(int offset)
    {
        var raw = Raw(); var verifier = new WebhookVerifier(Secret, new TestClock(Now));
        Assert.Throws<LunixiWebhookException>(() => verifier.Verify(Encoding.UTF8.GetBytes(raw), Headers(raw, timestamp: Now.AddSeconds(offset))));
    }

    [Fact]
    public void MissingDuplicateAndLegacyOnlySignatureHeadersAreRejected()
    {
        var raw = Raw(); var verifier = new WebhookVerifier(Secret, new TestClock(Now));
        Assert.Throws<LunixiWebhookException>(() => verifier.Verify(Encoding.UTF8.GetBytes(raw), []));
        var duplicated = Headers(raw).Append(new("x-lunixi-event-id", "event-1"));
        Assert.Throws<LunixiWebhookException>(() => verifier.Verify(Encoding.UTF8.GetBytes(raw), duplicated));
        var legacy = Headers(raw); legacy["X-Lunixi-Signature"] = "v1=" + new string('f', 64);
        Assert.Throws<LunixiWebhookException>(() => verifier.Verify(Encoding.UTF8.GetBytes(raw), legacy));
    }

    [Fact]
    public async Task DurableReplayCoordinatorDedupesAndRejectsConflicts()
    {
        var store = new MemoryReplayStore(); var processor = new WebhookProcessor(new(Secret, new TestClock(Now)), store, new TestClock(Now));
        var raw = Raw(); var bytes = Encoding.UTF8.GetBytes(raw); var count = 0;
        Task Handle(LunixiWebhook _, CancellationToken ct) { count++; return Task.CompletedTask; }
        Assert.True(await processor.ProcessAsync(bytes, Headers(raw), Handle));
        Assert.False(await processor.ProcessAsync(bytes, Headers(raw), Handle)); Assert.Equal(1, count);
        var changed = raw + " ";
        await Assert.ThrowsAsync<LunixiWebhookException>(() => processor.ProcessAsync(Encoding.UTF8.GetBytes(changed), Headers(changed), Handle));
    }

    [Fact]
    public async Task HandlerFailureIsRetryableAndBusyLeaseIsNeverAcknowledged()
    {
        var store = new MemoryReplayStore(); var processor = new WebhookProcessor(new(Secret, new TestClock(Now)), store, new TestClock(Now));
        var raw = Raw(); var bytes = Encoding.UTF8.GetBytes(raw);
        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessAsync(bytes, Headers(raw), (_, _) => throw new InvalidOperationException("domain failure")));
        Assert.True(await processor.ProcessAsync(bytes, Headers(raw), (_, _) => Task.CompletedTask));
        store.Busy = true;
        await Assert.ThrowsAsync<LunixiWebhookBusyException>(() => processor.ProcessAsync(bytes, Headers(raw), (_, _) => Task.CompletedTask));
    }
}
