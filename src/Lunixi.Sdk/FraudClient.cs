using System.Text.Json;

namespace Lunixi;

/// <summary>A validated fraud-evaluation request. The authenticated key determines the tenant.</summary>
public sealed class FraudEvaluateRequest
{
    private static readonly string[] Fields = ["integrationMode", "productContext", "transactionRef", "channel", "surface", "stage", "operationKind", "authModeRequested", "authModeApplied", "subMerchantRef", "subjectType", "subjectId", "paymentRail", "transferDirection", "merchantGroupIds", "paymentId", "payment", "bankTransfer", "cryptoTransfer", "walletOperation", "billPayment", "loanApplication", "payout", "customer", "device", "session", "merchant", "mobileApp", "telemetry", "metadata", "identitySnapshot", "identityAssertion"];
    /// <summary>The immutable validated request body.</summary>
    public JsonElement Payload { get; }
    /// <summary>Validates public evaluation fields; tenant identifiers and unknown fields are rejected.</summary>
    public FraudEvaluateRequest(object payload)
    {
        Payload = CanonicalJson.Element(payload); RequestValidation.Fields(Payload, Fields);
        foreach (var name in new[] { "transactionRef", "subjectId" })
            if (Payload.TryGetProperty(name, out var property) && property.ValueKind != JsonValueKind.Null)
                RequestValidation.RequiredText(Payload, name, 180);
    }
    /// <summary>Returns no customer, device or transaction data.</summary>
    public override string ToString() => "FraudEvaluateRequest { RequestData = [REDACTED] }";
}

/// <summary>Fraud evaluation, event ingest and persisted decision logs.</summary>
public sealed class FraudClient
{
    /// <summary>Most events one <see cref="RecordEventBatchAsync"/> call accepts; the gateway refuses the whole batch above it.</summary>
    public const int EventBatchLimit = 500;
    private static readonly string[] DecisionFilters = ["paymentId", "decision", "integrationMode", "productContext", "limit", "cursor"];
    private readonly LunixiClient client;
    internal FraudClient(LunixiClient client) => this.client = client;
    /// <summary>Evaluates a public fraud request. A decision is not a payment authorization or capture.</summary>
    public Task<LunixiResponse> EvaluateAsync(FraudEvaluateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return client.SendAsync(HttpMethod.Post, "/api/v1/fraud/decisions/evaluate", request.Payload, signed: false, cancellationToken: cancellationToken);
    }
    /// <summary>Records a signed transaction event, with the tenant derived from the authenticated key.</summary>
    public Task<LunixiResponse> RecordEventAsync(object transactionEvent, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        var body = CanonicalJson.Element(transactionEvent);
        if (body.ValueKind != JsonValueKind.Object) throw new LunixiConfigurationException("A transaction event must be an object.");
        return client.SendAsync(HttpMethod.Post, "/api/v1/fraud/events/transactions", body, idempotencyKey, cancellationToken: cancellationToken);
    }
    /// <summary>
    /// Records up to <see cref="EventBatchLimit"/> transaction events in one signed call
    /// (<c>POST /api/v1/fraud/events/transactions/batch</c>, sent as a JSON array). The batch is processed in order and can
    /// succeed partially: read the per-event results in the response.
    /// </summary>
    public Task<LunixiResponse> RecordEventBatchAsync(IReadOnlyList<object> transactionEvents, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transactionEvents);
        if (transactionEvents.Count == 0) throw new LunixiConfigurationException("A fraud event batch must not be empty.");
        if (transactionEvents.Count > EventBatchLimit)
            throw new LunixiConfigurationException($"A fraud event batch holds at most {EventBatchLimit} events; split the feed into chunks.");
        var body = CanonicalJson.Element(transactionEvents);
        if (body.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.Object))
            throw new LunixiConfigurationException("Each transaction event must be an object.");
        return client.SendAsync(HttpMethod.Post, "/api/v1/fraud/events/transactions/batch", body, idempotencyKey, cancellationToken: cancellationToken);
    }
    /// <summary>
    /// Reads the most recent decision log of a payment (<see cref="ListDecisionsAsync"/> with <c>paymentId</c> and
    /// <c>limit=1</c>), or null when the payment has none.
    /// </summary>
    public async Task<JsonElement?> LatestDecisionForPaymentAsync(string paymentId, CancellationToken cancellationToken = default)
    {
        RequestValidation.Text(paymentId, "paymentId", 256);
        var response = await ListDecisionsAsync(new Dictionary<string, string> { ["paymentId"] = paymentId, ["limit"] = "1" }, cancellationToken).ConfigureAwait(false);
        return response.Data is { ValueKind: JsonValueKind.Array } items && items.GetArrayLength() > 0 ? items[0].Clone() : null;
    }
    /// <summary>Reads one decision log by trace identifier.</summary>
    public Task<LunixiResponse> GetDecisionAsync(string traceId, CancellationToken cancellationToken = default)
        => client.SendAsync(HttpMethod.Get, "/api/v1/fraud/decision-logs/" + Validation.Id(traceId), signed: false, cancellationToken: cancellationToken);
    /// <summary>Lists decision logs with cursor paging. pageInfo is a sibling of data in the response envelope.</summary>
    public Task<LunixiResponse> ListDecisionsAsync(IReadOnlyDictionary<string, string>? filters = null, CancellationToken cancellationToken = default)
        => client.SendAsync(HttpMethod.Get, "/api/v1/fraud/decision-logs" + RequestValidation.Query(filters, DecisionFilters), signed: false, cancellationToken: cancellationToken);
}
