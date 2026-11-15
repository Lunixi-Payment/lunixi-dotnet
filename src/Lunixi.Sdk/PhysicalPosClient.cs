using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lunixi;

/// <summary>
/// Physical POS: payments on your card terminals, transactions, reports and end-of-day runs and results
/// (<c>/api/v1/physical-pos</c>). Every call is signed; the API key decides the environment, and a TEST key can read but
/// every payment write is 403 <c>physical_pos.environment.test_key_read_only</c>. Payment writes need the
/// <c>physical_pos:payments</c> scope, reads <c>physical_pos:view</c> or <c>physical_pos:payments</c>.
/// <para>
/// Card data is never sent: the card is read on the device. A field name that looks like card data or a card number in
/// <c>merchantReference</c>, an item name or tax group, or a free-text filter throws
/// <see cref="PhysicalPosCardDataRejectedException"/> before anything is sent; any other unknown field throws
/// <see cref="LunixiConfigurationException"/>.
/// </para>
/// <para>
/// A payment write is asynchronous: the answer means accepted, not paid. Follow the outcome with the
/// <c>physical_pos.payment.*</c> webhooks or <see cref="GetPaymentAsync"/>. Store the <c>Idempotency-Key</c> with your order
/// before you call; when <see cref="PhysicalPosOutcomeUnknownException"/> is thrown call again later with the SAME key. A new
/// key for the same payment can charge the card twice. For a sale also send <c>merchantReference</c> with
/// <c>allowDuplicateReference: false</c>.
/// </para>
/// <para>
/// Request bodies are anonymous objects, dictionaries, <see cref="JsonObject"/> or <see cref="JsonElement"/> values; send a JSON
/// null (to clear a nullable field) with a <see cref="JsonObject"/> or a dictionary. Money fields are canonical minor-unit
/// strings (<c>"4500"</c>); counts are numbers. Filters are a dictionary of the route's filter names.
/// </para>
/// </summary>
public sealed class PhysicalPosClient
{
    private readonly LunixiClient client;
    internal PhysicalPosClient(LunixiClient client) => this.client = client;

    /// <summary>A new <c>Idempotency-Key</c> (a random UUID). Store it with your order before you call.</summary>
    public static string NewIdempotencyKey() => PhysicalPosRequest.NewIdempotencyKey();

    /// <summary>
    /// Starts a payment on a device (<c>POST /payments</c>): <c>SALE</c>, <c>PREAUTH_OPEN</c> or <c>REFUND_UNMATCHED</c>.
    /// <c>target</c> is exactly one of <c>{ deviceId }</c>, <c>{ vendorCode, serialNumber }</c> or <c>{ externalRef }</c>;
    /// sale <c>items</c> carry <c>taxGroupCode</c>. For a provider-collected card (<c>instrument</c> other than <c>CARD</c>) send
    /// <c>interaction</c> (<c>INSTRUMENT_READ</c> or <c>CODE_DISPLAY</c>) and, when the terminal has several provider rows,
    /// <c>terminalProviderId</c>. <paramref name="idempotencyKey"/> is required and sent exactly as given.
    /// Returns 202 for a new payment (<c>paymentStatus: NOT_STARTED</c>) or 200 with <c>replayed</c> for the same key and body;
    /// throws <see cref="PhysicalPosOutcomeUnknownException"/> when the outcome is unknown.
    /// </summary>
    public async Task<PhysicalPosWriteResult> CreatePaymentAsync(object request, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var key = PhysicalPosRequest.RequiredIdempotencyKey(idempotencyKey, "a physical POS payment");
        var body = PhysicalPosRequest.Body(request, PhysicalPosShapes.CreatePayment, "createPayment request");
        return await PhysicalPosTransport.PaymentWriteAsync(client, "/payments", body, key, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reverses an approved payment (<c>POST /payments/{paymentId}/reversals</c>): <c>VOID</c>, <c>REFUND</c> (by
    /// <c>amountMinor</c> or by <c>items: [{ lineNo, quantityMilli }]</c>), <c>PREAUTH_CLOSE</c> or <c>PREAUTH_CANCEL</c>. The
    /// reversal is a new child payment followed like any payment. Same key rule, results and errors as
    /// <see cref="CreatePaymentAsync"/>; a void after end-of-day throws <see cref="PhysicalPosVoidAfterSettlementException"/>.
    /// </summary>
    public async Task<PhysicalPosWriteResult> CreateReversalAsync(string paymentId, object request, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var id = PhysicalPosRequest.PathId(paymentId, "paymentId");
        var key = PhysicalPosRequest.RequiredIdempotencyKey(idempotencyKey, "a physical POS reversal");
        var body = PhysicalPosRequest.Body(request, PhysicalPosShapes.CreateReversal, "createReversal request");
        return await PhysicalPosTransport.PaymentWriteAsync(client, "/payments/" + id + "/reversals", body, key, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Cancels a payment before its start command reaches the device (<c>POST /payments/{paymentId}/cancel</c>, 200). Only
    /// <c>EXPIRED</c> with <c>statusReason: MERCHANT_CANCELLED_BEFORE_DISPATCH</c> means it was cancelled; a payment already in a
    /// final state comes back unchanged with <c>replayed</c>. Cancelling is idempotent, so the key is optional (a new UUID
    /// when null).
    /// </summary>
    public async Task<PhysicalPosWriteResult> CancelPaymentAsync(string paymentId, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        var id = PhysicalPosRequest.PathId(paymentId, "paymentId");
        var key = PhysicalPosRequest.IdempotencyKeyOrNew(idempotencyKey);
        return await PhysicalPosTransport.PaymentWriteAsync(client, "/payments/" + id + "/cancel", new JsonObject(), key, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>One payment with its items and refund items (<c>GET /payments/{paymentId}</c>).</summary>
    public async Task<JsonElement> GetPaymentAsync(string paymentId, CancellationToken cancellationToken = default)
        => PhysicalPosTransport.Record(await Read("/payments/" + PhysicalPosRequest.PathId(paymentId, "paymentId"), "", cancellationToken).ConfigureAwait(false), "payment");

    /// <summary>
    /// Payments, newest first (<c>GET /payments</c>); roots and reversals in one list, without items. Filters: <c>limit</c>,
    /// <c>cursor</c>, <c>deviceId</c>, <c>paymentStatus</c>, <c>createdFrom</c>, <c>createdTo</c>, <c>merchantReference</c>
    /// (finds a payment after an unknown outcome).
    /// </summary>
    public Task<PhysicalPosPage> ListPaymentsAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => Page("/payments", filters, "listPayments", cancellationToken);

    /// <summary>Every payment of <see cref="ListPaymentsAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IteratePaymentsAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => PhysicalPosTransport.IterateAsync(ListPaymentsAsync, filters, cancellationToken);

    /// <summary>Transaction trees (root payment and its reversals), newest first (<c>GET /transactions</c>).</summary>
    public Task<PhysicalPosPage> ListTransactionsAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => Page("/transactions", filters, "listTransactions", cancellationToken);

    /// <summary>Every transaction tree of <see cref="ListTransactionsAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateTransactionsAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => PhysicalPosTransport.IterateAsync(ListTransactionsAsync, filters, cancellationToken);

    /// <summary>The tree of a root or child payment with its timeline (<c>GET /transactions/{paymentId}</c>).</summary>
    public async Task<JsonElement> GetTransactionAsync(string paymentId, CancellationToken cancellationToken = default)
        => PhysicalPosTransport.Record(await Read("/transactions/" + PhysicalPosRequest.PathId(paymentId, "paymentId"), "", cancellationToken).ConfigureAwait(false), "transaction");

    /// <summary>Payments with an unknown outcome or under manual review (<c>GET /transactions/ambiguous</c>).</summary>
    public Task<PhysicalPosPage> ListAmbiguousPaymentsAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => Page("/transactions/ambiguous", filters, "listAmbiguousPayments", cancellationToken);

    /// <summary>Every payment of <see cref="ListAmbiguousPaymentsAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateAmbiguousPaymentsAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => PhysicalPosTransport.IterateAsync(ListAmbiguousPaymentsAsync, filters, cancellationToken);

    /// <summary>
    /// Sale, refund and void counts and amounts with the net per currency (<c>GET /reports/sales-summary</c>). <c>dateFrom</c> /
    /// <c>dateTo</c> are business days (<c>YYYY-MM-DD</c>, Europe/Istanbul; a <see cref="DateOnly"/> or a date-time is sent as its own
    /// calendar day); <c>groupBy</c> is <c>DAY</c>, <c>STORE</c>, <c>DEVICE</c> or <c>INSTRUMENT</c>.
    /// </summary>
    public async Task<JsonElement> GetSalesSummaryAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => (await Read("/reports/sales-summary", PhysicalPosRequest.Query(filters, "getSalesSummary"), cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>Tax breakdown with refunds and voids deducted (<c>GET /reports/tax-summary</c>).</summary>
    public async Task<JsonElement> GetTaxSummaryAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => (await Read("/reports/tax-summary", PhysicalPosRequest.Query(filters, "getTaxSummary"), cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>End-of-day results per bank line, with the reported / system / difference totals (<c>GET /settlements</c>).</summary>
    public Task<PhysicalPosPage> ListSettlementsAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => Page("/settlements", filters, "listSettlements", cancellationToken);

    /// <summary>Every end-of-day result line of <see cref="ListSettlementsAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateSettlementsAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => PhysicalPosTransport.IterateAsync(ListSettlementsAsync, filters, cancellationToken);

    /// <summary>
    /// Starts end-of-day (batch close) on a device (<c>POST /devices/{deviceId}/settlement</c>, <c>physical_pos:payments</c>).
    /// End-of-day cannot be undone. The answer means accepted; the device reports the result later (follow it with
    /// <see cref="GetSettlementRunAsync"/> or <see cref="ListSettlementsAsync"/>). <see cref="PhysicalPosWriteResult.Entity"/> is
    /// the <c>run</c>; 202 new, 200 replay. Pass the key you stored, or null for a new UUID (returned as
    /// <see cref="PhysicalPosWriteResult.IdempotencyKey"/>; after an unknown outcome call again with it).
    /// </summary>
    public async Task<PhysicalPosWriteResult> StartSettlementAsync(string deviceId, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        var id = PhysicalPosRequest.PathId(deviceId, "deviceId");
        var key = PhysicalPosRequest.IdempotencyKeyOrNew(idempotencyKey);
        var response = await PhysicalPosTransport.SendAsync(client, HttpMethod.Post, "/devices/" + id + "/settlement", new JsonObject(), key, cancellationToken).ConfigureAwait(false);
        return new PhysicalPosWriteResult(response, "run", key);
    }

    /// <summary>
    /// End-of-day runs started by Lunixi (manual and scheduled), newest first (<c>GET /settlement-runs</c>). Filters:
    /// <c>deviceId</c>, <c>source</c> (<c>MANUAL_API</c>, <c>MANUAL_PANEL</c>, <c>SCHEDULED</c>), <c>runStatus</c> (<c>PENDING</c>,
    /// <c>STARTED</c>, <c>SKIPPED</c>, <c>FAILED</c>), <c>createdFrom</c> / <c>createdTo</c>, <c>includeTotal</c>, <c>limit</c>, <c>cursor</c>.
    /// </summary>
    public Task<PhysicalPosPage> ListSettlementRunsAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => Page("/settlement-runs", filters, "listSettlementRuns", cancellationToken);

    /// <summary>Every run of <see cref="ListSettlementRunsAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateSettlementRunsAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => PhysicalPosTransport.IterateAsync(ListSettlementRunsAsync, filters, cancellationToken);

    /// <summary>One end-of-day run with its command status and result state (<c>GET /settlement-runs/{runId}</c>).</summary>
    public async Task<JsonElement> GetSettlementRunAsync(string runId, CancellationToken cancellationToken = default)
        => PhysicalPosTransport.Record(await Read("/settlement-runs/" + PhysicalPosRequest.PathId(runId, "runId"), "", cancellationToken).ConfigureAwait(false), "run");

    /// <summary>
    /// Verifies (signature, event id, timestamp) and reads a <c>physical_pos.*</c> webhook from the exact raw body, the request
    /// headers and your webhook secret. Nothing is read before the signature verifies.
    /// </summary>
    public static PhysicalPosWebhookEvent ParseWebhookEvent(ReadOnlySpan<byte> rawBody, IEnumerable<KeyValuePair<string, string>> headers,
        string secret, TimeProvider? timeProvider = null, TimeSpan? tolerance = null)
        => ParseWebhookEvent(new WebhookVerifier(secret, timeProvider, tolerance).Verify(rawBody, headers));

    /// <summary>Verifies with a configured <see cref="WebhookVerifier"/> and reads a <c>physical_pos.*</c> webhook.</summary>
    public static PhysicalPosWebhookEvent ParseWebhookEvent(ReadOnlySpan<byte> rawBody, IEnumerable<KeyValuePair<string, string>> headers, WebhookVerifier verifier)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        return ParseWebhookEvent(verifier.Verify(rawBody, headers));
    }

    /// <summary>
    /// Reads a webhook already verified by <see cref="WebhookVerifier"/> (a <see cref="LunixiWebhook"/> exists only after
    /// verification). The type comes from the signed body; the payload is <c>data</c>, or <c>payloadJson</c> (an object or JSON
    /// text). Declared money fields must be canonical minor-unit strings. Throws <see cref="LunixiWebhookException"/> for an
    /// event that is not <c>physical_pos.*</c>, carries no payload or has invalid money.
    /// </summary>
    public static PhysicalPosWebhookEvent ParseWebhookEvent(LunixiWebhook verified)
    {
        ArgumentNullException.ThrowIfNull(verified);
        if (!verified.Type.StartsWith("physical_pos.", StringComparison.Ordinal))
            throw new LunixiWebhookException("Not a physical POS webhook event.");
        var envelope = verified.Envelope;
        var data = Payload(envelope);
        if (!PhysicalPosMoney.IsValid(data)) throw new LunixiWebhookException("The physical POS webhook carries invalid canonical money.");
        var id = PhysicalPosWebhookEvent.Text(envelope, "id") ?? PhysicalPosWebhookEvent.Text(envelope, "eventId") ?? verified.Id;
        return new PhysicalPosWebhookEvent(id, verified.Type, data, envelope);
    }

    private static JsonElement Payload(JsonElement envelope)
    {
        if (envelope.TryGetProperty("data", out var data) && IsNonEmptyObject(data)) return data;
        if (envelope.TryGetProperty("payloadJson", out var payload))
        {
            if (IsNonEmptyObject(payload)) return payload;
            if (payload.ValueKind == JsonValueKind.String)
            {
                JsonElement parsed;
                try
                {
                    using var document = JsonDocument.Parse(payload.GetString()!, new JsonDocumentOptions { MaxDepth = 64 });
                    parsed = document.RootElement.Clone();
                }
                catch (JsonException) { throw new LunixiWebhookException("The physical POS webhook payloadJson is not valid JSON."); }
                if (IsNonEmptyObject(parsed)) return parsed;
            }
        }
        throw new LunixiWebhookException("The physical POS webhook carries no event data (data / payloadJson).");
    }

    private static bool IsNonEmptyObject(JsonElement value) => value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Any();

    private Task<LunixiResponse> Read(string path, string query, CancellationToken cancellationToken)
        => PhysicalPosTransport.SendAsync(client, HttpMethod.Get, path + query, null, null, cancellationToken);

    private async Task<PhysicalPosPage> Page(string path, IReadOnlyDictionary<string, object?>? filters, string route, CancellationToken cancellationToken)
        => new(await Read(path, PhysicalPosRequest.Query(filters, route), cancellationToken).ConfigureAwait(false));
}
