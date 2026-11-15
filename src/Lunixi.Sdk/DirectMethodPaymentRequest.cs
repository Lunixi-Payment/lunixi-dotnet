using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lunixi;

/// <summary>
/// Body of <c>POST /api/v1/payments/direct/payment</c>: one payment with any method enabled for the merchant. Choose the
/// method with <see cref="WithMethodFields"/> (or <see cref="WithCard"/>); the provider code and the fields the provider
/// asks for are read from <see cref="PaymentsClient.ListMethodsAsync"/>. Every rule is checked before a request is sent.
/// </summary>
public sealed class DirectMethodPaymentRequest
{
    private readonly JsonObject payload = new();
    private string? paymentMethod;
    private JsonObject? block;
    private KeyValuePair<string, JsonNode>? plan;

    /// <summary>Starts a request with a positive minor-unit amount, a three-letter currency and your order reference.</summary>
    public DirectMethodPaymentRequest(string amount, string currency, string orderId)
    {
        var normalizedCurrency = currency?.ToUpperInvariant() ?? "";
        Validation.Amount(amount); RequestValidation.Currency(normalizedCurrency); RequestValidation.Text(orderId, "orderId", 256);
        payload["amount"] = amount; payload["currency"] = normalizedCurrency; payload["orderId"] = orderId;
    }

    /// <summary>
    /// Pays with a method other than card. <paramref name="fields"/> are the provider's <c>inputFields</c> values (a JSON
    /// object; null or empty when the provider asks for nothing). <paramref name="providerCode"/> is required when the
    /// method lists more than one provider (<c>providerSelection: REQUIRED</c>) and is never part of the fields. For
    /// <see cref="PaymentMethods.BankTransfer"/> pass null and the buyer's bank as <c>{ bankCode }</c>.
    /// </summary>
    public DirectMethodPaymentRequest WithMethodFields(string paymentMethod, string? providerCode, object? fields = null)
    {
        var code = PaymentMethodValidation.Normalize(paymentMethod);
        if (code == PaymentMethods.Card) throw new LunixiConfigurationException("Use WithCard to pay by card.");
        var blockName = PaymentMethodValidation.BlockName(code);
        if (!PaymentMethodFields.MethodBlocks.Contains(blockName))
            throw new LunixiConfigurationException($"paymentMethod {code} cannot be paid through direct/payment; ListMethodsAsync lists the methods that can.");
        var values = CanonicalJson.Element(fields ?? new { });
        if (values.ValueKind == JsonValueKind.Object && values.TryGetProperty("providerCode", out _))
            throw new LunixiConfigurationException($"{blockName} fields must not carry providerCode; pass it as its own argument.");
        RequestValidation.MethodBlock(values, ProviderCodeRule.Forbidden, blockName);
        if (code == PaymentMethods.PromissoryNote && values.TryGetProperty("schedule", out _))
            throw new LunixiConfigurationException("Pass the promissory-note schedule with WithPromissoryNoteSchedule, not in the fields.");
        if (code == PaymentMethods.BankTransfer && providerCode is not null)
            throw new LunixiConfigurationException("paymentMethod BANK_TRANSFER takes no providerCode: pass null and the buyer's bank as { bankCode }.");
        var methodBlock = new JsonObject();
        if (providerCode is not null) methodBlock["providerCode"] = RequestValidation.ProviderCode(providerCode);
        foreach (var property in values.EnumerateObject())
            methodBlock[property.Name] = code == PaymentMethods.PromissoryNote && property.Name == "noteCount"
                ? JsonValue.Create(PromissoryNotePlan.NoteCount(property.Value)) : JsonNode.Parse(property.Value.GetRawText());
        this.paymentMethod = code; block = methodBlock;
        return this;
    }

    /// <summary>
    /// Promissory-note plan with one note per row. Amounts add up to the payment amount; due dates are in non-decreasing
    /// order; at most 24 rows. Replaces a note count.
    /// </summary>
    public DirectMethodPaymentRequest WithPromissoryNoteSchedule(IEnumerable<PromissoryNoteScheduleItem> schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        var rows = schedule.ToArray();
        if (rows.Any(row => row is null)) throw new LunixiConfigurationException("promissoryNote.schedule rows cannot be null.");
        var element = CanonicalJson.Element(rows);
        PromissoryNotePlan.Schedule(element, null);
        plan = new KeyValuePair<string, JsonNode>("schedule", JsonNode.Parse(element.GetRawText())!);
        return this;
    }

    /// <summary>Promissory-note plan by note count (1–24, within your provider connection limits). Replaces a schedule.</summary>
    public DirectMethodPaymentRequest WithPromissoryNoteCount(int count) => NoteCount(PromissoryNotePlan.NoteCount(count));

    /// <summary>Promissory-note plan by note count given as its digit string (<c>"1"</c>–<c>"24"</c>). Replaces a schedule.</summary>
    public DirectMethodPaymentRequest WithPromissoryNoteCount(string count) => NoteCount(PromissoryNotePlan.NoteCount(count));

    /// <summary>Pays by card: a JSON object with new card fields or a stored card reference. This is the only server-side card charge.</summary>
    public DirectMethodPaymentRequest WithCard(object card)
    {
        ArgumentNullException.ThrowIfNull(card);
        var element = CanonicalJson.Element(card);
        if (element.ValueKind != JsonValueKind.Object || !element.EnumerateObject().Any())
            throw new LunixiConfigurationException("WithCard requires a non-empty card object.");
        paymentMethod = PaymentMethods.Card; block = JsonNode.Parse(element.GetRawText())!.AsObject();
        return this;
    }

    /// <summary>Card only. True charges with 3D Secure: the response carries a <c>REDIRECT</c> action.</summary>
    public DirectMethodPaymentRequest WithForce3D(bool force3D) { payload["force3D"] = force3D; return this; }

    /// <summary>Card only. <paramref name="quoteToken"/> binds the charge to an installment quote; required for two or more installments.</summary>
    public DirectMethodPaymentRequest WithInstallment(int count, string? quoteToken = null)
    {
        if (count is < 1 or > 36) throw new LunixiConfigurationException("Installment must be between one and 36.");
        if (quoteToken is not null) RequestValidation.Text(quoteToken, "installmentQuoteToken", 4096);
        payload["installment"] = count;
        if (quoteToken is not null) payload["installmentQuoteToken"] = quoteToken; else payload.Remove("installmentQuoteToken");
        return this;
    }

    /// <summary>True holds the amount (pre-authorization); capture or void it later.</summary>
    public DirectMethodPaymentRequest WithPreauth(bool preauth) { payload["preauth"] = preauth; return this; }

    /// <summary>The HTTPS URL the buyer's browser returns to after a <c>REDIRECT</c> action.</summary>
    public DirectMethodPaymentRequest WithCallbackUrl(Uri callbackUrl)
    {
        ArgumentNullException.ThrowIfNull(callbackUrl); RequestValidation.Https(callbackUrl);
        payload["callbackUrl"] = callbackUrl.AbsoluteUri; return this;
    }

    /// <summary>The buyer's language, for example <c>tr</c> or <c>en</c>.</summary>
    public DirectMethodPaymentRequest WithLocale(string locale) => Text("locale", locale, 64);
    /// <summary>An optional merchant description.</summary>
    public DirectMethodPaymentRequest WithDescription(string description) => Text("description", description, 1024);
    /// <summary>Your customer reference.</summary>
    public DirectMethodPaymentRequest WithCustomerId(string customerId) => Text("customerId", customerId, 256);
    /// <summary>The payment channel value documented by the gateway.</summary>
    public DirectMethodPaymentRequest WithPaymentChannel(string paymentChannel) => Text("paymentChannel", paymentChannel, 256);
    /// <summary>The payment group value documented by the gateway.</summary>
    public DirectMethodPaymentRequest WithPaymentGroup(string paymentGroup) => Text("paymentGroup", paymentGroup, 256);
    /// <summary>Buyer data following the public gateway DTO (a JSON object); do not log it.</summary>
    public DirectMethodPaymentRequest WithBuyer(object buyer) => Object("buyer", buyer);
    /// <summary>Billing address following the public gateway address DTO.</summary>
    public DirectMethodPaymentRequest WithBillingAddress(object billingAddress) => Object("billingAddress", billingAddress);
    /// <summary>Shipping address following the public gateway address DTO.</summary>
    public DirectMethodPaymentRequest WithShippingAddress(object shippingAddress) => Object("shippingAddress", shippingAddress);

    /// <summary>Basket lines. Required for <see cref="PaymentMethods.ShoppingLoan"/> (1–100 items).</summary>
    public DirectMethodPaymentRequest WithBasketItems(IEnumerable<BasketItem> basketItems)
    {
        ArgumentNullException.ThrowIfNull(basketItems);
        var items = basketItems.ToArray();
        if (items.Any(item => item is null)) throw new LunixiConfigurationException("Basket items cannot be null.");
        payload["basketItems"] = JsonNode.Parse(CanonicalJson.Serialize(items)); return this;
    }

    /// <summary>Optional non-sensitive merchant metadata.</summary>
    public DirectMethodPaymentRequest WithMetadata(IReadOnlyDictionary<string, object?> metadata) => Object("metadata", metadata);

    /// <summary>Returns the validated JSON body exactly as it is sent.</summary>
    public JsonElement ToPayload()
    {
        if (paymentMethod is null || block is null)
            throw new LunixiConfigurationException("Choose the payment method with WithMethodFields or WithCard.");
        var root = new JsonObject { ["paymentMethod"] = paymentMethod };
        foreach (var property in payload) root[property.Key] = property.Value?.DeepClone();
        var methodBlock = block.DeepClone().AsObject();
        if (paymentMethod == PaymentMethods.PromissoryNote && plan is { } given)
        {
            if (methodBlock.ContainsKey("noteCount"))
                throw new LunixiConfigurationException("The promissory-note plan is given twice: noteCount in the fields and a plan method. Use one.");
            methodBlock[given.Key] = given.Value.DeepClone();
        }
        else if (plan is not null) throw new LunixiConfigurationException("A promissory-note plan can only be sent with paymentMethod PROMISSORY_NOTE.");
        root[paymentMethod == PaymentMethods.Card ? "card" : PaymentMethodValidation.BlockName(paymentMethod)] = methodBlock;
        return DirectMethodPaymentValidation.Validate(CanonicalJson.Element(root));
    }

    /// <summary>Returns no card, buyer or provider input data.</summary>
    public override string ToString() => "DirectMethodPaymentRequest { RequestData = [REDACTED] }";

    private DirectMethodPaymentRequest NoteCount(string count)
    {
        plan = new KeyValuePair<string, JsonNode>("noteCount", JsonValue.Create(count));
        return this;
    }

    private DirectMethodPaymentRequest Text(string name, string value, int maxLength)
    {
        RequestValidation.Text(value, name, maxLength); payload[name] = value; return this;
    }

    private DirectMethodPaymentRequest Object(string name, object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var element = CanonicalJson.Element(value);
        if (element.ValueKind != JsonValueKind.Object) throw new LunixiConfigurationException($"{name} must be a JSON object.");
        payload[name] = JsonNode.Parse(element.GetRawText()); return this;
    }
}

/// <summary>The checks of a <c>POST /api/v1/payments/direct/payment</c> body, shared by the typed request and raw bodies.</summary>
internal static class DirectMethodPaymentValidation
{
    /// <summary>
    /// Validates a raw body and returns it as sent: root-level nulls are dropped and a numeric promissory-note
    /// <c>noteCount</c> is sent as its digit string.
    /// </summary>
    internal static JsonElement Validate(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object)
            throw new LunixiConfigurationException("A direct method payment needs a JSON object body. Use DirectMethodPaymentRequest to build it.");
        var root = JsonNode.Parse(input.GetRawText())!.AsObject();
        foreach (var key in root.Where(p => p.Value is null).Select(p => p.Key).ToArray()) root.Remove(key);
        var body = CanonicalJson.Element(root);
        if (body.TryGetProperty("price", out _) || body.TryGetProperty("paidPrice", out _))
            throw new LunixiConfigurationException("Root price and paidPrice are removed. Use the integer minor-unit amount.");
        RequestValidation.Fields(body, PaymentMethodFields.DirectMethodPayment);
        foreach (var name in new[] { "paymentMethod", "amount", "currency", "orderId" })
            if (!body.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.String && value.GetString() == "")
                throw new LunixiConfigurationException($"Direct method payments require '{name}'. Use DirectMethodPaymentRequest to build the request.");
        var method = PaymentMethodValidation.Normalize(LunixiClient.Property(body, "paymentMethod"));
        var amount = LunixiClient.Property(body, "amount") ?? throw new LunixiConfigurationException("amount must be positive integer minor units.");
        Validation.Amount(amount);
        RequestValidation.Currency(RequestValidation.RequiredText(body, "currency", 3));
        RequestValidation.RequiredText(body, "orderId", 256);
        var ownBlock = method == PaymentMethods.Card ? "card" : PaymentMethodValidation.BlockName(method);
        if (method != PaymentMethods.Card)
        {
            var cardField = PaymentMethodFields.CardOnly.FirstOrDefault(field => body.TryGetProperty(field, out _));
            if (cardField is not null) throw new LunixiConfigurationException($"{cardField} can only be sent with paymentMethod CARD.");
            if (!PaymentMethodFields.MethodBlocks.Contains(ownBlock))
                throw new LunixiConfigurationException($"paymentMethod {method} cannot be paid through direct/payment; ListMethodsAsync lists the methods that can.");
        }
        var otherBlock = PaymentMethodFields.MethodBlocks.FirstOrDefault(field => field != ownBlock && body.TryGetProperty(field, out _));
        if (otherBlock is not null) throw new LunixiConfigurationException($"paymentMethod {method} takes only the '{ownBlock}' block; '{otherBlock}' must not be sent.");
        if (!body.TryGetProperty(ownBlock, out var block) || block.ValueKind != JsonValueKind.Object)
            throw new LunixiConfigurationException($"paymentMethod {method} requires the '{ownBlock}' block.");
        if (method == PaymentMethods.Card)
        {
            if (!block.EnumerateObject().Any()) throw new LunixiConfigurationException("The card block must not be empty.");
        }
        else
        {
            if (method == PaymentMethods.BankTransfer && block.TryGetProperty("providerCode", out _))
                throw new LunixiConfigurationException("paymentMethod BANK_TRANSFER takes no providerCode in the bankTransfer block.");
            RequestValidation.MethodBlock(block, ProviderCodeRule.Optional, ownBlock, method == PaymentMethods.PromissoryNote ? ["schedule"] : null);
        }
        if (method == PaymentMethods.PromissoryNote)
        {
            PromissoryNotePlan.Assert(block, amount);
            if (block.TryGetProperty("noteCount", out var count) && count.ValueKind == JsonValueKind.Number)
                root["promissoryNote"]!["noteCount"] = JsonValue.Create(PromissoryNotePlan.NoteCount(count));
        }
        if (body.TryGetProperty("basketItems", out var basket) && basket.ValueKind != JsonValueKind.Array)
            throw new LunixiConfigurationException("basketItems must be a JSON array.");
        if (method == PaymentMethods.ShoppingLoan && (basket.ValueKind != JsonValueKind.Array || basket.GetArrayLength() is < 1 or > PaymentMethods.ShoppingLoanMaxBasketItems))
            throw new LunixiConfigurationException($"paymentMethod SHOPPING_LOAN requires 1 to {PaymentMethods.ShoppingLoanMaxBasketItems} basketItems: the bank sets the loan term from the basket.");
        foreach (var flag in new[] { "preauth", "force3D" })
            if (body.TryGetProperty(flag, out var value) && value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new LunixiConfigurationException($"{flag} must be a JSON boolean.");
        if (body.TryGetProperty("installment", out var installment) && (installment.ValueKind != JsonValueKind.Number || !installment.TryGetInt32(out var n) || n is < 1 or > 36))
            throw new LunixiConfigurationException("Installment must be between one and 36.");
        if (body.TryGetProperty("installmentQuoteToken", out _)) RequestValidation.RequiredText(body, "installmentQuoteToken", 4096);
        if (body.TryGetProperty("callbackUrl", out _))
        {
            var callback = LunixiClient.Property(body, "callbackUrl");
            if (callback is null || !Uri.TryCreate(callback, UriKind.Absolute, out var callbackUri)) throw new LunixiConfigurationException("callbackUrl must be an HTTPS URL string.");
            RequestValidation.Https(callbackUri);
        }
        foreach (var name in new[] { "buyer", "billingAddress", "shippingAddress", "metadata" })
            if (body.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Object)
                throw new LunixiConfigurationException($"{name} must be a JSON object.");
        return CanonicalJson.Element(root);
    }
}
