using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lunixi;

/// <summary>
/// KYC, KYB and evidence sessions (<c>/api/v1/kyc/*</c>). Bearer-only (no per-request signature). Your server creates the
/// session (<c>externalCustomerId</c> is your own user id, echoed on every webhook); the browser SDK then runs the capture with
/// the publishable key. The <c>kyc.*</c> webhook (<see cref="KycEvents"/>) is the authority for the verification result.
/// <para>
/// Calls are checked against <see cref="KycRoutes"/> before anything is sent. Session calls return the envelope's <c>data</c>
/// (<c>{ session, capturePolicy, … }</c>; no personal data comes back, only status, decision, risk and the achieved assurance);
/// lists return <see cref="LunixiPage"/>. A document image never appears in an error message.
/// </para>
/// </summary>
public sealed class KycClient
{
    private static readonly string[] SessionListFilters = ["limit", "cursor", "pageSize", "pageToken"];
    private readonly RouteTableInvoker invoker;
    internal KycClient(LunixiClient client) => invoker = new RouteTableInvoker(client, KycRoutes.All);

    /// <summary>
    /// Creates a session: <c>{ subjectId, sessionType (<see cref="KycSessionTypes"/>), externalCustomerId?, customerMode?,
    /// person? | company? | customer?, jurisdiction?, requiredDocumentTypes?, primaryDocumentType?, evidencePurpose?, ttlMinutes?,
    /// metadata? }</c>. <c>person</c> / <c>company</c> are sent as <c>customer</c> with <c>customerType</c> <c>PERSON</c> /
    /// <c>COMPANY</c>; <c>metadata</c> is sent as <c>evidenceContextJson</c> text. An <c>Idempotency-Key</c> (the argument or
    /// the request's own <c>idempotencyKey</c>) goes in the header and the body.
    /// </summary>
    public Task<JsonElement> CreateSessionAsync(object request, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        var (body, key) = SessionBody("createSession", request, idempotencyKey);
        return invoker.DataAsync("createSession", body: body, idempotencyKey: key, cancellationToken: cancellationToken);
    }

    /// <summary>One session.</summary>
    public Task<JsonElement> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("getSession", Id(sessionId), cancellationToken: cancellationToken);

    /// <summary>
    /// Sessions; filters <c>limit</c>, <c>cursor</c> (<c>pageSize</c> / <c>pageToken</c> are read as the same). Anything else
    /// is refused: the merchant list has no status or customer filter, and dropping one would return every session.
    /// </summary>
    public Task<LunixiPage> ListSessionsAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
    {
        var given = filters ?? new Dictionary<string, object?>();
        var unknown = given.Where(filter => filter.Value is not null && !SessionListFilters.Contains(filter.Key)).Select(filter => filter.Key).ToArray();
        if (unknown.Length > 0)
            throw new LunixiConfigurationException($"listSessions does not accept the filter(s) {string.Join(", ", unknown)}; it takes limit, cursor.");
        var query = RouteTableInvoker.Filters(("limit", Value(given, "limit") ?? Value(given, "pageSize")), ("cursor", Value(given, "cursor") ?? Value(given, "pageToken")));
        return invoker.PageAsync("listSessions", null, query, cancellationToken);
    }

    /// <summary>Every session of <see cref="ListSessionsAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateSessionsAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => RouteRequest.IterateAsync(ListSessionsAsync, filters, cancellationToken);

    /// <summary>Sets the applicant jurisdiction (ISO country) on a server-created session.</summary>
    public Task<JsonElement> SetJurisdictionAsync(string sessionId, string jurisdiction, CancellationToken cancellationToken = default)
        => invoker.DataAsync("setJurisdiction", Id(sessionId), new JsonObject { ["jurisdiction"] = RouteTableInvoker.RequireText(jurisdiction, "jurisdiction") },
            cancellationToken: cancellationToken);

    /// <summary>Sets the primary document: <c>PASSPORT</c>, <c>NATIONAL_ID</c>, <c>DRIVERS_LICENSE</c> or <c>RESIDENCE_PERMIT</c>.</summary>
    public Task<JsonElement> SetPrimaryDocumentAsync(string sessionId, string documentType, CancellationToken cancellationToken = default)
        => invoker.DataAsync("setPrimaryDocument", Id(sessionId), new JsonObject { ["documentType"] = RouteTableInvoker.RequireText(documentType, "documentType") },
            cancellationToken: cancellationToken);

    /// <summary>
    /// Uploads a document from the server (most flows use the browser SDK): <c>{ documentType, fileBase64, mimeType, side?,
    /// jurisdiction?, metadataJson?, idempotencyKey? }</c>. The image never appears in an error message.
    /// </summary>
    public Task<JsonElement> UploadDocumentAsync(string sessionId, object document, CancellationToken cancellationToken = default)
        => invoker.DataAsync("uploadDocument", Id(sessionId), RouteTableInvoker.Changes(document, "uploadDocument"), cancellationToken: cancellationToken);

    /// <summary>The session's event timeline (audit).</summary>
    public Task<JsonElement> TimelineAsync(string sessionId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("timeline", Id(sessionId), cancellationToken: cancellationToken);

    /// <summary>Submits the session for processing.</summary>
    public Task<JsonElement> SubmitAsync(string sessionId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("submit", Id(sessionId), cancellationToken: cancellationToken);

    /// <summary>Sends the session to manual review.</summary>
    public Task<JsonElement> ManualReviewAsync(string sessionId, string? reasonCode = null, string? reasonDetail = null, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject();
        if (RouteTableInvoker.OptionalText(reasonCode) is { } code) body["reasonCode"] = code;
        if (RouteTableInvoker.OptionalText(reasonDetail) is { } detail) body["reasonDetail"] = detail;
        return invoker.DataAsync("manualReview", Id(sessionId), body, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// An <c>EVIDENCE</c> session (proof-of-X), optionally linked to a payment or order; the same body as
    /// <see cref="CreateSessionAsync"/>.
    /// </summary>
    public Task<JsonElement> EvidenceRequestAsync(object request, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        var (body, key) = SessionBody("evidenceRequest", request, idempotencyKey);
        return invoker.DataAsync("evidenceRequest", body: body, idempotencyKey: key, cancellationToken: cancellationToken);
    }

    /// <summary>Your KYC policy (required assurance, providers…).</summary>
    public Task<JsonElement> MerchantPolicyAsync(CancellationToken cancellationToken = default)
        => invoker.DataAsync("merchantPolicy", cancellationToken: cancellationToken);

    /// <summary>Changes your KYC policy.</summary>
    public Task<JsonElement> UpdateMerchantPolicyAsync(object policy, CancellationToken cancellationToken = default)
        => invoker.DataAsync("updateMerchantPolicy", body: RouteTableInvoker.Changes(policy, "updateMerchantPolicy"), cancellationToken: cancellationToken);

    /// <summary>Rotates the KYC webhook signing secret.</summary>
    public Task<JsonElement> RotateWebhookSecretAsync(CancellationToken cancellationToken = default)
        => invoker.DataAsync("rotateWebhookSecret", cancellationToken: cancellationToken);

    /// <summary>KYC webhook deliveries; filters <c>limit</c>, <c>status</c>.</summary>
    public Task<LunixiPage> WebhookOutboxAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => invoker.PageAsync("webhookOutbox", null, filters, cancellationToken);

    /// <summary>Delivers a KYC webhook event again.</summary>
    public Task<JsonElement> RetryWebhookEventAsync(string eventId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("retryWebhookEvent", RouteTableInvoker.Ids(("eventId", eventId)), cancellationToken: cancellationToken);

    /// <summary>Data erasure requests; filters <c>limit</c>, <c>status</c>.</summary>
    public Task<LunixiPage> ErasureRequestsAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => invoker.PageAsync("erasureRequests", null, filters, cancellationToken);

    private static IReadOnlyDictionary<string, string?> Id(string sessionId) => RouteTableInvoker.Ids(("sessionId", sessionId));

    private static object? Value(IReadOnlyDictionary<string, object?> filters, string name) => filters.TryGetValue(name, out var value) ? value : null;

    private static (JsonObject Body, string? Key) SessionBody(string name, object request, string? idempotencyKey)
    {
        var body = RouteTableInvoker.Changes(request, name);
        var person = Take(body, "person");
        var company = Take(body, "company");
        var metadata = Take(body, "metadata");
        var key = RouteTableInvoker.BodyKey(body, idempotencyKey, name);
        if (body["sessionType"] is { } type) body["sessionType"] = RouteTableInvoker.OneOf(type, "sessionType", KycSessionTypes.All, upper: true);
        if (new[] { person, company, body["customer"] }.Count(identity => identity is not null) > 1)
            throw new LunixiConfigurationException($"{name} takes one of person, company or customer.");
        if (person is not null) body["customer"] = Customer("PERSON", person, "person");
        if (company is not null) body["customer"] = Customer("COMPANY", company, "company");
        if (metadata is not null)
        {
            if (body.ContainsKey("evidenceContextJson")) throw new LunixiConfigurationException($"{name} takes metadata or evidenceContextJson, not both.");
            if (metadata is not JsonObject) throw new LunixiConfigurationException("metadata must be an object.");
            body["evidenceContextJson"] = Encoding.UTF8.GetString(CanonicalJson.Serialize(metadata));
        }
        if (body["ttlMinutes"] is { } ttl && RouteTableInvoker.Integer(ttl) is not (>= 1 and <= KycRoutes.SessionTtlMaxMinutes))
            throw new LunixiConfigurationException($"ttlMinutes must be an integer from 1 to {KycRoutes.SessionTtlMaxMinutes}.");
        if (body["requiredDocumentTypes"] is { } types && (types is not JsonArray list || list.Any(entry => RouteRequest.Text(entry) is null)))
            throw new LunixiConfigurationException("requiredDocumentTypes must be an array of document types.");
        return (body, key);
    }

    private static JsonNode? Take(JsonObject body, string name)
    {
        var value = body[name];
        body.Remove(name);
        return value;
    }

    private static JsonObject Customer(string customerType, JsonNode value, string field)
    {
        if (value is not JsonObject identity) throw new LunixiConfigurationException($"{field} must be an object.");
        var customer = new JsonObject { ["customerType"] = customerType };
        foreach (var property in identity) customer[property.Key] = property.Value?.DeepClone();
        return customer;
    }
}
