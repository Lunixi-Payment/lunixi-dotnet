using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lunixi;

/// <summary>A hosted payment intent. Amount is the basket total in integer minor units, excluding installment fees.</summary>
public sealed class CreateIntentRequest
{
    /// <summary>Positive basket total in minor units.</summary>
    public long Amount { get; }
    /// <summary>The gateway currency code.</summary>
    public string Currency { get; }
    /// <summary>Your stable order reference.</summary>
    public string OrderId { get; }
    /// <summary>An HTTPS return/callback URL owned by the merchant application.</summary>
    public Uri? CallbackUrl { get; init; }
    /// <summary>True to authorize for later capture; false or absent to charge immediately.</summary>
    public bool? Preauth { get; init; }
    /// <summary>Buyer data following the public gateway DTO; do not log it.</summary>
    public JsonElement? Buyer { get; init; }
    /// <summary>Billing address following the public gateway address DTO.</summary>
    public JsonElement? BillingAddress { get; init; }
    /// <summary>Shipping address following the public gateway address DTO.</summary>
    public JsonElement? ShippingAddress { get; init; }
    /// <summary>Basket lines. Their price sum must equal Amount.</summary>
    public IReadOnlyList<BasketItem>? BasketItems { get; init; }
    /// <summary>Your customer reference, when applicable.</summary>
    public string? CustomerId { get; init; }
    /// <summary>Optional non-sensitive merchant metadata.</summary>
    public IReadOnlyDictionary<string, object?>? Metadata { get; init; }
    /// <summary>An optional merchant description.</summary>
    public string? Description { get; init; }
    /// <summary>Creates a hosted payment request without collecting card data on the merchant server.</summary>
    public CreateIntentRequest(long amount, string currency, string orderId)
    {
        var normalizedCurrency = currency?.ToUpperInvariant() ?? "";
        Validation.Amount(amount); RequestValidation.Currency(normalizedCurrency); RequestValidation.Text(orderId, "orderId", 256);
        Amount = amount; Currency = normalizedCurrency; OrderId = orderId;
    }
    internal void Validate()
    {
        if (CallbackUrl is not null) RequestValidation.Https(CallbackUrl);
        if (CustomerId is not null) RequestValidation.Text(CustomerId, "customerId", 256);
        if (Description is not null) RequestValidation.Text(Description, "description", 1024);
        foreach (var item in new[] { Buyer, BillingAddress, ShippingAddress })
            if (item is { } value && value.ValueKind != JsonValueKind.Object)
                throw new LunixiConfigurationException("Buyer and address values must be JSON objects.");
        if (BasketItems is null) return;
        long total = 0;
        if (BasketItems.Count is < 1 or > 1000) throw new LunixiConfigurationException("BasketItems must contain between one and 1000 lines.");
        foreach (var item in BasketItems)
        {
            ArgumentNullException.ThrowIfNull(item);
            Validation.Amount(item.Price);
            if (total > Amount - item.Price) throw new LunixiConfigurationException("Basket line prices must sum exactly to Amount.");
            total += item.Price;
        }
        if (total != Amount) throw new LunixiConfigurationException("Basket line prices must sum exactly to Amount.");
    }
    /// <summary>Returns no buyer data, checkout token or merchant metadata.</summary>
    public override string ToString() => "CreateIntentRequest { RequestData = [REDACTED] }";
}

/// <summary>One basket line. Price is the complete line total in integer minor units.</summary>
public sealed class BasketItem
{
    /// <summary>The merchant's line identifier.</summary>
    public string Id { get; }
    /// <summary>The item name displayed to the buyer.</summary>
    public string Name { get; }
    /// <summary>PHYSICAL or VIRTUAL.</summary>
    public string ItemType { get; }
    /// <summary>Line total in positive integer minor units.</summary>
    public long Price { get; }
    /// <summary>The item's primary category, if used by the gateway.</summary>
    public string? Category1 { get; init; }
    /// <summary>The item's secondary category.</summary>
    public string? Category2 { get; init; }
    /// <summary>Creates one validated basket line.</summary>
    public BasketItem(string id, string name, long price, string itemType = "PHYSICAL")
    {
        RequestValidation.Text(id, "id", 256); RequestValidation.Text(name, "name", 1024); Validation.Amount(price);
        if (itemType is not ("PHYSICAL" or "VIRTUAL")) throw new LunixiConfigurationException("ItemType must be PHYSICAL or VIRTUAL.");
        Id = id; Name = name; Price = price; ItemType = itemType;
    }
}

/// <summary>Payment operations. The gateway response remains authoritative; a pending state is never a successful charge.</summary>
public sealed class PaymentsClient
{
    private const string BasePath = "/api/v1/payments";
    private readonly LunixiClient client;
    internal PaymentsClient(LunixiClient client) => this.client = client;
    /// <summary>Creates a hosted intent with one persisted idempotency key per checkout attempt.</summary>
    public Task<LunixiResponse> CreateIntentAsync(CreateIntentRequest request, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); request.Validate(); Validation.IdempotencyKey(idempotencyKey);
        return client.SendAsync(HttpMethod.Post, BasePath + "/intents", request, idempotencyKey, cancellationToken: cancellationToken);
    }
    /// <summary>Reads a payment's current status for reconciliation.</summary>
    public Task<LunixiResponse> GetAsync(string paymentId, CancellationToken cancellationToken = default)
        => client.SendAsync(HttpMethod.Get, BasePath + "/" + Validation.Id(paymentId), signed: false, cancellationToken: cancellationToken);
    /// <summary>Lists payments. Filters are restricted to the public gateway filter names.</summary>
    public Task<LunixiResponse> ListAsync(IReadOnlyDictionary<string, string>? filters = null, CancellationToken cancellationToken = default)
        => client.SendAsync(HttpMethod.Get, BasePath + RequestValidation.Query(filters, ["page", "limit", "status", "customerId", "includeLegs"]), signed: false, cancellationToken: cancellationToken);
    /// <summary>Captures an authorized payment, optionally partially. Reuse the operation key on retry.</summary>
    public Task<LunixiResponse> CaptureAsync(string paymentId, string idempotencyKey, long? amount = null, CancellationToken cancellationToken = default)
        => OperationAsync(paymentId, "capture", idempotencyKey, amount, null, cancellationToken);
    /// <summary>Refunds a payment, optionally partially. Reuse the operation key on retry.</summary>
    public Task<LunixiResponse> RefundAsync(string paymentId, string idempotencyKey, long? amount = null, string? reason = null, CancellationToken cancellationToken = default)
        => OperationAsync(paymentId, "refund", idempotencyKey, amount, reason, cancellationToken);
    /// <summary>Voids an authorization using the original payment reference.</summary>
    public Task<LunixiResponse> VoidAsync(string paymentId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Validation.IdempotencyKey(idempotencyKey);
        return client.SendAsync(HttpMethod.Post, BasePath + "/" + Validation.Id(paymentId) + "/void", idempotencyKey: idempotencyKey, cancellationToken: cancellationToken);
    }
    /// <summary>Lists currently enabled merchant payment methods and their providers.</summary>
    public Task<LunixiResponse> ListMethodsAsync(string? paymentMethod = null, CancellationToken cancellationToken = default)
    {
        if (paymentMethod is not null && (paymentMethod.Length is < 1 or > 32 || paymentMethod.Any(c => !(char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_'))))
            throw new LunixiConfigurationException("paymentMethod must be an uppercase public method code.");
        if (paymentMethod == "QR") paymentMethod = "QR_CODE";
        var query = paymentMethod is null ? "" : "?paymentMethod=" + Uri.EscapeDataString(paymentMethod);
        return client.SendAsync(HttpMethod.Get, BasePath + "/methods" + query, cancellationToken: cancellationToken);
    }
    /// <summary>Creates or continues a validated split payment, using the same persisted key throughout the flow.</summary>
    public async Task<LunixiResponse> CreateSplitAsync(SplitPaymentRequest request, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); Validation.IdempotencyKey(idempotencyKey);
        try { return await client.SendAsync(HttpMethod.Post, BasePath + "/split", request.Payload, idempotencyKey, cancellationToken: cancellationToken).ConfigureAwait(false); }
        catch (LunixiValidationException e) when (e.StatusCode == 400 && e.Response is { } body &&
            SplitPaymentRequest.InProgress.Contains(e.Code ?? "") && body.TryGetProperty("data", out var data) &&
            LunixiClient.Property(data, "splitPaymentId") is { Length: > 0 })
        { return new LunixiResponse(400, e.Response!.Value, e.RequestId); }
    }
    /// <summary>Reads a split payment and its legs. Only a verified COMPLETED, balanced result is final success.</summary>
    public Task<LunixiResponse> GetSplitAsync(string splitPaymentId, CancellationToken cancellationToken = default)
        => client.SendAsync(HttpMethod.Get, BasePath + "/split/" + Validation.Id(splitPaymentId), signed: false, cancellationToken: cancellationToken);
    /// <summary>Lists split payments with the public cursor filters.</summary>
    public Task<LunixiResponse> ListSplitsAsync(IReadOnlyDictionary<string, string>? filters = null, CancellationToken cancellationToken = default)
        => client.SendAsync(HttpMethod.Get, BasePath + "/split" + RequestValidation.Query(filters, ["status", "currency", "createdFrom", "createdTo", "orderId", "ownerRef", "limit", "after"]), signed: false, cancellationToken: cancellationToken);
    /// <summary>Refunds a split payment's parent; never refund each leg independently.</summary>
    public Task<LunixiResponse> RefundSplitAsync(string splitPaymentId, long amount, string idempotencyKey, string? reason = null, CancellationToken cancellationToken = default)
    {
        Validation.Amount(amount); Validation.IdempotencyKey(idempotencyKey);
        if (reason is not null) RequestValidation.Text(reason, "reason", 500);
        return client.SendAsync(HttpMethod.Post, BasePath + "/split/" + Validation.Id(splitPaymentId) + "/refund",
            new { amount, reason }, idempotencyKey, cancellationToken: cancellationToken);
    }
    /// <summary>Confirms a payment's OTP, consent or requested input fields; no checkout-token credentials are accepted.</summary>
    public Task<LunixiResponse> ConfirmActionAsync(string paymentId, string actionId, object input, CancellationToken cancellationToken = default)
    {
        var body = CanonicalJson.Element(input); RequestValidation.Fields(body, ["otp", "consentAccepted", "fields"]);
        if (body.TryGetProperty("otp", out _)) RequestValidation.RequiredText(body, "otp", 64);
        if (body.TryGetProperty("consentAccepted", out var consent) && consent.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new LunixiConfigurationException("consentAccepted must be a JSON boolean.");
        if (body.TryGetProperty("fields", out var fields)) RequestValidation.MethodBlock(fields, false);
        return client.SendAsync(HttpMethod.Post, BasePath + "/" + Validation.Id(paymentId) + "/actions/" + Validation.Id(actionId) + "/confirm", body, cancellationToken: cancellationToken);
    }
    /// <summary>Resends an OTP for an existing payment action; inspect the returned replacement action identifier.</summary>
    public Task<LunixiResponse> ResendActionAsync(string paymentId, string actionId, CancellationToken cancellationToken = default)
        => client.SendAsync(HttpMethod.Post, BasePath + "/" + Validation.Id(paymentId) + "/actions/" + Validation.Id(actionId) + "/resend", new { }, cancellationToken: cancellationToken);
    private Task<LunixiResponse> OperationAsync(string id, string operation, string key, long? amount, string? reason, CancellationToken token)
    {
        Validation.IdempotencyKey(key); if (amount is not null) Validation.Amount(amount.Value);
        if (reason is not null) RequestValidation.Text(reason, "reason", 500);
        return client.SendAsync(HttpMethod.Post, BasePath + "/" + Validation.Id(id) + "/" + operation,
            new { paymentIntentId = id, amount, reason }, key, cancellationToken: token);
    }
}

internal static class RequestValidation
{
    internal static void Currency(string currency)
    {
        if (currency is null || currency.Length != 3 || currency.Any(c => !char.IsAsciiLetterUpper(c)))
            throw new LunixiConfigurationException("Currency must be a three-letter uppercase ISO 4217 code. Gateway/provider availability is validated by Lunixi.");
    }
    internal static void Text(string value, string field, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength || value.Any(char.IsControl))
            throw new LunixiConfigurationException($"{field} must be non-empty bounded text without control characters.");
    }
    internal static void Https(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != "https" || uri.UserInfo != "" || uri.Fragment != "")
            throw new LunixiConfigurationException("Callback URLs must use HTTPS without user credentials or fragments.");
    }
    internal static void Fields(JsonElement body, IReadOnlyCollection<string> allowed)
    {
        if (body.ValueKind != JsonValueKind.Object) throw new LunixiConfigurationException("Request must be a JSON object.");
        if (body.EnumerateObject().Any(p => !allowed.Contains(p.Name)))
            throw new LunixiConfigurationException("Request contains an unknown or removed field.");
    }
    internal static long PositiveAmount(JsonElement body, string name)
    {
        if (!body.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var amount))
            throw new LunixiConfigurationException($"{name} must be positive integer minor units.");
        Validation.Amount(amount); return amount;
    }
    internal static string RequiredText(JsonElement body, string name, int maxLength)
    {
        var value = LunixiClient.Property(body, name);
        if (value is null) throw new LunixiConfigurationException($"{name} is required.");
        Text(value, name, maxLength); return value;
    }
    internal static string Query(IReadOnlyDictionary<string, string>? filters, IReadOnlyCollection<string> allowed)
    {
        if (filters is null || filters.Count == 0) return "";
        foreach (var pair in filters)
        {
            if (!allowed.Contains(pair.Key)) throw new LunixiConfigurationException("Unknown query filter.");
            Text(pair.Value, "filter", 1024);
            if (pair.Key is "limit" or "page" && (!int.TryParse(pair.Value, out var n) || n < 1 || pair.Key == "limit" && n > 100))
                throw new LunixiConfigurationException("Page must be positive; limit must be between one and 100.");
            if (pair.Key == "includeLegs" && pair.Value is not ("true" or "false")) throw new LunixiConfigurationException("includeLegs must be true or false.");
        }
        return "?" + string.Join('&', filters.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
    }
    internal static void MethodBlock(JsonElement block, bool providerRequired)
    {
        if (block.ValueKind != JsonValueKind.Object || block.EnumerateObject().Count() > 32)
            throw new LunixiConfigurationException("Method fields must be an object with at most 32 properties.");
        if (providerRequired) RequiredText(block, "providerCode", 64);
        foreach (var property in block.EnumerateObject())
        {
            FieldName(property.Name);
            if (property.Value.ValueKind == JsonValueKind.Object) ScalarObject(property.Value, 16);
            else if (property.Value.ValueKind == JsonValueKind.Array)
            {
                if (property.Value.GetArrayLength() > 10) throw new LunixiConfigurationException("Method groups support at most ten items.");
                foreach (var group in property.Value.EnumerateArray())
                {
                    if (group.ValueKind != JsonValueKind.Object || group.EnumerateObject().Count() > 32)
                        throw new LunixiConfigurationException("Each method group item must be a bounded object.");
                    foreach (var member in group.EnumerateObject())
                    {
                        FieldName(member.Name);
                        if (member.Value.ValueKind == JsonValueKind.Object) ScalarObject(member.Value, 16);
                        else Scalar(member.Value);
                    }
                }
            }
            else Scalar(property.Value);
        }
    }
    private static void ScalarObject(JsonElement value, int max)
    {
        if (value.EnumerateObject().Count() > max) throw new LunixiConfigurationException("Method address fields exceed the permitted count.");
        foreach (var property in value.EnumerateObject()) { FieldName(property.Name); Scalar(property.Value); }
    }
    private static void FieldName(string name)
    {
        if (name.Length is < 1 or > 128 || !char.IsAsciiLetter(name[0]) || name.Any(c => !char.IsAsciiLetterOrDigit(c)) || name is "constructor" or "prototype" or "__proto__")
            throw new LunixiConfigurationException("Method input field names must be safe alphanumeric identifiers.");
    }
    private static void Scalar(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) return;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number)) return;
        if (value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= 1024) return;
        throw new LunixiConfigurationException("Method inputs must be bounded text, finite numbers, booleans or declared address/group fields.");
    }
}
