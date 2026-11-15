using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lunixi;

/// <summary>
/// A sanction answer that is not <c>status: "success"</c> although its HTTP status was 2xx. A screening that did not run is
/// never an empty result: treat the subject as not screened and screen again. (When no list publication can be pinned the
/// gateway answers 503, which is a plain <see cref="LunixiApiException"/>.)
/// </summary>
public sealed class SanctionScreeningException : LunixiApiException
{
    internal SanctionScreeningException(LunixiResponse response)
        : base("The sanction screening did not complete; it is not a clear result. Screen again.", response.StatusCode,
            LunixiClient.Property(response.Envelope, "code"), response.RequestId, response.Envelope, null, null)
    { }
}

/// <summary>
/// Sanctions screening (<c>/api/v1/sanction/*</c>). Calls carry the access token minted from your API key (no per-request
/// signature); the key needs the <c>SANCTION_SERVICE</c> product and the <c>sanction:view_results</c> permission.
/// <para>
/// Screening returns matches, not verdicts: the decision is yours. Read a result with <see cref="IsClearScreening"/>: an empty
/// match list is clear only when <c>coverageStatus</c> is <c>COMPLETE</c>. When the screening cannot run the gateway answers
/// 503 and the call throws; a 2xx answer that is not <c>status: "success"</c> throws <see cref="SanctionScreeningException"/>.
/// </para>
/// <para>
/// Calls are checked against <see cref="SanctionRoutes"/> before sending: an unknown body field or filter, a missing required
/// field, an empty <c>anyOf</c> group, a nested element outside its field list or a value outside its set
/// (<see cref="SanctionRoutes.ValueSets"/>) is a <see cref="LunixiConfigurationException"/>. Methods return the envelope's
/// <c>data</c>; the envelope's request id is the HTTP request's id, the screening's own id is <c>screeningRequestId</c>.
/// </para>
/// </summary>
public sealed class SanctionClient
{
    private static readonly string[] BlockingDecisions = ["block", "blocked", "reject", "rejected", "review", "hit", "escalate"];
    private readonly LunixiClient client;
    internal SanctionClient(LunixiClient client) => this.client = client;

    /// <summary>
    /// Whether a screening answer can be read as "nothing found": true only when it has no match or hit of any kind and, when
    /// it reports coverage, its <c>coverageStatus</c> is <c>COMPLETE</c>. A <c>PARTIAL</c> or <c>DEGRADED</c> screening, a
    /// replay without coverage (<c>null</c>), a wallet held by a listed party, a matched procurement ban, a transaction that
    /// needs a case or a blocking decision is never clear. Account and adverse-media screening report no coverage; for them the
    /// hits and flags decide.
    /// </summary>
    /// <param name="result">The <c>data</c> a screening method returned.</param>
    public static bool IsClearScreening(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object) return false;
        foreach (var name in new[] { "matches", "hits", "publicationHits" })
            if (result.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0) return false;
        foreach (var flag in new[] { "isSanctioned", "isBlacklisted", "matched", "caseRequired" })
            if (result.TryGetProperty(flag, out var value) && value.ValueKind == JsonValueKind.True) return false;
        if (LunixiClient.Property(result, "decision") is { } decision && BlockingDecisions.Contains(decision.ToLowerInvariant())) return false;
        if (result.TryGetProperty("coverageStatus", out var coverage))
            return coverage.ValueKind == JsonValueKind.String && coverage.GetString() == SanctionCoverageStatuses.Complete;
        return true;
    }

    // Screening

    /// <summary>
    /// Screens a person or organization: <c>{ name, subjectType?, country?, dateOfBirth?, identifiers? ([{ type, value }]),
    /// riskDomains?, mode?, threshold?, customerReference? }</c>.
    /// </summary>
    public Task<JsonElement> ScreenNameAsync(object request, CancellationToken cancellationToken = default)
        => ScreeningAsync("screenName", request, cancellationToken);

    /// <summary>Screens a crypto wallet address (exact match against listed addresses): <c>{ walletAddress, chain?, mode?, customerReference? }</c>.</summary>
    public Task<JsonElement> ScreenWalletAsync(object request, CancellationToken cancellationToken = default)
        => ScreeningAsync("screenWallet", request, cancellationToken);

    /// <summary>
    /// Screens a bank account or IBAN against your own deny list and account risk data (no list publication):
    /// <c>{ accountValue, accountScheme, providerCode?, productChannel?, customerReference? }</c>.
    /// </summary>
    public Task<JsonElement> ScreenAccountAsync(object request, CancellationToken cancellationToken = default)
        => ScreeningAsync("screenAccount", request, cancellationToken);

    /// <summary>
    /// Screens every party and account of a transaction against one list publication: <c>{ transactionType?, direction?,
    /// amount?, currency?, channel?, occurredAt?, parties?, accounts?, metadataJson? }</c> with at least one party or account.
    /// </summary>
    public Task<JsonElement> ScreenTransactionAsync(object request, CancellationToken cancellationToken = default)
        => ScreeningAsync("screenTransaction", request, cancellationToken);

    /// <summary>
    /// Screens public procurement debarment lists by name and TCKN/VKN (at least one of <c>entityName</c>, <c>tckn</c>,
    /// <c>vkn</c>). A source that is not published makes <c>coverageStatus</c> <c>PARTIAL</c> and is named in
    /// <c>uncoveredSources</c>: no match is not a clean result for it.
    /// </summary>
    public Task<JsonElement> ScreenPublicProcurementBanAsync(object request, CancellationToken cancellationToken = default)
        => ScreeningAsync("screenPublicProcurementBan", request, cancellationToken);

    /// <summary>Screens adverse media; it runs only when your adverse-media policy is enabled.</summary>
    public Task<JsonElement> ScreenAdverseMediaAsync(object request, CancellationToken cancellationToken = default)
        => ScreeningAsync("screenAdverseMedia", request, cancellationToken);

    /// <summary>Evaluates behavioural risk signals.</summary>
    public Task<JsonElement> EvaluateBehavioralAlertAsync(object request, CancellationToken cancellationToken = default)
        => ScreeningAsync("evaluateBehavioralAlert", request, cancellationToken);

    /// <summary>Earlier screenings (audit trail), newest first; filters <c>limit</c>, <c>cursor</c>. List-level fields such as <c>runsToday</c> are in <see cref="LunixiPage.Data"/>.</summary>
    public Task<LunixiPage> ListScreeningHistoryAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => PageAsync("listScreeningHistory", filters, cancellationToken);

    /// <summary>Every screening of <see cref="ListScreeningHistoryAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateScreeningHistoryAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => RouteRequest.IterateAsync(ListScreeningHistoryAsync, filters, cancellationToken);

    // Ongoing monitoring

    /// <summary>
    /// Registers a subject for ongoing monitoring: <c>{ externalCustomerId, subjectType?, profileJson?, frequency?,
    /// riskDomainScope? }</c>; returns <c>{ monitoredEntityId }</c>. A <c>profile</c> object (name, dateOfBirth, country,
    /// identifiers …) is sent as <c>profileJson</c> text; pass one of the two.
    /// </summary>
    public async Task<JsonElement> RegisterMonitoringAsync(object request, CancellationToken cancellationToken = default)
    {
        var body = RouteRequest.ObjectOf(request, "registerMonitoring body");
        if (body["profile"] is { } profile)
        {
            if (body.ContainsKey("profileJson")) throw new LunixiConfigurationException("registerMonitoring takes profile or profileJson, not both.");
            body.Remove("profile");
            body["profileJson"] = System.Text.Encoding.UTF8.GetString(CanonicalJson.Serialize(profile));
        }
        return (await SendAsync("registerMonitoring", null, body, null, cancellationToken).ConfigureAwait(false)).Data;
    }

    /// <summary>New-match alerts raised by monitoring; filters <c>status</c> (for example <c>open</c>), <c>limit</c>, <c>cursor</c>, <c>includeTotal</c>.</summary>
    public Task<LunixiPage> ListMonitoringAlertsAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => PageAsync("listMonitoringAlerts", filters, cancellationToken);

    /// <summary>Every alert of <see cref="ListMonitoringAlertsAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateMonitoringAlertsAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => RouteRequest.IterateAsync(ListMonitoringAlertsAsync, filters, cancellationToken);

    /// <summary>Monitored subjects; filters <c>activeOnly</c> (the gateway defaults to true), <c>limit</c>, <c>cursor</c>, <c>includeTotal</c>.</summary>
    public Task<LunixiPage> ListMonitoredEntitiesAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => PageAsync("listMonitoredEntities", filters, cancellationToken);

    /// <summary>Every subject of <see cref="ListMonitoredEntitiesAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateMonitoredEntitiesAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => RouteRequest.IterateAsync(ListMonitoredEntitiesAsync, filters, cancellationToken);

    /// <summary>Matches raised by monitoring; filters <c>status</c>, <c>limit</c>, <c>cursor</c>.</summary>
    public Task<LunixiPage> ListMonitoringMatchesAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => PageAsync("listMonitoringMatches", filters, cancellationToken);

    /// <summary>Every match of <see cref="ListMonitoringMatchesAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateMonitoringMatchesAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => RouteRequest.IterateAsync(ListMonitoringMatchesAsync, filters, cancellationToken);

    /// <summary>Freshness of the source lists: <c>{ manifest }</c>. A manifest that could not be produced throws <see cref="SanctionScreeningException"/>.</summary>
    public async Task<JsonElement> GetDatasetFreshnessAsync(CancellationToken cancellationToken = default)
        => Screened(await SendAsync("getDatasetFreshness", null, null, null, cancellationToken).ConfigureAwait(false));

    // Entity intelligence

    /// <summary>Entity search by name.</summary>
    public async Task<JsonElement> SearchEntitiesAsync(string q, int? limit = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(q)) throw new LunixiConfigurationException("searchEntities requires q.");
        return (await SendAsync("searchEntities", null, null, Filters(("q", q), ("limit", limit)), cancellationToken).ConfigureAwait(false)).Data;
    }

    /// <summary>An entity's risk profile; pass <paramref name="screeningRequestId"/> to read it on that screening's list publication.</summary>
    public async Task<JsonElement> GetEntityProfileAsync(string entityId, string? screeningRequestId = null, CancellationToken cancellationToken = default)
        => (await SendAsync("getEntityProfile", entityId, null, Filters(("screeningRequestId", screeningRequestId)), cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>An entity's relationship graph, <paramref name="depth"/> 1–3.</summary>
    public async Task<JsonElement> GetEntityGraphAsync(string entityId, int? depth = null, CancellationToken cancellationToken = default)
        => (await SendAsync("getEntityGraph", entityId, null, Filters(("depth", depth)), cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>Indirect exposure paths; <paramref name="entityIds"/> are the records of a merged match (sent comma-separated).</summary>
    public async Task<JsonElement> GetIndirectExposureAsync(string entityId, int? maxDepth = null, IReadOnlyList<string>? entityIds = null,
        CancellationToken cancellationToken = default)
        => (await SendAsync("getIndirectExposure", entityId, null, Filters(("maxDepth", maxDepth), ("entityIds", IdList(entityIds))), cancellationToken)
            .ConfigureAwait(false)).Data;

    /// <summary>Ownership aggregation (50% rule); <paramref name="entityIds"/> are the records of a merged match (sent comma-separated).</summary>
    public async Task<JsonElement> GetOwnershipAggregationAsync(string entityId, int? depth = null, IReadOnlyList<string>? entityIds = null,
        CancellationToken cancellationToken = default)
        => (await SendAsync("getOwnershipAggregation", entityId, null, Filters(("depth", depth), ("entityIds", IdList(entityIds))), cancellationToken)
            .ConfigureAwait(false)).Data;

    // Deny list

    /// <summary>Your own deny-list entries (read only with an API key); filters <c>limit</c>, <c>cursor</c>.</summary>
    public Task<LunixiPage> ListBlacklistEntriesAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => PageAsync("listBlacklistEntries", filters, cancellationToken);

    /// <summary>Every entry of <see cref="ListBlacklistEntriesAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateBlacklistEntriesAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => RouteRequest.IterateAsync(ListBlacklistEntriesAsync, filters, cancellationToken);

    // Requests

    private async Task<JsonElement> ScreeningAsync(string routeName, object request, CancellationToken cancellationToken)
        => Screened(await SendAsync(routeName, null, request, null, cancellationToken).ConfigureAwait(false));

    private async Task<LunixiPage> PageAsync(string routeName, IReadOnlyDictionary<string, object?>? filters, CancellationToken cancellationToken)
        => new(await SendAsync(routeName, null, null, filters, cancellationToken).ConfigureAwait(false));

    /// <summary>A 2xx answer that is not <c>status: "success"</c> is refused rather than read.</summary>
    private static JsonElement Screened(LunixiResponse response)
    {
        if (LunixiClient.Property(response.Envelope, "status") is { } status && status != "success") throw new SanctionScreeningException(response);
        return response.Data;
    }

    private static IReadOnlyDictionary<string, object?> Filters(params (string Name, object? Value)[] values)
        => values.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);

    /// <summary>Member ids of a merged match, comma-separated (the gateway accepts both forms).</summary>
    private static string? IdList(IReadOnlyList<string>? entityIds)
    {
        if (entityIds is null) return null;
        var ids = entityIds.Select(id => id?.Trim()).Where(id => !string.IsNullOrEmpty(id)).ToArray();
        if (ids.Any(id => id!.Contains(','))) throw new LunixiConfigurationException("entityIds must be ids without commas.");
        return ids.Length == 0 ? null : string.Join(',', ids);
    }

    private Task<LunixiResponse> SendAsync(string routeName, string? entityId, object? body, IReadOnlyDictionary<string, object?>? query,
        CancellationToken cancellationToken)
    {
        var route = SanctionRoutes.All[routeName];
        var ids = new Dictionary<string, string?>(StringComparer.Ordinal) { ["entityId"] = entityId };
        var path = RouteRequest.FillPath(route, ids) + RouteRequest.Query(query, route.Query, routeName);
        var payload = route.Method == "GET" ? null : Body(routeName, route, body);
        return client.SendCoreAsync(route.HttpMethod, path, payload, null, false, Validation.MaxIdempotencyKeyLength, cancellationToken);
    }

    /// <summary>The route's fields, then the nested lists element by element and the value sets.</summary>
    private static JsonObject Body(string routeName, LunixiRoute route, object? request)
    {
        var plain = RouteRequest.Fields(RouteRequest.ObjectOf(request, routeName + " body"), route.Required, route.Optional, route.AnyOf, routeName);
        foreach (var name in plain.Select(property => property.Key).ToArray())
        {
            var key = routeName + "." + name;
            if (SanctionRoutes.NestedFields.TryGetValue(key, out var nested)) plain[name] = Rows(plain[name], nested, key);
            if (SanctionRoutes.ValueSets.TryGetValue(key, out var allowed)) Values(plain[name], allowed, key);
        }
        return plain;
    }

    private static JsonArray Rows(JsonNode? value, LunixiNestedFields spec, string label)
    {
        if (value is not JsonArray rows) throw new LunixiConfigurationException($"{label} must be an array.");
        var checkedRows = new JsonArray();
        for (var index = 0; index < rows.Count; index++)
        {
            var rowLabel = $"{label}[{index}]";
            if (rows[index] is not JsonObject row) throw new LunixiConfigurationException($"{rowLabel} must be an object.");
            checkedRows.Add(RouteRequest.Fields(RouteRequest.Compact((JsonObject)row.DeepClone()), spec.Required, spec.Optional, [], rowLabel));
        }
        return checkedRows;
    }

    private static void Values(JsonNode? value, IReadOnlyList<string> allowed, string label)
    {
        JsonNode?[] values = value is JsonArray list ? list.ToArray() : [value];
        if (values.Any(entry => RouteRequest.Text(entry) is not { } text || !allowed.Contains(text)))
            throw new LunixiConfigurationException($"{label} must be one of {string.Join(", ", allowed)}.");
    }
}
