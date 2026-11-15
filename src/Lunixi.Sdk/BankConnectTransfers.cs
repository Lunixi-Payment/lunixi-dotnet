using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Lunixi;

/// <summary>
/// The answer of a transfer or exchange-order create (HTTP 201): <c>{ transfer, stepUp, replayed }</c> or
/// <c>{ order, replayed }</c>. <see cref="IdempotencyKey"/> is the key that was sent (yours, or the UUID the SDK generated):
/// store it with your payout while the outcome is not known and call again with it to get the same record back.
/// </summary>
public sealed class BankConnectWriteResult
{
    private readonly string entityName;
    /// <summary>The response <c>data</c> object.</summary>
    public JsonElement Data { get; }
    /// <summary>True when the same key and body were already recorded (the existing record is returned).</summary>
    public bool Replayed { get; }
    /// <summary>The HTTP status: 201 for a new record, 200 or 201 for a replay.</summary>
    public int StatusCode { get; }
    /// <summary>The <c>Idempotency-Key</c> that was sent.</summary>
    public string IdempotencyKey { get; }
    /// <summary>The server request identifier, if present.</summary>
    public string? RequestId { get; }

    internal BankConnectWriteResult(LunixiResponse response, string entityName, string idempotencyKey)
    {
        this.entityName = entityName;
        Data = response.Data;
        Replayed = Data.ValueKind == JsonValueKind.Object && Data.TryGetProperty("replayed", out var replayed) && replayed.ValueKind == JsonValueKind.True;
        StatusCode = response.StatusCode; IdempotencyKey = idempotencyKey; RequestId = response.RequestId;
    }

    /// <summary>The written record: <c>transfer</c> of a transfer, <c>order</c> of an exchange order; null when absent.</summary>
    public JsonElement? Entity => Data.ValueKind == JsonValueKind.Object && Data.TryGetProperty(entityName, out var value) && value.ValueKind != JsonValueKind.Null
        ? value : null;

    /// <summary>Returns no response data.</summary>
    public override string ToString() => $"BankConnectWriteResult {{ StatusCode = {StatusCode}, Replayed = {Replayed}, Data = [REDACTED] }}";
}

/// <summary>
/// Outgoing bank transfers (<c>/api/v1/bank-connect/transfers</c>), on a connection whose <c>capabilities</c> include
/// <c>OUTGOING_TRANSFER</c>. Every route, reads included, is signed and needs <c>bank-connect:transfer</c>; the key must have an
/// IP allowlist and the request must come from a listed IP. With an API key there is no e-mail code: <see cref="CreateAsync"/>
/// answers <c>SUBMITTED</c> and the transfer is queued for the bank. Follow it with <see cref="GetAsync"/> or the
/// <c>bank_transfer.*</c> webhooks (<see cref="BankTransferEvents"/>). The bank chooses the rail and decides on the balance.
/// </summary>
public sealed class BankTransferClient
{
    private readonly LunixiClient client;
    internal BankTransferClient(LunixiClient client) => this.client = client;

    /// <summary>
    /// Payment purpose codes the bank accepts on this connection: <c>{ items: [{ code, name, order }] }</c>. Pass one as
    /// <c>paymentPurpose</c> to <see cref="CreateAsync"/>; leave it out for the bank's default.
    /// </summary>
    public async Task<JsonElement> ListPurposesAsync(string connectionId, CancellationToken cancellationToken = default)
        => (await BankTransferRequest.SendAsync(client, "listTransferPurposes", null, BankTransferRequest.ConnectionQuery(connectionId), null, null, cancellationToken)
            .ConfigureAwait(false)).Data;

    /// <summary>
    /// Creates an outgoing transfer (HTTP 201): <c>{ accountId, receiverIban, receiverName, amountMinor, currency?,
    /// paymentPurpose?, description? }</c>. <c>receiverIban</c> is a TR IBAN (at most 42 characters), <c>receiverName</c> 2–140
    /// characters, <c>amountMinor</c> a positive minor-unit string (<c>"150075"</c> = 1,500.75 TRY), <c>currency</c> defaults to
    /// TRY, <c>description</c> at most 140 characters.
    /// <para>
    /// <paramref name="idempotencyKey"/> (8–200 printable ASCII characters) is generated as a UUID when left out, and the SDK
    /// retries a network error or a 5xx with the same key. Pass your own key (for example your payout id) to retry safely
    /// across processes: the same key with the same body returns the existing transfer with <c>replayed: true</c>; with another
    /// body it is 409 <c>bank_connect.transfer.idempotency_conflict</c>. The key used is <see cref="BankConnectWriteResult.IdempotencyKey"/>.
    /// </para>
    /// </summary>
    public async Task<BankConnectWriteResult> CreateAsync(object request, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        var body = BankTransferRequest.Body("createTransfer", request);
        BankTransferRequest.CheckTransfer(body);
        var key = RouteRequest.KeyOrNew(idempotencyKey);
        var response = await BankTransferRequest.SendAsync(client, "createTransfer", null, null, body, key, cancellationToken).ConfigureAwait(false);
        return new BankConnectWriteResult(response, "transfer", key);
    }

    /// <summary>
    /// Transfers, newest first; filters <c>accountId</c>, <c>status</c> (one or a list of <see cref="BankTransferStatuses"/>),
    /// <c>createdFrom</c> (inclusive) / <c>createdTo</c> (exclusive) as a <see cref="DateTimeOffset"/> or ISO-8601 text,
    /// <c>limit</c> 1–100, <c>cursor</c>.
    /// </summary>
    public async Task<LunixiPage> ListAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => new(await BankTransferRequest.SendAsync(client, "listTransfers", null, filters, null, null, cancellationToken).ConfigureAwait(false));

    /// <summary>Every transfer of <see cref="ListAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => RouteRequest.IterateAsync(ListAsync, filters, cancellationToken);

    /// <summary>One transfer; 404 <c>bank_connect.transfer.not_found</c> for an unknown id.</summary>
    public async Task<JsonElement> GetAsync(string transferId, CancellationToken cancellationToken = default)
        => (await BankTransferRequest.SendAsync(client, "getTransfer", ("transferId", transferId), null, null, null, cancellationToken).ConfigureAwait(false)).Data;
}

/// <summary>
/// Currency exchange between two of your accounts (<c>/api/v1/bank-connect/fx</c>), on a connection whose
/// <c>capabilities</c> include <c>FX_EXCHANGE</c>. The two accounts are active accounts of the same connection in different
/// currencies; same scope and IP allowlist rule as transfers. Flow: <see cref="ListRatesAsync"/> (optional) →
/// <see cref="CreateQuoteAsync"/> → <see cref="CreateOrderAsync"/> with the quote id before <c>quote.expiresAt</c> (the bank
/// sets the validity, see <c>validForSeconds</c>). The order is <c>SUBMITTED</c>; follow it with <see cref="GetOrderAsync"/>.
/// There is no webhook for exchange orders.
/// </summary>
public sealed class BankFxClient
{
    private readonly LunixiClient client;
    internal BankFxClient(LunixiClient client) => this.client = client;

    /// <summary>Current rates on the connection: <c>{ items, refreshSeconds, fetchedAt }</c>; rates are decimal strings.</summary>
    public async Task<JsonElement> ListRatesAsync(string connectionId, CancellationToken cancellationToken = default)
        => (await BankTransferRequest.SendAsync(client, "listFxRates", null, BankTransferRequest.ConnectionQuery(connectionId), null, null, cancellationToken)
            .ConfigureAwait(false)).Data;

    /// <summary>
    /// Quotes an exchange (HTTP 201): <c>{ sourceAccountId, targetAccountId }</c> (you sell from the source and buy into the
    /// target) and exactly one of <c>sourceAmountMinor</c> / <c>targetAmountMinor</c> (a positive minor-unit string of that
    /// account's currency). Nothing is booked; a quote is used by one order.
    /// </summary>
    public async Task<JsonElement> CreateQuoteAsync(object request, CancellationToken cancellationToken = default)
    {
        var body = BankTransferRequest.Body("createFxQuote", request);
        if (body.ContainsKey("sourceAmountMinor") == body.ContainsKey("targetAmountMinor"))
            throw new LunixiConfigurationException("createFxQuote takes exactly one of sourceAmountMinor and targetAmountMinor.");
        if (RouteRequest.Text(body["sourceAccountId"]) == RouteRequest.Text(body["targetAccountId"]))
            throw new LunixiConfigurationException("createFxQuote needs two different accounts.");
        return (await BankTransferRequest.SendAsync(client, "createFxQuote", null, null, body, null, cancellationToken).ConfigureAwait(false)).Data;
    }

    /// <summary>
    /// Executes a quote (HTTP 201): <c>{ quoteId, description? }</c> (at most 140 characters); the answer is <c>{ order,
    /// replayed }</c>. <paramref name="idempotencyKey"/> follows the rule of <see cref="BankTransferClient.CreateAsync"/> and is
    /// generated when left out. 409 <c>bank_connect.fx.quote_expired</c> / <c>bank_connect.fx.quote_used</c>: ask for a new quote.
    /// </summary>
    public async Task<BankConnectWriteResult> CreateOrderAsync(object request, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        var body = BankTransferRequest.Body("createFxOrder", request);
        BankTransferRequest.CheckDescription(body);
        var key = RouteRequest.KeyOrNew(idempotencyKey);
        var response = await BankTransferRequest.SendAsync(client, "createFxOrder", null, null, body, key, cancellationToken).ConfigureAwait(false);
        return new BankConnectWriteResult(response, "order", key);
    }

    /// <summary>Exchange orders, newest first; filters <c>status</c> (one or a list of <see cref="BankFxOrderStatuses"/>), <c>limit</c>, <c>cursor</c>.</summary>
    public async Task<LunixiPage> ListOrdersAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => new(await BankTransferRequest.SendAsync(client, "listFxOrders", null, filters, null, null, cancellationToken).ConfigureAwait(false));

    /// <summary>Every order of <see cref="ListOrdersAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateOrdersAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => RouteRequest.IterateAsync(ListOrdersAsync, filters, cancellationToken);

    /// <summary>One exchange order; 404 <c>bank_connect.fx.order_not_found</c> for an unknown id.</summary>
    public async Task<JsonElement> GetOrderAsync(string orderId, CancellationToken cancellationToken = default)
        => (await BankTransferRequest.SendAsync(client, "getFxOrder", ("orderId", orderId), null, null, null, cancellationToken).ConfigureAwait(false)).Data;
}

/// <summary>Sends one route of <see cref="BankTransferRoutes"/>, signed, after checking its body and filters.</summary>
internal static class BankTransferRequest
{
    private static readonly Regex PaymentPurpose = new(BankTransferRoutes.PaymentPurposePattern, RegexOptions.CultureInvariant);
    private static readonly string[] AmountFields = ["amountMinor", "sourceAmountMinor", "targetAmountMinor"];

    internal static Task<LunixiResponse> SendAsync(LunixiClient client, string routeName, (string Name, string Value)? id,
        IReadOnlyDictionary<string, object?>? filters, JsonObject? body, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var route = BankTransferRoutes.All[routeName];
        var ids = id is { } segment ? new Dictionary<string, string?>(StringComparer.Ordinal) { [segment.Name] = segment.Value } : null;
        var path = RouteRequest.FillPath(route, ids) + RouteRequest.Query(filters, route.Query, routeName, (key, value) => FilterValue(key, value, routeName));
        return client.SendCoreAsync(route.HttpMethod, path, body, idempotencyKey, true, 200, cancellationToken);
    }

    internal static IReadOnlyDictionary<string, object?> ConnectionQuery(string connectionId)
        => new Dictionary<string, object?>(StringComparer.Ordinal) { ["connectionId"] = RouteRequest.Id(connectionId, "connectionId") };

    /// <summary>The route's fields: empty strings are left out, unknown or missing fields fail, amounts are positive minor-unit strings.</summary>
    internal static JsonObject Body(string routeName, object request)
    {
        if (request is null) throw new LunixiConfigurationException($"{routeName} body must be an object.");
        var route = BankTransferRoutes.All[routeName];
        var body = RouteRequest.ObjectOf(request, routeName + " body");
        foreach (var name in body.Where(property => RouteRequest.Text(property.Value) == "").Select(property => property.Key).ToArray()) body.Remove(name);
        RouteRequest.Fields(body, route.Required, route.Optional, [], routeName);
        foreach (var name in AmountFields.Where(body.ContainsKey)) RouteRequest.PositiveMinor(body[name], name);
        foreach (var name in route.Required.Where(name => !AmountFields.Contains(name)))
        {
            var text = RouteRequest.Text(body[name])?.Trim();
            if (string.IsNullOrEmpty(text)) throw new LunixiConfigurationException($"'{name}' must be a non-empty string.");
            body[name] = text;
        }
        return body;
    }

    internal static void CheckTransfer(JsonObject body)
    {
        if (RouteRequest.Text(body["receiverIban"])!.Length > BankTransferRoutes.IbanMaxLength)
            throw new LunixiConfigurationException($"'receiverIban' must be at most {BankTransferRoutes.IbanMaxLength} characters.");
        var nameLength = RouteRequest.CodePoints(RouteRequest.Text(body["receiverName"])!);
        if (nameLength is < BankTransferRoutes.ReceiverNameMinLength or > BankTransferRoutes.ReceiverNameMaxLength)
            throw new LunixiConfigurationException($"'receiverName' must be {BankTransferRoutes.ReceiverNameMinLength}-{BankTransferRoutes.ReceiverNameMaxLength} characters.");
        if (body.ContainsKey("currency")) body["currency"] = RouteRequest.Currency(body["currency"], "currency");
        if (body.ContainsKey("paymentPurpose") && (RouteRequest.Text(body["paymentPurpose"]) is not { } purpose || !PaymentPurpose.IsMatch(purpose)))
            throw new LunixiConfigurationException("'paymentPurpose' must be a code from ListPurposesAsync (letters, digits, underscore; at most 64).");
        CheckDescription(body);
    }

    internal static void CheckDescription(JsonObject body)
    {
        if (!body.ContainsKey("description")) return;
        if (RouteRequest.Text(body["description"]) is not { } description || RouteRequest.CodePoints(description) > BankTransferRoutes.DescriptionMaxLength)
            throw new LunixiConfigurationException($"'description' must be a string of at most {BankTransferRoutes.DescriptionMaxLength} characters.");
    }

    private static string FilterValue(string key, object value, string routeName) => key switch
    {
        "limit" => BankConnectQuery.Limit(value),
        "cursor" => BankConnectQuery.Cursor(value),
        "status" => string.Join(',', RouteRequest.Values(key, value, routeName == "listFxOrders" ? BankFxOrderStatuses.All : BankTransferStatuses.All)),
        "accountId" or "connectionId" => RouteRequest.Id(value as string, key),
        "createdFrom" or "createdTo" => RouteRequest.Time(key, value),
        _ => throw new LunixiConfigurationException($"Unknown filter '{key}'."),
    };
}
