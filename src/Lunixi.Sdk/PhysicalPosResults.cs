using System.Text.Json;

namespace Lunixi;

/// <summary>
/// The answer of a physical POS write that carries an <c>Idempotency-Key</c>. Payment writes answer 202 for a new payment
/// (accepted, not paid: follow the outcome by webhook or <see cref="PhysicalPosClient.GetPaymentAsync"/>) and 200 with
/// <see cref="Replayed"/> when the same key and body were sent before (the payment's current view). A payment cancel always
/// answers 200; management creates answer 201 new / 200 replay; end-of-day starts 202 new / 200 replay.
/// Keep <see cref="IdempotencyKey"/> while the outcome is not known.
/// </summary>
public sealed class PhysicalPosWriteResult
{
    private readonly string entityName;
    /// <summary>The response <c>data</c> object (sibling fields such as <c>closedAssignment</c> or <c>link</c> included).</summary>
    public JsonElement Data { get; }
    /// <summary>True when the same key and body were already recorded.</summary>
    public bool Replayed { get; }
    /// <summary>The HTTP status: 202 / 201 new, 200 replay or plain success.</summary>
    public int StatusCode { get; }
    /// <summary>The <c>Idempotency-Key</c> that was sent (yours, or the one the SDK generated).</summary>
    public string IdempotencyKey { get; }
    /// <summary>The server request identifier, if present.</summary>
    public string? RequestId { get; }

    internal PhysicalPosWriteResult(LunixiResponse response, string entityName, string idempotencyKey)
    {
        this.entityName = entityName;
        Data = response.Data.ValueKind == JsonValueKind.Object ? response.Data : EmptyObject;
        Replayed = Data.TryGetProperty("replayed", out var replayed) && replayed.ValueKind == JsonValueKind.True;
        StatusCode = response.StatusCode; IdempotencyKey = idempotencyKey; RequestId = response.RequestId;
    }

    /// <summary>True for 202 Accepted: a new payment, reversal or end-of-day run was queued to the device.</summary>
    public bool Accepted => StatusCode == 202;
    /// <summary>The written record (<c>payment</c>, <c>run</c>, <c>store</c>, <c>register</c>, <c>terminal</c>, <c>acquirer</c>, <c>provider</c> or <c>device</c>), or null.</summary>
    public JsonElement? Entity => Property(entityName);
    /// <summary>The payment of a payment write, or null for other writes.</summary>
    public JsonElement? Payment => Property("payment");
    /// <summary>A field of <see cref="Data"/>, or null when it is absent or JSON null.</summary>
    public JsonElement? Property(string name)
        => Data.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value : null;
    /// <summary>Returns no response data.</summary>
    public override string ToString() => $"PhysicalPosWriteResult {{ StatusCode = {StatusCode}, Replayed = {Replayed}, Data = [REDACTED] }}";

    private static readonly JsonElement EmptyObject = Empty();
    private static JsonElement Empty()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }
}

/// <summary>
/// One page of a physical POS list. The rows are <c>data.items</c>; the paging metadata is the envelope's <c>pageInfo</c>
/// (a copy inside <c>data</c> is read as a fallback). Lists are newest first and the cursor is opaque: send
/// <see cref="NextCursor"/> back as the <c>cursor</c> filter. <see cref="HasMore"/> is true only with a non-empty cursor.
/// </summary>
public sealed class PhysicalPosPage
{
    /// <summary>The rows of this page, in wire order.</summary>
    public IReadOnlyList<JsonElement> Items { get; }
    /// <summary>True when a further page exists.</summary>
    public bool HasMore { get; }
    /// <summary>The cursor of the next page, or null on the last page.</summary>
    public string? NextCursor { get; }
    /// <summary>The filtered total; only filled with <c>includeTotal: true</c> on entity lists.</summary>
    public long? TotalCount { get; }
    /// <summary>The server request identifier, if present.</summary>
    public string? RequestId { get; }

    internal PhysicalPosPage(LunixiResponse response)
    {
        var data = response.Data;
        Items = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().ToArray() : [];
        RequestId = response.RequestId;
        if ((PageInfo(response.Envelope) ?? PageInfo(data)) is not { } pageInfo) return;
        NextCursor = PhysicalPosWebhookEvent.Text(pageInfo, "nextCursor");
        HasMore = NextCursor is not null && pageInfo.TryGetProperty("hasMore", out var hasMore) && hasMore.ValueKind == JsonValueKind.True;
        if (pageInfo.TryGetProperty("totalCount", out var total) && total.ValueKind == JsonValueKind.Number &&
            total.TryGetInt64(out var count) && Math.Abs(count) <= Validation.MaxSafeInteger)
            TotalCount = count;
    }

    private static JsonElement? PageInfo(JsonElement owner)
        => owner.ValueKind == JsonValueKind.Object && owner.TryGetProperty("pageInfo", out var info) && info.ValueKind == JsonValueKind.Object ? info : null;

    /// <summary>Returns no row data.</summary>
    public override string ToString() => $"PhysicalPosPage {{ Count = {Items.Count}, HasMore = {HasMore} }}";
}

/// <summary>The original payment of a reversal event; <see cref="Origin"/> is <c>API</c> or <c>DEVICE_DIRECT</c> (the set is open).</summary>
public sealed record PhysicalPosOriginalPayment(string PaymentId, string Origin);

/// <summary>The device cashier session a payment was started from; the cashier's name is not sent.</summary>
public sealed record PhysicalPosCashier(string CashierId, string CashierCode);

/// <summary>
/// A verified physical POS webhook (<c>physical_pos.*</c>), read by <see cref="PhysicalPosClient.ParseWebhookEvent(LunixiWebhook)"/>.
/// Payment payloads (<see cref="Data"/>) carry <c>paymentId, rootPaymentId, original, operation, paymentStatus, statusReason,
/// amountMinor, approvedAmountMinor, currency, deviceId, merchantReference, historySeq</c> …; a device sale also carries
/// <c>origin</c> and <c>deviceOccurredAt</c>. Amounts are minor-unit strings, checked before this object is returned.
/// Delivery order is not guaranteed and an event can repeat: deduplicate by <see cref="Id"/> and ignore a payment event
/// whose <see cref="HistorySeq"/> is not greater than the last one you applied for that payment. End-of-day events carry
/// no <c>historySeq</c>; deduplicate them by <see cref="Id"/>.
/// </summary>
public sealed class PhysicalPosWebhookEvent
{
    /// <summary><see cref="Category"/> of <c>physical_pos.payment.*</c>.</summary>
    public const string CategoryPayment = "payment";
    /// <summary><see cref="Category"/> of <c>physical_pos.fiscal_document.*</c>.</summary>
    public const string CategoryFiscalDocument = "fiscal_document";
    /// <summary><see cref="Category"/> of <c>physical_pos.settlement.*</c> (end of day).</summary>
    public const string CategorySettlement = "settlement";
    /// <summary><see cref="Category"/> of any other <c>physical_pos.*</c> type.</summary>
    public const string CategoryOther = "other";

    /// <summary>The event id; the same transition keeps the same id on every delivery.</summary>
    public string Id { get; }
    /// <summary>The signed event type, for example <see cref="PhysicalPosEvents.PaymentApproved"/>.</summary>
    public string Type { get; }
    /// <summary><c>payment</c>, <c>fiscal_document</c>, <c>settlement</c> or <c>other</c>.</summary>
    public string Category { get; }
    /// <summary><c>LIVE</c> or <c>TEST</c>, when the envelope carries it.</summary>
    public string? Environment { get; }
    /// <summary>When the event occurred, when the envelope carries it.</summary>
    public string? CreatedAt { get; }
    /// <summary>The per-payment ordering number (<c>data.historySeq</c>), or null (end-of-day events have none).</summary>
    public long? HistorySeq { get; }
    /// <summary>A reversal's original payment, or null on a root payment.</summary>
    public PhysicalPosOriginalPayment? Original { get; }
    /// <summary>The cashier of a payment started from a device cashier session, or null (the payload then has no <c>cashier</c>).</summary>
    public PhysicalPosCashier? Cashier { get; }
    /// <summary>The event payload.</summary>
    public JsonElement Data { get; }
    /// <summary>The whole verified envelope.</summary>
    public JsonElement Envelope { get; }

    internal PhysicalPosWebhookEvent(string id, string type, JsonElement data, JsonElement envelope)
    {
        Id = id; Type = type; Category = CategoryOf(type); Data = data; Envelope = envelope;
        Environment = Text(envelope, "environment");
        CreatedAt = Text(envelope, "createdAt") ?? Text(envelope, "occurredAt") ?? Text(envelope, "timestamp");
        HistorySeq = data.TryGetProperty("historySeq", out var seq) && seq.ValueKind == JsonValueKind.Number && seq.TryGetInt64(out var number) &&
            Math.Abs(number) <= Validation.MaxSafeInteger ? number : null;
        Original = OriginalOf(data); Cashier = CashierOf(data);
    }

    /// <summary>True for <see cref="PhysicalPosEvents.PaymentDeviceSale"/>, a sale made directly on the device.</summary>
    public bool IsDeviceSale => Type == PhysicalPosEvents.PaymentDeviceSale;
    /// <summary>True for a <c>physical_pos.settlement.*</c> (end-of-day) event.</summary>
    public bool IsSettlementEvent => Category == CategorySettlement;
    /// <summary>Returns only the event type; the payload is never logged implicitly.</summary>
    public override string ToString() => $"PhysicalPosWebhookEvent {{ Type = {Type}, Data = [REDACTED] }}";

    internal static string CategoryOf(string type)
    {
        if (type.StartsWith("physical_pos.payment.", StringComparison.Ordinal)) return CategoryPayment;
        if (type.StartsWith("physical_pos.fiscal_document.", StringComparison.Ordinal)) return CategoryFiscalDocument;
        if (type.StartsWith("physical_pos.settlement.", StringComparison.Ordinal)) return CategorySettlement;
        return CategoryOther;
    }

    /// <summary><c>data.original</c> when it carries <c>paymentId</c> and <c>origin</c> as non-empty strings (other keys dropped).</summary>
    private static PhysicalPosOriginalPayment? OriginalOf(JsonElement data)
        => data.TryGetProperty("original", out var original) && Text(original, "paymentId") is { } paymentId && Text(original, "origin") is { } origin
            ? new PhysicalPosOriginalPayment(paymentId, origin) : null;

    /// <summary><c>data.cashier</c> when it carries <c>cashierId</c> and <c>cashierCode</c> as non-empty strings (other keys dropped).</summary>
    private static PhysicalPosCashier? CashierOf(JsonElement data)
        => data.TryGetProperty("cashier", out var cashier) && Text(cashier, "cashierId") is { } cashierId && Text(cashier, "cashierCode") is { } cashierCode
            ? new PhysicalPosCashier(cashierId, cashierCode) : null;

    internal static string? Text(JsonElement owner, string name) => LunixiClient.Property(owner, name) is { Length: > 0 } text ? text : null;
}
