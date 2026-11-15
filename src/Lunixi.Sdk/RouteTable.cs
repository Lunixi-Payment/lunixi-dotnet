using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Lunixi;

/// <summary>
/// One row of a route table (<see cref="WalletRoutes"/>, <see cref="SanctionRoutes"/>, <see cref="BankTransferRoutes"/>,
/// <see cref="SubscriptionRoutes"/>, <see cref="KycRoutes"/>, <see cref="IdentityRoutes"/>, <see cref="MarketplaceRoutes"/>):
/// the HTTP method, the path template (<c>:name</c> segments are filled from arguments), whether the call is signed, the
/// <c>Idempotency-Key</c> rule, and the body and query fields it accepts. The clients check every call against its row before
/// anything is sent; the tables are the same as the Node SDK's and the shared operation catalog's.
/// </summary>
public sealed class LunixiRoute
{
    /// <summary><see cref="IdempotencyKeyRule"/>: the gateway rejects the call without an <c>Idempotency-Key</c>.</summary>
    public const string KeyRequired = "required";
    /// <summary><see cref="IdempotencyKeyRule"/>: an <c>Idempotency-Key</c> is accepted but not required.</summary>
    public const string KeyOptional = "optional";
    /// <summary><see cref="IdempotencyKeyRule"/>: the route takes no <c>Idempotency-Key</c>.</summary>
    public const string KeyNone = "none";

    /// <summary><c>GET</c>, <c>POST</c>, <c>PUT</c>, <c>PATCH</c> or <c>DELETE</c>.</summary>
    public string Method { get; private set; }
    /// <summary>The path template, for example <c>/api/v1/wallet/end-users/:endUserId</c>.</summary>
    public string Path { get; private set; }
    /// <summary>The <c>Idempotency-Key</c> rule: <see cref="KeyRequired"/>, <see cref="KeyOptional"/> or <see cref="KeyNone"/>.</summary>
    public string IdempotencyKeyRule { get; private set; } = KeyNone;
    /// <summary>True when the gateway rejects the call without an <c>Idempotency-Key</c>.</summary>
    public bool IdempotencyKey => IdempotencyKeyRule == KeyRequired;
    /// <summary>
    /// True when the call carries the per-request Ed25519 signature (step-up). Only the subscription, KYC, identity and
    /// marketplace tables state it; the wallet, sanction and bank transfer clients sign as their product requires.
    /// </summary>
    public bool StepUp { get; private set; }
    /// <summary>True for a sanction screening: the answer must say <c>status: "success"</c> to be read.</summary>
    public bool Screening { get; private set; }
    /// <summary>True when a 2xx answer is a file (an image), not the JSON envelope.</summary>
    public bool Binary { get; private set; }
    /// <summary>True when the body is an object the gateway validates itself (no field list).</summary>
    public bool Free { get; private set; }
    /// <summary>True when an empty body is sent as no body.</summary>
    public bool OptionalBody { get; private set; }
    /// <summary>True when the route accepts any scalar filter (no filter list).</summary>
    public bool AnyQuery { get; private set; }
    /// <summary>Body fields the call cannot do without.</summary>
    public IReadOnlyList<string> Required { get; private set; } = [];
    /// <summary>Other body fields the gateway accepts.</summary>
    public IReadOnlyList<string> Optional { get; private set; } = [];
    /// <summary>At least one of these body fields must be present (a list: non-empty).</summary>
    public IReadOnlyList<string> AnyOf { get; private set; } = [];
    /// <summary>Accepted query parameters.</summary>
    public IReadOnlyList<string> Query { get; private set; } = [];

    private LunixiRoute(string method, string path) { Method = method; Path = path; }

    /// <summary>True when the route takes a JSON body (it lists body fields or takes a free body).</summary>
    public bool HasBody => Required.Count + Optional.Count > 0 || Free;

    internal static LunixiRoute Get(string path) => new("GET", path);
    internal static LunixiRoute Post(string path) => new("POST", path);
    internal static LunixiRoute Put(string path) => new("PUT", path);
    internal static LunixiRoute Patch(string path) => new("PATCH", path);
    internal static LunixiRoute Delete(string path) => new("DELETE", path);
    internal LunixiRoute Keyed() => Copy(route => route.IdempotencyKeyRule = KeyRequired);
    internal LunixiRoute KeyAccepted() => Copy(route => route.IdempotencyKeyRule = KeyOptional);
    internal LunixiRoute Signed() => Copy(route => route.StepUp = true);
    internal LunixiRoute AsScreening() => Copy(route => route.Screening = true);
    internal LunixiRoute AsBinary() => Copy(route => route.Binary = true);
    internal LunixiRoute AsFree() => Copy(route => route.Free = true);
    internal LunixiRoute WithOptionalBody() => Copy(route => route.OptionalBody = true);
    internal LunixiRoute WithAnyQuery() => Copy(route => route.AnyQuery = true);
    internal LunixiRoute WithRequired(params string[] fields) => Copy(route => route.Required = Array.AsReadOnly(fields));
    internal LunixiRoute WithOptional(params string[] fields) => Copy(route => route.Optional = Array.AsReadOnly(fields));
    internal LunixiRoute WithAnyOf(params string[] fields) => Copy(route => route.AnyOf = Array.AsReadOnly(fields));
    internal LunixiRoute WithQuery(params string[] fields) => Copy(route => route.Query = Array.AsReadOnly(fields));

    private LunixiRoute Copy(Action<LunixiRoute> change)
    {
        var copy = (LunixiRoute)MemberwiseClone();
        change(copy);
        return copy;
    }

    internal HttpMethod HttpMethod => Method switch
    {
        "GET" => HttpMethod.Get,
        "POST" => HttpMethod.Post,
        "PUT" => HttpMethod.Put,
        "PATCH" => HttpMethod.Patch,
        "DELETE" => HttpMethod.Delete,
        _ => new HttpMethod(Method),
    };

    /// <summary>Returns the method and path template.</summary>
    public override string ToString() => Method + " " + Path;
}

/// <summary>
/// Request checks shared by the route-table clients (wallet, sanction, bank connect, insurance). Every check runs before
/// anything leaves the process and fails with a <see cref="LunixiConfigurationException"/> that names the field, never its value.
/// </summary>
internal static class RouteRequest
{
    private static readonly Regex PathParameter = new(":([A-Za-z]+)", RegexOptions.CultureInvariant);

    /// <summary>The path template with every <c>:name</c> segment filled (trimmed, URL-encoded).</summary>
    internal static string FillPath(LunixiRoute route, IReadOnlyDictionary<string, string?>? parameters)
        => PathParameter.Replace(route.Path, match =>
        {
            var name = match.Groups[1].Value;
            string? value = null;
            parameters?.TryGetValue(name, out value);
            return PathId(value, name);
        });

    /// <summary>A path segment: non-empty after trimming, at most 256 characters, no control characters, not <c>.</c> or <c>..</c>.</summary>
    internal static string PathId(string? value, string field) => Uri.EscapeDataString(Id(value, field));

    /// <summary>The trimmed identifier, checked like <see cref="PathId"/> but not encoded.</summary>
    internal static string Id(string? value, string field)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text)) throw new LunixiConfigurationException($"{field} is required.");
        if (text is "." or ".." || text.Length > 256 || text.Any(char.IsControl))
            throw new LunixiConfigurationException($"{field} is not a valid identifier.");
        return text;
    }

    /// <summary>The request as a JSON object (null is an empty object). Null members are left out.</summary>
    internal static JsonObject ObjectOf(object? request, string label)
    {
        if (request is null) return new JsonObject();
        JsonNode? node;
        try { node = request is JsonNode json ? json.DeepClone() : JsonNode.Parse(CanonicalJson.Serialize(request)); }
        catch (JsonException) { throw new LunixiConfigurationException($"{label} must be an object."); }
        if (node is not JsonObject body) throw new LunixiConfigurationException($"{label} must be an object.");
        return Compact(body);
    }

    /// <summary>The object without its null members.</summary>
    internal static JsonObject Compact(JsonObject body)
    {
        foreach (var name in body.Where(property => property.Value is null).Select(property => property.Key).ToArray()) body.Remove(name);
        return body;
    }

    /// <summary>
    /// Keeps the fields of a route: an unknown field or a missing required one is an error, and with <c>anyOf</c> at least one
    /// of the group must be present. The body is returned in the order given, without null members.
    /// </summary>
    internal static JsonObject Fields(JsonObject body, IReadOnlyList<string> required, IReadOnlyList<string> optional,
        IReadOnlyList<string> anyOf, string label)
    {
        var unknown = body.Select(property => property.Key).Where(name => !required.Contains(name) && !optional.Contains(name)).ToArray();
        if (unknown.Length > 0)
            throw new LunixiConfigurationException(
                $"{label} does not accept {string.Join(", ", unknown)}. Allowed: {string.Join(", ", required.Concat(optional))}. The gateway rejects unknown fields.");
        var missing = required.Where(name => !Present(body[name])).ToArray();
        if (missing.Length > 0) throw new LunixiConfigurationException($"{label} requires {string.Join(", ", missing)}.");
        if (anyOf.Count > 0 && !anyOf.Any(name => Present(body[name])))
            throw new LunixiConfigurationException($"{label} requires at least one of {string.Join(", ", anyOf)}.");
        return body;
    }

    /// <summary>Not null, not an empty string and not an empty list.</summary>
    internal static bool Present(JsonNode? value) => value switch
    {
        null => false,
        JsonValue text when text.TryGetValue<string>(out var s) => s.Length > 0,
        JsonArray list => list.Count > 0,
        _ => true,
    };

    /// <summary>A JSON string value, or null for any other node.</summary>
    internal static string? Text(JsonNode? value) => value is JsonValue node && node.TryGetValue<string>(out var text) ? text : null;

    /// <summary>
    /// The query string of a route (with its leading <c>?</c>, or empty). Unknown filter names are refused; null and empty
    /// values are left out; the parameters are sent in the route's order. <paramref name="convert"/> turns one value into its
    /// wire text (default: <see cref="ScalarText"/>).
    /// </summary>
    internal static string Query(IReadOnlyDictionary<string, object?>? filters, IReadOnlyList<string> allowed, string label,
        Func<string, object, string>? convert = null)
    {
        if (filters is null || filters.Count == 0) return "";
        var unknown = filters.Keys.Where(key => key is null || !allowed.Contains(key)).ToArray();
        if (unknown.Length > 0)
            throw new LunixiConfigurationException(allowed.Count == 0
                ? $"{label} takes no filters."
                : $"{label} does not accept the filter(s) {string.Join(", ", unknown)}; it takes {string.Join(", ", allowed)}. The gateway rejects unknown query fields.");
        var pairs = new List<string>();
        foreach (var key in allowed)
        {
            if (!filters.TryGetValue(key, out var value) || value is null || value is "") continue;
            if (value is IEnumerable and not string && !((IEnumerable)value).Cast<object?>().Any()) continue;
            var text = convert is null ? ScalarText(label, key, value) : convert(key, value);
            pairs.Add(Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(text));
        }
        return pairs.Count == 0 ? "" : "?" + string.Join('&', pairs);
    }

    /// <summary>Text as given, a boolean as <c>true</c>/<c>false</c>, an integer in invariant digits, a time as an ISO-8601 UTC instant.</summary>
    internal static string ScalarText(string label, string key, object value) => value switch
    {
        string text => text,
        bool flag => flag ? "true" : "false",
        int or long or short or byte or uint or ushort => Convert.ToString(value, CultureInfo.InvariantCulture)!,
        DateTimeOffset time => Instant(time),
        DateTime time => Instant(new DateTimeOffset(time)),
        _ => throw new LunixiConfigurationException($"{label} filter {key} must be a string, an integer or a boolean."),
    };

    /// <summary>An integer filter value, or null when <paramref name="value"/> is not an integer type.</summary>
    internal static long? Integer(object value) => value switch
    {
        int n => n,
        long n => n,
        short n => n,
        byte n => n,
        uint n => n,
        ushort n => n,
        _ => null,
    };

    /// <summary>One value or a list of values (strings), each checked against <paramref name="allowed"/>.</summary>
    internal static IReadOnlyList<string> Values(string key, object value, IReadOnlyList<string> allowed)
    {
        string[]? values = value is string single ? new[] { single } : value is IEnumerable<string> list ? list.ToArray() : null;
        if (values is null || values.Length == 0 || values.Any(entry => !allowed.Contains(entry)))
            throw new LunixiConfigurationException($"'{key}' must be one of {string.Join(", ", allowed)}.");
        return values;
    }

    /// <summary>A time filter: a <see cref="DateTimeOffset"/> / <see cref="DateTime"/> (sent as a UTC instant) or non-empty ISO-8601 text.</summary>
    internal static string Time(string key, object value) => value switch
    {
        DateTimeOffset time => Instant(time),
        DateTime time => Instant(new DateTimeOffset(time)),
        string text when text.Trim().Length > 0 => text.Trim(),
        _ => throw new LunixiConfigurationException($"'{key}' must be a DateTimeOffset or an ISO-8601 string with a time zone."),
    };

    /// <summary>A time body field (a JSON string, already serialized from a <see cref="DateTimeOffset"/> or given as ISO-8601 text).</summary>
    internal static string TimeField(JsonNode? value, string field)
    {
        var text = Text(value)?.Trim();
        if (string.IsNullOrEmpty(text)) throw new LunixiConfigurationException($"'{field}' must be a DateTimeOffset or an ISO-8601 string with a time zone.");
        return text;
    }

    internal static string Instant(DateTimeOffset time) => time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>The instant of a time filter, or null when the text cannot be read (the gateway then decides).</summary>
    internal static DateTimeOffset? ReadTime(object? value) => value switch
    {
        DateTimeOffset time => time,
        DateTime time => new DateTimeOffset(time),
        string text when DateTimeOffset.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) => parsed,
        _ => null,
    };

    /// <summary>
    /// The <c>Idempotency-Key</c> rule of a route-table call: required on a keyed route, passed on when given on a route that
    /// accepts one, refused on any other. The key's format (printable ASCII, length) is checked by the transport.
    /// </summary>
    internal static string? Key(LunixiRoute route, string? idempotencyKey, string label)
    {
        if (route.IdempotencyKeyRule == LunixiRoute.KeyRequired)
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey))
                throw new LunixiConfigurationException($"A stable Idempotency-Key is required for {label}. Reuse the same key when retrying the same operation.");
            return idempotencyKey;
        }
        if (idempotencyKey is null) return null;
        if (route.IdempotencyKeyRule != LunixiRoute.KeyOptional) throw new LunixiConfigurationException($"{label} does not take an Idempotency-Key.");
        if (idempotencyKey.Trim().Length == 0) throw new LunixiConfigurationException($"The Idempotency-Key of {label} must be a non-empty string.");
        return idempotencyKey;
    }

    /// <summary>
    /// The caller's key checked against <c>^[\x21-\x7E]{8,200}$</c>, or a new UUID v4 when none is given. Used by the routes
    /// whose key the SDK may generate (bank transfers and exchange orders).
    /// </summary>
    internal static string KeyOrNew(string? idempotencyKey)
    {
        if (string.IsNullOrEmpty(idempotencyKey)) return Guid.NewGuid().ToString();
        return PrintableKey(idempotencyKey);
    }

    /// <summary>8–200 printable ASCII characters without spaces, checked exactly as given.</summary>
    internal static string PrintableKey(string idempotencyKey)
    {
        if (idempotencyKey.Length is < 8 or > 200 || idempotencyKey.Any(c => c < 0x21 || c > 0x7e))
            throw new LunixiConfigurationException("Idempotency-Key must be 8-200 printable ASCII characters without spaces.");
        return idempotencyKey;
    }

    /// <summary>A positive canonical minor-unit string (for example <c>"12550"</c>), never a JSON number or a decimal.</summary>
    internal static string PositiveMinor(JsonNode? value, string field)
    {
        var text = Text(value);
        try { if (text is not null) { Money.Parse(text, positive: true); return text; } }
        catch (LunixiConfigurationException) { }
        throw new LunixiConfigurationException($"'{field}' must be a positive canonical string in minor units (for example \"12550\" for 125.50), not a JSON number.");
    }

    /// <summary>Length in Unicode code points, the way the gateway counts characters.</summary>
    internal static int CodePoints(string value) => value.EnumerateRunes().Count();

    internal static string Currency(object? value, string field)
    {
        var currency = (value as string ?? Text(value as JsonNode))?.Trim().ToUpperInvariant();
        if (currency is null || currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z'))
            throw new LunixiConfigurationException($"'{field}' must be an ISO 4217 code such as TRY.");
        return currency;
    }

    /// <summary>
    /// Walks every page, sending the previous page's <see cref="LunixiPage.NextCursor"/> as <paramref name="cursorName"/>
    /// (<c>cursor</c>, or <c>pageToken</c> for the subscription lists) until the last page.
    /// </summary>
    internal static async IAsyncEnumerable<JsonElement> IterateAsync(
        Func<IReadOnlyDictionary<string, object?>, CancellationToken, Task<LunixiPage>> fetchPage,
        IReadOnlyDictionary<string, object?>? filters, [EnumeratorCancellation] CancellationToken cancellationToken,
        string cursorName = "cursor")
    {
        var request = filters is null ? new Dictionary<string, object?>(StringComparer.Ordinal) : new Dictionary<string, object?>(filters, StringComparer.Ordinal);
        while (true)
        {
            var page = await fetchPage(request, cancellationToken).ConfigureAwait(false);
            foreach (var item in page.Items) yield return item;
            if (!page.HasMore || page.NextCursor is null) yield break;
            request[cursorName] = page.NextCursor;
        }
    }
}

/// <summary>
/// One page of a route-table list (wallet, sanction, bank connect, insurance, subscriptions, KYC, identity, marketplace). The
/// rows are <c>data</c> when it is a list, otherwise <c>data.items</c> (wallet operations: <c>data.operations</c>); the paging
/// metadata is the envelope's <c>pageInfo</c> or a copy inside <c>data</c>. Without a <c>pageInfo</c> cursor the next page is
/// <c>nextCursor</c> or <c>nextPageToken</c> in <c>data</c> or at the envelope root; a <c>pageInfo</c> that names no cursor is
/// the last page. The cursor is opaque: send <see cref="NextCursor"/> back unchanged as the <c>cursor</c> filter (the
/// subscription lists: <c>pageToken</c>). <see cref="HasMore"/> is true only with a non-empty cursor.
/// </summary>
public sealed class LunixiPage
{
    /// <summary>The rows of this page, in wire order.</summary>
    public IReadOnlyList<JsonElement> Items { get; }
    /// <summary>True when a further page exists.</summary>
    public bool HasMore { get; }
    /// <summary>The cursor of the next page, or null on the last page.</summary>
    public string? NextCursor { get; }
    /// <summary>The total row count when the list reports one (<c>totalCount</c> or <c>total</c>), otherwise null.</summary>
    public long? TotalCount { get; }
    /// <summary>The page size the list reports (<c>pageSize</c> at the envelope root or in <c>data</c>), otherwise null.</summary>
    public long? PageSize { get; }
    /// <summary>The envelope's <c>data</c>, for list-level fields such as <c>runsToday</c> of the screening history.</summary>
    public JsonElement Data { get; }
    /// <summary>The server request identifier, if present.</summary>
    public string? RequestId { get; }

    internal LunixiPage(LunixiResponse response)
    {
        var data = response.Data;
        var envelope = response.Envelope;
        Data = data; RequestId = response.RequestId;
        Items = Rows(data);
        var pageInfo = PageInfo(envelope) ?? PageInfo(data);
        NextCursor = pageInfo is { } info && (info.TryGetProperty("nextCursor", out _) || info.TryGetProperty("endCursor", out _))
            ? Text(info, "nextCursor") ?? Text(info, "endCursor")
            : Text(data, "nextCursor") ?? Text(data, "nextPageToken") ?? Text(envelope, "nextPageToken") ?? Text(envelope, "nextCursor");
        HasMore = NextCursor is not null &&
            !(pageInfo is { } paging && paging.TryGetProperty("hasMore", out var hasMore) && hasMore.ValueKind == JsonValueKind.False);
        TotalCount = Count(pageInfo, "totalCount") ?? Count(data, "totalCount") ?? Count(data, "total");
        PageSize = Count(envelope, "pageSize") ?? Count(data, "pageSize");
    }

    private static IReadOnlyList<JsonElement> Rows(JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.Array) return Objects(data);
        if (data.ValueKind != JsonValueKind.Object) return [];
        foreach (var name in new[] { "items", "operations", "rows" })
            if (data.TryGetProperty(name, out var rows) && rows.ValueKind == JsonValueKind.Array) return Objects(rows);
        foreach (var property in data.EnumerateObject())
            if (property.Value.ValueKind == JsonValueKind.Array) return Objects(property.Value);
        return [];
    }

    private static JsonElement[] Objects(JsonElement rows) => rows.EnumerateArray().Where(row => row.ValueKind == JsonValueKind.Object).ToArray();

    private static JsonElement? PageInfo(JsonElement owner)
        => owner.ValueKind == JsonValueKind.Object && owner.TryGetProperty("pageInfo", out var info) && info.ValueKind == JsonValueKind.Object ? info : null;

    private static string? Text(JsonElement owner, string name) => LunixiClient.Property(owner, name) is { Length: > 0 } text ? text : null;

    private static long? Count(JsonElement? owner, string name)
        => owner is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(name, out var total) &&
           total.ValueKind == JsonValueKind.Number && total.TryGetInt64(out var count) && Math.Abs(count) <= Validation.MaxSafeInteger ? count : null;

    /// <summary>Returns no row data.</summary>
    public override string ToString() => $"LunixiPage {{ Count = {Items.Count}, HasMore = {HasMore} }}";
}

/// <summary>An insertion-ordered route table under construction (a dictionary does not promise enumeration order).</summary>
internal sealed class OrderedRoutes : IEnumerable<KeyValuePair<string, LunixiRoute>>
{
    private readonly List<KeyValuePair<string, LunixiRoute>> rows = [];

    internal LunixiRoute this[string name]
    {
        get => rows.First(row => row.Key == name).Value;
        set
        {
            if (rows.Any(row => row.Key == name)) throw new InvalidOperationException("Duplicate route " + name + ".");
            rows.Add(new(name, value));
        }
    }

    internal IReadOnlyDictionary<string, LunixiRoute> Freeze() => new OrderedRouteTable(rows);

    public IEnumerator<KeyValuePair<string, LunixiRoute>> GetEnumerator() => rows.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>A read-only route table that enumerates in declaration order.</summary>
internal sealed class OrderedRouteTable : IReadOnlyDictionary<string, LunixiRoute>
{
    private readonly KeyValuePair<string, LunixiRoute>[] rows;
    private readonly Dictionary<string, LunixiRoute> index;

    internal OrderedRouteTable(IEnumerable<KeyValuePair<string, LunixiRoute>> rows)
    {
        this.rows = rows.ToArray();
        index = new Dictionary<string, LunixiRoute>(this.rows, StringComparer.Ordinal);
    }

    public LunixiRoute this[string key] => index[key];
    public IEnumerable<string> Keys => rows.Select(row => row.Key);
    public IEnumerable<LunixiRoute> Values => rows.Select(row => row.Value);
    public int Count => rows.Length;
    public bool ContainsKey(string key) => index.ContainsKey(key);
    public bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out LunixiRoute value) => index.TryGetValue(key, out value);
    public IEnumerator<KeyValuePair<string, LunixiRoute>> GetEnumerator() => ((IEnumerable<KeyValuePair<string, LunixiRoute>>)rows).GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
