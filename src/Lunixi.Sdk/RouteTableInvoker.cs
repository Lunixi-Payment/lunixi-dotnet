using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lunixi;

/// <summary>
/// Sends one route of a route table (<see cref="SubscriptionRoutes"/>, <see cref="KycRoutes"/>, <see cref="IdentityRoutes"/>,
/// <see cref="MarketplaceRoutes"/>) exactly as the row describes it: the path is filled from the identifiers, the
/// <c>Idempotency-Key</c> rule is applied, the filters and the body are checked against the row's lists (a free body only has
/// to be an object), and the call is signed only when the row says <c>stepUp</c>. On the subscription and marketplace
/// contracts the money members of the body, the filters and the answer must be canonical minor-unit strings. Every check runs
/// before anything leaves the process and names the field, never its value.
/// </summary>
internal sealed class RouteTableInvoker
{
    private readonly LunixiClient client;
    private readonly IReadOnlyDictionary<string, LunixiRoute> routes;

    internal RouteTableInvoker(LunixiClient client, IReadOnlyDictionary<string, LunixiRoute> routes)
    {
        this.client = client;
        this.routes = routes;
    }

    internal static IReadOnlyDictionary<string, string?> Ids(params (string Name, string? Value)[] values)
        => values.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);

    internal static IReadOnlyDictionary<string, object?> Filters(params (string Name, object? Value)[] values)
        => values.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);

    /// <summary>The envelope's <c>data</c>.</summary>
    internal async Task<JsonElement> DataAsync(string routeName, IReadOnlyDictionary<string, string?>? ids = null, object? body = null,
        IReadOnlyDictionary<string, object?>? query = null, string? idempotencyKey = null, CancellationToken cancellationToken = default)
        => (await SendAsync(routeName, ids, body, query, idempotencyKey, cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>A list answer as one <see cref="LunixiPage"/>.</summary>
    internal async Task<LunixiPage> PageAsync(string routeName, IReadOnlyDictionary<string, string?>? ids,
        IReadOnlyDictionary<string, object?>? filters, CancellationToken cancellationToken)
        => new(await SendAsync(routeName, ids, null, filters, null, cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Answers that put their fields at the envelope root and/or under <c>data</c> (the portal SDK session, the dealer
    /// onboarding link): both, merged, <c>data</c> winning.
    /// </summary>
    internal async Task<JsonElement> MergedAsync(string routeName, IReadOnlyDictionary<string, string?>? ids, object? body,
        CancellationToken cancellationToken)
        => MergeRoot((await SendAsync(routeName, ids, body, null, null, cancellationToken).ConfigureAwait(false)).Envelope);

    internal async Task<LunixiResponse> SendAsync(string routeName, IReadOnlyDictionary<string, string?>? ids, object? body,
        IReadOnlyDictionary<string, object?>? query, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var route = routes[routeName];
        var path = RouteRequest.FillPath(route, ids);
        var key = RouteRequest.Key(route, idempotencyKey, routeName);
        var allowed = route.AnyQuery ? query?.Keys.ToArray() ?? [] : route.Query;
        var queryText = RouteRequest.Query(query, allowed, routeName);
        var payload = Payload(routeName, route, body);
        var money = Money.IsWireMoneyRoute(route.Path);
        if (money)
        {
            if (payload is not null) Money.ValidateWireFields(CanonicalJson.Element(payload));
            if (query is not null) WireFilters(query);
        }
        var response = await client.SendCoreAsync(route.HttpMethod, path + queryText, payload, key, route.StepUp,
            Validation.MaxIdempotencyKeyLength, cancellationToken).ConfigureAwait(false);
        if (money)
        {
            try { Money.ValidateWireFields(response.Envelope); }
            catch (LunixiConfigurationException error)
            { throw new LunixiTransportException("The gateway answer is not usable: " + error.Message, route.Method != "GET"); }
        }
        return response;
    }

    /// <summary>
    /// The body of a route: the row's fields (unknown refused, required present), a free object, or nothing. With
    /// <c>optionalBody</c> an empty body is sent as no body.
    /// </summary>
    private static JsonObject? Payload(string routeName, LunixiRoute route, object? body)
    {
        if (route.Required.Count + route.Optional.Count > 0)
        {
            var plain = RouteRequest.Fields(RouteRequest.ObjectOf(body, routeName + " body"), route.Required, route.Optional, route.AnyOf, routeName);
            return route.OptionalBody && plain.Count == 0 ? null : plain;
        }
        if (route.Free)
        {
            var plain = RouteRequest.ObjectOf(body, routeName + " body");
            return route.OptionalBody && plain.Count == 0 ? null : plain;
        }
        if (body is not null) throw new LunixiConfigurationException($"{routeName} takes no body.");
        return null;
    }

    /// <summary>A money filter (for example <c>mtdVolumeMinor</c>) is a canonical minor-unit string, never a number.</summary>
    private static void WireFilters(IReadOnlyDictionary<string, object?> query)
    {
        foreach (var (name, value) in query)
            if (value is not null && Money.WireMoneyFields.Contains(name)) Money.WireAmount(name, value as string);
    }

    private static JsonElement MergeRoot(JsonElement envelope)
    {
        var merged = new JsonObject();
        if (envelope.ValueKind != JsonValueKind.Object) return CanonicalJson.Element(merged);
        foreach (var property in envelope.EnumerateObject())
            if (property.Name != "data") merged[property.Name] = JsonNode.Parse(property.Value.GetRawText());
        if (envelope.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            foreach (var property in data.EnumerateObject()) merged[property.Name] = JsonNode.Parse(property.Value.GetRawText());
        return CanonicalJson.Element(merged);
    }

    /// <summary>A body that must be a JSON object (not null).</summary>
    internal static JsonObject Changes(object? value, string label)
    {
        if (value is null) throw new LunixiConfigurationException($"{label} body must be an object.");
        return RouteRequest.ObjectOf(value, label + " body");
    }

    /// <summary>Optional text: null and empty are left out.</summary>
    internal static string? OptionalText(string? value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>Required text: trimmed, non-empty.</summary>
    internal static string RequireText(string? value, string field)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text)) throw new LunixiConfigurationException($"{field} is required.");
        return text;
    }

    /// <summary>
    /// The request's own <c>idempotencyKey</c> or the argument; when both are given they must agree. The key is put back into
    /// the body (the subscription, plan-change and KYC session contracts carry it there as well).
    /// </summary>
    internal static string? BodyKey(JsonObject body, string? idempotencyKey, string label)
    {
        var own = RouteRequest.Text(body["idempotencyKey"])?.Trim();
        if (string.IsNullOrEmpty(own)) own = null;
        var given = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        if (own is not null && given is not null && own != given)
            throw new LunixiConfigurationException($"{label}: the request's idempotencyKey and the idempotencyKey argument differ.");
        var key = given ?? own;
        if (key is not null) body["idempotencyKey"] = key;
        return key;
    }

    /// <summary>A value of a closed set, optionally upper-cased first; the message lists the set, never the value.</summary>
    internal static string OneOf(JsonNode? value, string field, IReadOnlyList<string> allowed, bool upper = false)
    {
        var text = RouteRequest.Text(value);
        if (text is not null && upper) text = text.ToUpperInvariant();
        if (text is null || !allowed.Contains(text)) throw new LunixiConfigurationException($"{field} must be one of {string.Join(", ", allowed)}.");
        return text;
    }

    /// <summary>A JSON boolean when present.</summary>
    internal static void Boolean(JsonObject body, string field, string? label = null)
    {
        if (body[field] is { } value && value.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False))
            throw new LunixiConfigurationException($"{label ?? field} must be a boolean.");
    }

    /// <summary>A JSON integer of at least <paramref name="min"/> when present.</summary>
    internal static void Count(JsonObject body, string field, long min, string? label = null)
    {
        if (body[field] is { } value && !(Integer(value) is { } number && number >= min))
            throw new LunixiConfigurationException($"{label ?? field} must be an integer of at least {min}.");
    }

    /// <summary>A JSON object when present.</summary>
    internal static void ObjectField(JsonObject body, string field)
    {
        if (body[field] is { } value && value is not JsonObject) throw new LunixiConfigurationException($"{field} must be an object.");
    }

    /// <summary>The integer value of a JSON number, or null when it is not an integer.</summary>
    internal static long? Integer(JsonNode? value)
        => value is JsonValue number && number.GetValueKind() == JsonValueKind.Number
            ? number.TryGetValue<long>(out var result) ? result
            : number.TryGetValue<int>(out var small) ? small
            : null
            : null;
}
