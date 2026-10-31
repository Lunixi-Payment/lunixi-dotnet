using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lunixi;

/// <summary>An authenticated webhook envelope. A captured event still requires merchant order and amount reconciliation.</summary>
public sealed class LunixiWebhook
{
    /// <summary>The signed delivery event identifier. Use this as the durable inbox unique key.</summary>
    public string Id { get; }
    /// <summary>The authenticated event type.</summary>
    public string Type { get; }
    /// <summary>The authenticated parsed envelope.</summary>
    public JsonElement Envelope { get; }
    /// <summary>The data member, or an empty object when absent.</summary>
    public JsonElement Data => Envelope.TryGetProperty("data", out var value) ? value : Empty;
    /// <summary>SHA-256 of the exact body, for detecting conflicting deliveries of one event identifier.</summary>
    public string BodySha256 { get; }
    private static readonly JsonElement Empty = JsonSerializer.SerializeToElement(new { });
    internal LunixiWebhook(string id, string type, JsonElement envelope, ReadOnlySpan<byte> raw)
    { Id = id; Type = type; Envelope = envelope.Clone(); BodySha256 = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant(); }
    /// <summary>Returns only the event's type; envelope contents are never logged implicitly.</summary>
    public override string ToString() => $"LunixiWebhook {{ Type = {Type}, Data = [REDACTED] }}";
}

/// <summary>Verifies v2 HMAC signatures over exact raw bytes, signed timestamp and event identifier.</summary>
public sealed class WebhookVerifier
{
    private readonly byte[] secret;
    private readonly TimeProvider clock;
    private readonly TimeSpan tolerance;
    /// <summary>Creates a verifier. The default permitted clock skew is five minutes.</summary>
    public WebhookVerifier(string secret, TimeProvider? timeProvider = null, TimeSpan? tolerance = null)
    {
        if (string.IsNullOrEmpty(secret) || secret.Length > 4096)
            throw new LunixiConfigurationException("A non-empty bounded webhook signing secret is required.");
        this.secret = Encoding.UTF8.GetBytes(secret); clock = timeProvider ?? TimeProvider.System;
        this.tolerance = tolerance ?? TimeSpan.FromMinutes(5);
        if (this.tolerance <= TimeSpan.Zero || this.tolerance > TimeSpan.FromMinutes(15))
            throw new LunixiConfigurationException("Webhook timestamp tolerance must be positive and at most fifteen minutes.");
    }
    /// <summary>Verifies raw bytes before parsing JSON. Header names are case insensitive; critical duplicate headers are rejected.</summary>
    public LunixiWebhook Verify(ReadOnlySpan<byte> rawBody, IEnumerable<KeyValuePair<string, string>> headers)
    {
        if (rawBody.Length is < 1 or > 1024 * 1024) throw new LunixiWebhookException("Webhook body is empty or exceeds one MiB.");
        ArgumentNullException.ThrowIfNull(headers);
        var selected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string[] names = ["x-lunixi-signature", "x-lunixi-signature-timestamp", "x-lunixi-event-id", "x-lunixi-event-type"];
        foreach (var header in headers)
        {
            if (!names.Contains(header.Key, StringComparer.OrdinalIgnoreCase)) continue;
            if (header.Value is null || header.Value.Length > 4096 || !selected.TryAdd(header.Key, header.Value))
                throw new LunixiWebhookException("Webhook signature headers are invalid or repeated.");
        }
        if (!selected.TryGetValue(names[0], out var signature) || !selected.TryGetValue(names[1], out var timestamp) ||
            !selected.TryGetValue(names[2], out var id) || Validation.SafeIdentifier(id) is null)
            throw new LunixiWebhookException("Required webhook signature headers are missing or invalid.");
        // Require ISO 8601 with an explicit timezone; never interpret a local timestamp as UTC.
        string[] timestampFormats = ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"];
        if (!DateTimeOffset.TryParseExact(timestamp, timestampFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var sentAt) ||
            (clock.GetUtcNow() - sentAt).Duration() > tolerance)
            throw new LunixiWebhookException("Webhook timestamp is invalid or outside the permitted window.");
        var prefix = Encoding.UTF8.GetBytes(id + "." + timestamp + ".");
        var message = new byte[prefix.Length + rawBody.Length]; prefix.CopyTo(message, 0); rawBody.CopyTo(message.AsSpan(prefix.Length));
        var expected = HMACSHA256.HashData(secret, message);
        var matched = false; var count = 0;
        foreach (var part in signature.Split(','))
        {
            var item = part.Trim();
            if (!item.StartsWith("v2=", StringComparison.Ordinal)) continue;
            if (++count > 8) throw new LunixiWebhookException("Webhook includes too many v2 signatures.");
            var candidate = item[3..];
            if (candidate.Length != 64 || !candidate.All(char.IsAsciiHexDigit)) continue;
            matched |= CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(candidate));
        }
        if (!matched) throw new LunixiWebhookException("Webhook signature is invalid.");
        try
        {
            using var document = JsonDocument.Parse(rawBody.ToArray(), new JsonDocumentOptions { MaxDepth = 64 });
            var body = document.RootElement;
            if (body.ValueKind != JsonValueKind.Object || body.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != body.EnumerateObject().Count())
                throw new LunixiWebhookException("Webhook must be an unambiguous JSON object.");
            if (body.TryGetProperty("id", out _) && LunixiClient.Property(body, "id") != id)
                throw new LunixiWebhookException("Webhook event identifier does not match its signed header.");
            var type = LunixiClient.Property(body, "type");
            if (type is null || Validation.SafeIdentifier(type) is null || type.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')))
                throw new LunixiWebhookException("Webhook event type is missing or invalid.");
            if (selected.TryGetValue(names[3], out var headerType) && headerType != type)
                throw new LunixiWebhookException("Webhook event type does not match its header.");
            return new LunixiWebhook(id, type, body, rawBody);
        }
        catch (JsonException) { throw new LunixiWebhookException("Authenticated webhook body is malformed JSON."); }
    }
    /// <summary>Returns no webhook signing secret.</summary>
    public override string ToString() => "WebhookVerifier { Secret = [REDACTED] }";
}

/// <summary>The result of a durable, atomic webhook-inbox reservation.</summary>
public enum WebhookReservationStatus
{
    /// <summary>This processor owns the lease.</summary>
    Acquired,
    /// <summary>The identical event was already committed.</summary>
    Completed,
    /// <summary>Another processor has an unexpired lease; respond with a retriable failure.</summary>
    Busy,
    /// <summary>The event identifier was reused with different body bytes; reject and investigate.</summary>
    Conflict,
}

/// <summary>A storage reservation, including a fencing token for the winning processor.</summary>
public sealed record WebhookReservation(WebhookReservationStatus Status, string? LeaseToken = null);

/// <summary>Durable replay storage implemented by the merchant, normally in its order database.</summary>
/// <remarks>Acquire must atomically compare the body hash, completed state and lease expiry. Completion/release must compare the lease token. Retain completed identifiers at least seven days. An in-memory dictionary is insufficient for production.</remarks>
public interface IWebhookReplayStore
{
    /// <summary>Atomically acquires an inbox lease or reports a duplicate, conflict or busy processor.</summary>
    ValueTask<WebhookReservation> AcquireAsync(string eventId, string bodySha256, DateTimeOffset now, DateTimeOffset leaseExpiresAt, CancellationToken cancellationToken);
    /// <summary>Marks an event complete only if this fencing token still owns it. Throw if ownership was lost.</summary>
    ValueTask CompleteAsync(string eventId, string leaseToken, CancellationToken cancellationToken);
    /// <summary>Releases an uncommitted event only if this fencing token still owns it.</summary>
    ValueTask ReleaseAsync(string eventId, string leaseToken, CancellationToken cancellationToken);
}

/// <summary>Coordinates a verifier and durable replay store. Handlers must also make their own domain writes idempotent.</summary>
/// <remarks>Use the same database transaction for inbox completion and order updates wherever possible, as demonstrated in the ASP.NET Core sample. This coordinator cannot make two external systems atomic.</remarks>
public sealed class WebhookProcessor(WebhookVerifier verifier, IWebhookReplayStore store, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    /// <summary>Verifies and processes an event; returns false for an already committed duplicate.</summary>
    public async Task<bool> ProcessAsync(ReadOnlyMemory<byte> rawBody, IEnumerable<KeyValuePair<string, string>> headers,
        Func<LunixiWebhook, CancellationToken, Task> handler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var webhook = verifier.Verify(rawBody.Span, headers); var now = clock.GetUtcNow();
        var reservation = await store.AcquireAsync(webhook.Id, webhook.BodySha256, now, now.AddMinutes(5), cancellationToken).ConfigureAwait(false);
        if (reservation.Status == WebhookReservationStatus.Completed) return false;
        if (reservation.Status == WebhookReservationStatus.Conflict) throw new LunixiWebhookException("Webhook identifier conflicts with a previous delivery.");
        if (reservation.Status == WebhookReservationStatus.Busy) throw new LunixiWebhookBusyException();
        if (reservation.Status != WebhookReservationStatus.Acquired || string.IsNullOrEmpty(reservation.LeaseToken))
            throw new LunixiWebhookException("Webhook replay store did not provide a valid lease.");
        try
        {
            await handler(webhook, cancellationToken).ConfigureAwait(false);
            await store.CompleteAsync(webhook.Id, reservation.LeaseToken, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            // A canceled HTTP request must not leave a lease needlessly locked until expiry.
            using var releaseTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await store.ReleaseAsync(webhook.Id, reservation.LeaseToken, releaseTimeout.Token).ConfigureAwait(false); }
            catch { /* Preserve the original failure. An unavailable store is recovered through lease expiry. */ }
            throw;
        }
    }
}
