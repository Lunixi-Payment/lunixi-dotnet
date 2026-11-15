using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lunixi;

/// <summary>
/// The single description of every identity route the SDK calls (<c>/api/v1/identity/live-sessions*</c>). The surface is
/// bearer-only; the browser fingerprint SDK uses the publishable key (<c>/identity/sdk/init</c>), which is not here. The table
/// is the Node SDK's <c>IDENTITY_ROUTES</c>, row for row, and the shared catalog's group of the same name.
/// </summary>
public static class IdentityRoutes
{
    private const string Live = "/api/v1/identity/live-sessions";

    /// <summary>Body of <c>analyze</c> / <c>analyzeSync</c>; the nested objects are forwarded as given.</summary>
    public static IReadOnlyList<string> AnalyzeFields { get; } =
    [
        "subjectType", "subjectId", "productContext", "integrationMode", "customer", "device",
        "session", "source", "account", "metadata", "paymentId",
    ];

    /// <summary>Every route by name, in the Node table's order.</summary>
    public static IReadOnlyDictionary<string, LunixiRoute> All { get; } = Build();

    private static IReadOnlyDictionary<string, LunixiRoute> Build()
    {
        var analyze = AnalyzeFields.ToArray();
        var routes = new OrderedRoutes
        {
            ["analyzeSync"] = LunixiRoute.Post(Live + "/analyze-sync").WithOptional(analyze),
            ["analyze"] = LunixiRoute.Post(Live + "/analyze").WithOptional(analyze),
            ["accountLinks"] = LunixiRoute.Get(Live + "/:traceId/account-links"),
            ["getSession"] = LunixiRoute.Get(Live + "/:traceId"),
            ["relatedSessions"] = LunixiRoute.Get(Live + "/:traceId/related-sessions").WithQuery("limit"),
            ["timeline"] = LunixiRoute.Get(Live + "/:traceId/timeline"),
            ["topSources"] = LunixiRoute.Get(Live + "/top-sources").WithQuery("days", "limit"),
            ["listSessions"] = LunixiRoute.Get(Live).WithQuery("fingerprintId", "deviceId", "paymentId", "limit"),
        };
        return routes.Freeze();
    }
}

/// <summary>Readers of an <see cref="IdentityClient.AccountLinksAsync"/> answer.</summary>
public static class IdentitySignals
{
    /// <summary>
    /// True when the device is shared by at least <paramref name="threshold"/> accounts, counting the current one
    /// (<c>linkedAccounts</c> excludes it). With the default 2, a second account on the device trips it.
    /// </summary>
    public static bool IsDeviceShared(JsonElement accountLinks, int threshold = 2)
    {
        var linked = accountLinks.ValueKind == JsonValueKind.Object && accountLinks.TryGetProperty("linkedAccounts", out var accounts) &&
            accounts.ValueKind == JsonValueKind.Array ? accounts.GetArrayLength() : 0;
        return linked + 1 >= Math.Max(2, threshold);
    }

    /// <summary>The distinct <c>linkReasons</c> of the answer's sessions (<c>same_device_cluster</c>, <c>same_fingerprint</c>, …), in first-seen order.</summary>
    public static IReadOnlyList<string> LinkReasons(JsonElement accountLinks)
    {
        var reasons = new List<string>();
        if (accountLinks.ValueKind != JsonValueKind.Object || !accountLinks.TryGetProperty("sessions", out var sessions) || sessions.ValueKind != JsonValueKind.Array)
            return reasons;
        foreach (var session in sessions.EnumerateArray())
        {
            if (session.ValueKind != JsonValueKind.Object || !session.TryGetProperty("linkReasons", out var list) || list.ValueKind != JsonValueKind.Array) continue;
            foreach (var reason in list.EnumerateArray())
            {
                var text = reason.ValueKind == JsonValueKind.String ? reason.GetString()! : reason.GetRawText();
                if (!reasons.Contains(text)) reasons.Add(text);
            }
        }
        return reasons;
    }
}

/// <summary>
/// Identity signals (<c>/api/v1/identity/*</c>). Bearer-only. The browser fingerprint SDK produces a <c>traceId</c>; your
/// server reads the linked accounts of that trace and flags or blocks when the device is shared ("one person, many accounts",
/// <see cref="IdentitySignals.IsDeviceShared"/>).
/// <para>
/// This is a signal surface: it carries risk flags, reason codes, similarity probabilities and the identity snapshot, not an
/// allow / review / block verdict (that is the fraud decision). Calls are checked against <see cref="IdentityRoutes"/>; they
/// return the envelope's <c>data</c>, and <see cref="ListSessionsAsync"/> returns <see cref="LunixiPage"/>.
/// </para>
/// </summary>
public sealed class IdentityClient
{
    private static readonly string[] NestedObjects = ["customer", "device", "session", "source", "account", "metadata"];
    private readonly RouteTableInvoker invoker;
    internal IdentityClient(LunixiClient client) => invoker = new RouteTableInvoker(client, IdentityRoutes.All);

    /// <summary>
    /// Synchronous analysis: <c>{ subjectType?, subjectId?, productContext?, integrationMode?, customer?, device?, session?,
    /// source?, account?, metadata?, paymentId? }</c>. Returns <c>{ traceId, analysisStatus, riskFlags, reasonCodes,
    /// identitySnapshot, … }</c>.
    /// </summary>
    public Task<JsonElement> AnalyzeSyncAsync(object request, CancellationToken cancellationToken = default)
        => invoker.DataAsync("analyzeSync", body: AnalyzeBody(request), cancellationToken: cancellationToken);

    /// <summary>Queues the analysis and returns the <c>traceId</c> at once; the same body as <see cref="AnalyzeSyncAsync"/>.</summary>
    public Task<JsonElement> AnalyzeAsync(object request, CancellationToken cancellationToken = default)
        => invoker.DataAsync("analyze", body: AnalyzeBody(request), cancellationToken: cancellationToken);

    /// <summary>Other accounts seen on this device / fingerprint / IP (account keys hashed).</summary>
    public Task<JsonElement> AccountLinksAsync(string traceId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("accountLinks", Id(traceId), cancellationToken: cancellationToken);

    /// <summary>One identity session.</summary>
    public Task<JsonElement> GetSessionAsync(string traceId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("getSession", Id(traceId), cancellationToken: cancellationToken);

    /// <summary>Recent sessions of the same device-identity cluster.</summary>
    public Task<JsonElement> RelatedSessionsAsync(string traceId, int? limit = null, CancellationToken cancellationToken = default)
        => invoker.DataAsync("relatedSessions", Id(traceId), query: RouteTableInvoker.Filters(("limit", limit)), cancellationToken: cancellationToken);

    /// <summary>The session summary and its recorded lifecycle events.</summary>
    public Task<JsonElement> TimelineAsync(string traceId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("timeline", Id(traceId), cancellationToken: cancellationToken);

    /// <summary>Source domains ranked by repeated risky activity; filters <c>days</c>, <c>limit</c>.</summary>
    public Task<JsonElement> TopSourcesAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => invoker.DataAsync("topSources", query: filters, cancellationToken: cancellationToken);

    /// <summary>Identity sessions; filters <c>fingerprintId</c>, <c>deviceId</c>, <c>paymentId</c>, <c>limit</c>.</summary>
    public Task<LunixiPage> ListSessionsAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => invoker.PageAsync("listSessions", null, filters, cancellationToken);

    private static IReadOnlyDictionary<string, string?> Id(string traceId) => RouteTableInvoker.Ids(("traceId", traceId));

    private static JsonObject AnalyzeBody(object request)
    {
        var body = RouteTableInvoker.Changes(request, "analyze");
        foreach (var field in NestedObjects) RouteTableInvoker.ObjectField(body, field);
        if (body.ContainsKey("subjectType") != body.ContainsKey("subjectId"))
            throw new LunixiConfigurationException("subjectType and subjectId go together.");
        return body;
    }
}
