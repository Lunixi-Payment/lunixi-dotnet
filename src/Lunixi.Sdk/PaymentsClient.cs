using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lunixi;

/// <summary>A hosted payment intent. Amount is the basket total in integer minor units, excluding installment fees.</summary>
public sealed class CreateIntentRequest
{
    /// <summary>Positive basket total in minor units.</summary>
    public string Amount { get; }
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
    /// <summary>
    /// The installment counts the hosted form may offer for this payment, for example <c>[1, 2, 3]</c>;
    /// <c>[1]</c> allows a single payment only. The form shows only the counts also allowed by your panel
    /// settings, the card and the platform maximum; null means no restriction. Counts are 1–36, distinct and
    /// include 1, otherwise <see cref="PaymentsClient.CreateIntentAsync"/> throws
    /// <see cref="LunixiConfigurationException"/> before any request. Sent as <c>enabledInstallments</c> in ascending order.
    /// </summary>
    [JsonConverter(typeof(AscendingInstallmentsConverter))]
    public IReadOnlyList<int>? EnabledInstallments { get; init; }
    /// <summary>Creates a hosted payment request without collecting card data on the merchant server.</summary>
    public CreateIntentRequest(string amount, string currency, string orderId)
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
        if (EnabledInstallments is not null) RequestValidation.EnabledInstallments(EnabledInstallments);
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
            if (total > Money.Parse(Amount) - Money.Parse(item.Price)) throw new LunixiConfigurationException("Basket line prices must sum exactly to Amount.");
            total += Money.Parse(item.Price);
        }
        if (total != Money.Parse(Amount)) throw new LunixiConfigurationException("Basket line prices must sum exactly to Amount.");
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
    public string Price { get; }
    /// <summary>The item's primary category, if used by the gateway.</summary>
    public string? Category1 { get; init; }
    /// <summary>The item's secondary category.</summary>
    public string? Category2 { get; init; }
    /// <summary>
    /// Optional Lunixi product category code, sent as <c>productCategory</c>; use a <see cref="ProductCategories"/>
    /// constant. Used by payment methods with legal installment limits (e.g. shopping loan) and other platform
    /// features. Null omits the field; an unknown code is rejected by the API.
    /// </summary>
    public string? ProductCategory { get; init; }
    /// <summary>Creates one validated basket line.</summary>
    public BasketItem(string id, string name, string price, string itemType = "PHYSICAL")
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
    private const string BinPath = "/api/v1/bin";
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
    /// <summary>Captures the remaining authorization with no amount field. Reuse the operation key on retry.</summary>
    public Task<LunixiResponse> CaptureAsync(string paymentId, string idempotencyKey, CancellationToken cancellationToken = default)
        => OperationAsync(paymentId, "capture", idempotencyKey, null, null, cancellationToken);
    /// <summary>Captures an explicit positive amount. Null is invalid.</summary>
    public Task<LunixiResponse> CaptureAsync(string paymentId, string idempotencyKey, string amount, CancellationToken cancellationToken = default)
    {
        Validation.Amount(amount);
        return OperationAsync(paymentId, "capture", idempotencyKey, amount, null, cancellationToken);
    }
    /// <summary>Refunds the remaining balance with no amount field. Reuse the operation key on retry.</summary>
    public Task<LunixiResponse> RefundAsync(string paymentId, string idempotencyKey, CancellationToken cancellationToken = default)
        => RefundRemainingAsync(paymentId, idempotencyKey, cancellationToken: cancellationToken);
    /// <summary>Refunds an explicit positive amount. Null is invalid.</summary>
    public Task<LunixiResponse> RefundAsync(string paymentId, string idempotencyKey, string amount, string? reason = null, CancellationToken cancellationToken = default)
    {
        Validation.Amount(amount);
        return OperationAsync(paymentId, "refund", idempotencyKey, amount, reason, cancellationToken);
    }
    /// <summary>Refunds the remaining balance, optionally recording a reason, with no amount field.</summary>
    public Task<LunixiResponse> RefundRemainingAsync(string paymentId, string idempotencyKey, string? reason = null, CancellationToken cancellationToken = default)
        => OperationAsync(paymentId, "refund", idempotencyKey, null, reason, cancellationToken);
    /// <summary>Voids an authorization using the original payment reference.</summary>
    public Task<LunixiResponse> VoidAsync(string paymentId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Validation.IdempotencyKey(idempotencyKey);
        return client.SendAsync(HttpMethod.Post, BasePath + "/" + Validation.Id(paymentId) + "/void", idempotencyKey: idempotencyKey, cancellationToken: cancellationToken);
    }
    /// <summary>
    /// Lists the payment methods open for the account (signed): for each method its <c>blockName</c>,
    /// <c>providerSelection</c>, <c>providerSelectionMode</c>, limits, split eligibility and the providers with the
    /// <c>inputFields</c> to send in the method block. <paramref name="paymentMethod"/> narrows the list to one method
    /// (<c>QR</c> is sent as <c>QR_CODE</c>); a code that is not an uppercase method code throws before any request.
    /// </summary>
    public Task<LunixiResponse> ListMethodsAsync(string? paymentMethod = null, CancellationToken cancellationToken = default)
    {
        var query = paymentMethod is null ? "" : "?paymentMethod=" + Uri.EscapeDataString(PaymentMethodValidation.Normalize(paymentMethod));
        return client.SendAsync(HttpMethod.Get, BasePath + "/methods" + query, cancellationToken: cancellationToken);
    }
    /// <summary>
    /// Starts a payment with any enabled method (<c>POST /api/v1/payments/direct/payment</c>, signed). The idempotency
    /// key is required; reuse it when retrying the same payment. The envelope is returned as it is: <c>data</c> holds
    /// <c>paymentId</c>, <c>status</c>, <c>paymentMethod</c>, <c>providerCode</c> and, when the buyer must act,
    /// <c>action</c> (HTTP 202). A declined payment is HTTP 400 and throws <see cref="LunixiValidationException"/>.
    /// </summary>
    public Task<LunixiResponse> DirectMethodPaymentAsync(DirectMethodPaymentRequest request, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = request.ToPayload(); Validation.IdempotencyKey(idempotencyKey);
        return client.SendAsync(HttpMethod.Post, BasePath + "/direct/payment", body, idempotencyKey, cancellationToken: cancellationToken);
    }
    /// <summary>
    /// Sends a raw <c>direct/payment</c> body after the same checks as <see cref="DirectMethodPaymentRequest"/>: known fields
    /// only, exactly the chosen method's block, card fields only with <c>CARD</c>, block structure, promissory-note plan,
    /// shopping-loan basket and no <c>providerCode</c> for <c>BANK_TRANSFER</c>.
    /// </summary>
    public Task<LunixiResponse> DirectMethodPaymentAsync(object payload, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload is DirectMethodPaymentRequest request) return DirectMethodPaymentAsync(request, idempotencyKey, cancellationToken);
        var body = DirectMethodPaymentValidation.Validate(CanonicalJson.Element(payload)); Validation.IdempotencyKey(idempotencyKey);
        return client.SendAsync(HttpMethod.Post, BasePath + "/direct/payment", body, idempotencyKey, cancellationToken: cancellationToken);
    }
    /// <summary>
    /// Reads the notes of a promissory-note payment (signed): <c>documentType</c>, <c>totalAmount</c> and per note
    /// <c>serialNumber</c>, <c>sequence</c>, <c>amount</c>, <c>dueDate</c> and <c>status</c> (<see cref="PromissoryNoteStatuses"/>).
    /// </summary>
    public Task<LunixiResponse> GetPromissoryNotesAsync(string paymentId, CancellationToken cancellationToken = default)
        => client.SendAsync(HttpMethod.Get, PromissoryNotesPath(paymentId), cancellationToken: cancellationToken);
    /// <summary>
    /// Cancels notes at the provider (signed). Null <paramref name="serialNumbers"/> cancels every open note; otherwise
    /// 1–24 serial numbers from <see cref="GetPromissoryNotesAsync"/>. The answer lists <c>cancelResults[]</c> per note
    /// (<see cref="PromissoryNoteCancelResults"/>, <see cref="PromissoryNoteCancelErrorCodes"/>).
    /// </summary>
    public Task<LunixiResponse> CancelPromissoryNotesAsync(string paymentId, IReadOnlyList<string>? serialNumbers = null, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        var path = PromissoryNotesPath(paymentId) + "/cancel";
        object? body = serialNumbers is null ? null : new { serialNumbers = PromissoryNotePlan.SerialNumbers(serialNumbers) };
        return client.SendAsync(HttpMethod.Post, path, body, idempotencyKey, cancellationToken: cancellationToken);
    }
    /// <summary>
    /// Downloads the printable notes as a PDF, one note per page (signed). <paramref name="locale"/> is
    /// <see cref="PromissoryNoteDocumentLocales.Turkish"/> or <see cref="PromissoryNoteDocumentLocales.English"/>; null uses
    /// the gateway default. The document is built from the provider's current data and is not stored. Errors are read as
    /// the JSON envelope and thrown as <see cref="LunixiApiException"/>; documents up to 16 MiB are accepted.
    /// </summary>
    public Task<LunixiBinaryResponse> DownloadPromissoryNoteDocumentAsync(string paymentId, string? locale = null, CancellationToken cancellationToken = default)
    {
        var query = locale is null ? "" : "?locale=" + Uri.EscapeDataString(PromissoryNotePlan.DocumentLocale(locale));
        return client.SendBinaryAsync(PromissoryNotesPath(paymentId) + "/document" + query, "application/pdf, application/json", cancellationToken);
    }
    /// <summary>
    /// Lists late, short and excess transfers of <c>BANK_TRANSFER</c> payments (signed). The envelope's <c>data.items</c>
    /// carries <c>id</c>, <c>paymentId</c>, <c>kind</c>, <c>status</c>, <c>environment</c>, <c>amountMinor</c> and the
    /// masked sender name.
    /// </summary>
    public Task<LunixiResponse> ListBankTransferExceptionsAsync(BankTransferExceptionFilters? filters = null, CancellationToken cancellationToken = default)
        => client.SendAsync(HttpMethod.Get, BasePath + "/bank-transfer/exceptions" + (filters?.Query() ?? ""), cancellationToken: cancellationToken);
    /// <summary>
    /// Accepts a bank transfer exception (signed). <c>LATE</c>: a new captured payment linked to the original is opened;
    /// <c>UNDERPAID</c>: the payment is captured with the amount received. <c>data</c> is the exception or null.
    /// </summary>
    public Task<LunixiResponse> AcceptBankTransferExceptionAsync(string exceptionId, CancellationToken cancellationToken = default)
        => client.SendAsync(HttpMethod.Post, BasePath + "/bank-transfer/exceptions/" + BankTransferExceptionFilters.ExceptionId(exceptionId) + "/accept", cancellationToken: cancellationToken);
    /// <summary>
    /// Reports that you refunded the money of a bank transfer exception to the sender from your own bank (signed); Lunixi
    /// does not send the refund. <c>data</c> is the exception or null.
    /// </summary>
    public Task<LunixiResponse> MarkBankTransferExceptionRefundedAsync(string exceptionId, CancellationToken cancellationToken = default)
        => client.SendAsync(HttpMethod.Post, BasePath + "/bank-transfer/exceptions/" + BankTransferExceptionFilters.ExceptionId(exceptionId) + "/refunded", cancellationToken: cancellationToken);
    /// <summary>
    /// Creates a marketplace hosted intent (<c>POST /api/v1/payments/marketplace/intents</c>, signed): the intent body plus
    /// <c>sellers[]</c> (<c>subDealerId</c>, <c>amountMinor</c>, optional <c>scenario</c>) and, when given,
    /// <c>multiSellerPolicy</c> (<see cref="MarketplaceSellerPolicies"/>). The sellers' shares must add up exactly to the
    /// intent amount; any other total throws before a request is sent. Reuse the idempotency key on retry.
    /// </summary>
    public Task<LunixiResponse> CreateMarketplaceIntentAsync(CreateIntentRequest request, IReadOnlyList<MarketplaceSeller> sellers, string idempotencyKey,
        string? multiSellerPolicy = null, CancellationToken cancellationToken = default)
    {
        var body = CardValidation.MarketplaceIntent(request, sellers, multiSellerPolicy); Validation.IdempotencyKey(idempotencyKey);
        return client.SendAsync(HttpMethod.Post, BasePath + "/marketplace/intents", body, idempotencyKey, cancellationToken: cancellationToken);
    }
    /// <summary>
    /// Reads card, bank and installment eligibility for a BIN or PAN prefix (<c>POST /api/v1/bin/info</c>). The value must
    /// be 6–19 digits; it is never placed in an error message.
    /// </summary>
    public Task<LunixiResponse> BinInfoAsync(string binOrPan, CancellationToken cancellationToken = default)
    {
        CardValidation.BinOrPan(binOrPan);
        return client.SendAsync(HttpMethod.Post, BinPath + "/info", new { binOrPan }, signed: false, cancellationToken: cancellationToken);
    }
    /// <summary>
    /// Reads the installment table for an amount (<c>POST /api/v1/bin/installments</c>). With a BIN and an installment
    /// count above one the answer carries the <c>quoteToken</c> that <see cref="DirectMethodPaymentRequest.WithInstallment"/> needs.
    /// </summary>
    public Task<LunixiResponse> InstallmentOptionsAsync(InstallmentOptionsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); request.Validate();
        return client.SendAsync(HttpMethod.Post, BinPath + "/installments", request, signed: false, cancellationToken: cancellationToken);
    }
    /// <summary>Lists the card brands, families and banks used by the BIN and installment answers (<c>GET /api/v1/bin/card-taxonomy</c>).</summary>
    public Task<LunixiResponse> CardTaxonomyAsync(CancellationToken cancellationToken = default)
        => client.SendAsync(HttpMethod.Get, BinPath + "/card-taxonomy", signed: false, cancellationToken: cancellationToken);
    /// <summary>
    /// Verifies and stores a card (<c>POST /api/v1/payments/cards</c>, signed). The idempotency key is required; reuse it when
    /// retrying the same card. The answer carries the <c>storedCardToken</c>, never the card number.
    /// </summary>
    public Task<LunixiResponse> StoreCardAsync(StoreCardRequest request, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = request.ToPayload(); Validation.IdempotencyKey(idempotencyKey);
        return client.SendAsync(HttpMethod.Post, BasePath + "/cards", body, idempotencyKey, cancellationToken: cancellationToken);
    }
    /// <summary>Lists the stored cards of one card owner (<c>GET /api/v1/payments/cards?cardUserKey=</c>).</summary>
    public Task<LunixiResponse> ListStoredCardsAsync(string cardUserKey, CancellationToken cancellationToken = default)
    {
        RequestValidation.Text(cardUserKey, "cardUserKey", 256);
        return client.SendAsync(HttpMethod.Get, BasePath + "/cards?cardUserKey=" + Uri.EscapeDataString(cardUserKey), signed: false, cancellationToken: cancellationToken);
    }
    /// <summary>Reads one stored card by its <c>storedCardToken</c>.</summary>
    public Task<LunixiResponse> GetStoredCardAsync(string storedCardToken, CancellationToken cancellationToken = default)
        => client.SendAsync(HttpMethod.Get, BasePath + "/cards/" + Validation.Id(storedCardToken), signed: false, cancellationToken: cancellationToken);
    /// <summary>Deactivates a stored card (<c>DELETE /api/v1/payments/cards/{storedCardToken}</c>, signed). The idempotency key is required.</summary>
    public Task<LunixiResponse> DeactivateStoredCardAsync(string storedCardToken, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var path = BasePath + "/cards/" + Validation.Id(storedCardToken); Validation.IdempotencyKey(idempotencyKey);
        return client.SendAsync(HttpMethod.Delete, path, idempotencyKey: idempotencyKey, cancellationToken: cancellationToken);
    }
    /// <summary>
    /// Reports payments recovered by provider failover retries (<c>GET /api/v1/payments/analytics/failover-recovery</c>).
    /// <paramref name="filters"/> are the documented report filters, for example <c>from</c>, <c>to</c> and <c>range</c>.
    /// </summary>
    public Task<LunixiResponse> FailoverRecoveryAnalyticsAsync(IReadOnlyDictionary<string, string>? filters = null, CancellationToken cancellationToken = default)
        => client.SendAsync(HttpMethod.Get, BasePath + "/analytics/failover-recovery" + RequestValidation.ReportQuery(filters), signed: false, cancellationToken: cancellationToken);
    /// <summary>Reports the performance of one provider credential (<c>GET /api/v1/payments/analytics/provider-credentials/{credentialId}</c>).</summary>
    public Task<LunixiResponse> ProviderCredentialAnalyticsAsync(string credentialId, IReadOnlyDictionary<string, string>? filters = null, CancellationToken cancellationToken = default)
    {
        var path = BasePath + "/analytics/provider-credentials/" + Validation.Id(credentialId);
        return client.SendAsync(HttpMethod.Get, path + RequestValidation.ReportQuery(filters), signed: false, cancellationToken: cancellationToken);
    }
    /// <summary>Creates or continues a validated split payment, using the same persisted key throughout the flow.</summary>
    public async Task<LunixiResponse> CreateSplitAsync(SplitPaymentRequest request, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); Validation.IdempotencyKey(idempotencyKey);
        try { return await client.SendAsync(HttpMethod.Post, BasePath + "/split", request.Payload, idempotencyKey, cancellationToken: cancellationToken).ConfigureAwait(false); }
        catch (LunixiValidationException e) when (e.StatusCode == 400 && e.Response is { } body &&
            SplitPaymentRequest.InProgress.Contains(e.Code ?? "") && body.TryGetProperty("data", out var data) &&
            LunixiClient.Property(data, "splitPaymentId") is { Length: > 0 })
        { Money.ValidateResponse(e.Response!.Value, true); return new LunixiResponse(400, e.Response!.Value, e.RequestId); }
    }
    /// <summary>Reads a split payment and its legs. Only a verified COMPLETED, balanced result is final success.</summary>
    public Task<LunixiResponse> GetSplitAsync(string splitPaymentId, CancellationToken cancellationToken = default)
        => client.SendAsync(HttpMethod.Get, BasePath + "/split/" + Validation.Id(splitPaymentId), signed: false, cancellationToken: cancellationToken);
    /// <summary>Lists split payments with the public cursor filters.</summary>
    public Task<LunixiResponse> ListSplitsAsync(IReadOnlyDictionary<string, string>? filters = null, CancellationToken cancellationToken = default)
        => client.SendAsync(HttpMethod.Get, BasePath + "/split" + RequestValidation.Query(filters, SplitPaymentFields.ListFilters), signed: false, cancellationToken: cancellationToken);
    /// <summary>Refunds a split payment's parent; never refund each leg independently.</summary>
    public Task<LunixiResponse> RefundSplitAsync(string splitPaymentId, string amount, string idempotencyKey, string? reason = null, CancellationToken cancellationToken = default)
    {
        Validation.Amount(amount); Validation.IdempotencyKey(idempotencyKey);
        if (reason is not null) RequestValidation.Text(reason, "reason", 500);
        return client.SendAsync(HttpMethod.Post, BasePath + "/split/" + Validation.Id(splitPaymentId) + "/refund",
            new { amount, reason }, idempotencyKey, cancellationToken: cancellationToken);
    }
    /// <summary>
    /// Confirms a payment action (signed) with the buyer's one-time code (<c>otp</c>, digits only), the agreement consent
    /// (<c>consentAccepted</c>) or the values an <c>INPUT</c> action asks for (<c>fields</c>). At least one is required;
    /// checkout-token credentials are never accepted.
    /// </summary>
    public Task<LunixiResponse> ConfirmActionAsync(string paymentId, string actionId, object input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var body = CanonicalJson.Element(input); RequestValidation.Fields(body, PaymentMethodFields.ActionConfirm);
        if (!body.EnumerateObject().Any()) throw new LunixiConfigurationException("ConfirmActionAsync needs otp, consentAccepted or fields.");
        if (body.TryGetProperty("otp", out var otp) && (otp.ValueKind != JsonValueKind.String || otp.GetString() is not { Length: >= 1 and <= 16 } code || !code.All(char.IsAsciiDigit)))
            throw new LunixiConfigurationException("otp must be the code the buyer entered, as a string of 1 to 16 digits.");
        if (body.TryGetProperty("consentAccepted", out var consent) && consent.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new LunixiConfigurationException("consentAccepted must be a JSON boolean.");
        if (body.TryGetProperty("fields", out var fields)) RequestValidation.MethodBlock(fields, ProviderCodeRule.Forbidden);
        return client.SendAsync(HttpMethod.Post, ActionPath(paymentId, actionId, "confirm"), body, cancellationToken: cancellationToken);
    }
    /// <summary>Resends an OTP for an existing payment action; inspect the returned replacement action identifier.</summary>
    public Task<LunixiResponse> ResendActionAsync(string paymentId, string actionId, CancellationToken cancellationToken = default)
        => client.SendAsync(HttpMethod.Post, ActionPath(paymentId, actionId, "resend"), new { }, cancellationToken: cancellationToken);
    private static string ActionPath(string paymentId, string actionId, string operation)
        => BasePath + "/" + Validation.Id(paymentId) + "/actions/" + Validation.Id(actionId) + "/" + operation;
    private static string PromissoryNotesPath(string paymentId) => BasePath + "/" + Validation.Id(paymentId) + "/promissory-notes";
    private Task<LunixiResponse> OperationAsync(string id, string operation, string key, string? amount, string? reason, CancellationToken token)
    {
        Validation.IdempotencyKey(key); if (amount is not null) Validation.Amount(amount);
        if (reason is not null) RequestValidation.Text(reason, "reason", 500);
        var body = new Dictionary<string, object> { ["paymentIntentId"] = id };
        if (amount is not null) body["amount"] = amount;
        if (reason is not null) body["reason"] = reason;
        return client.SendAsync(HttpMethod.Post, BasePath + "/" + Validation.Id(id) + "/" + operation,
            body, key, cancellationToken: token);
    }
}

/// <summary>Writes an installment-count list in ascending order without changing the request.</summary>
internal sealed class AscendingInstallmentsConverter : JsonConverter<IReadOnlyList<int>>
{
    public override IReadOnlyList<int>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => JsonSerializer.Deserialize<int[]>(ref reader, options);
    public override void Write(Utf8JsonWriter writer, IReadOnlyList<int> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var count in value.Order()) writer.WriteNumberValue(count);
        writer.WriteEndArray();
    }
}

/// <summary>Whether a method block may, must or must not carry <c>providerCode</c>.</summary>
internal enum ProviderCodeRule { Forbidden, Optional, Required }

internal static class RequestValidation
{
    internal const int MinEnabledInstallment = 1;
    internal const int MaxEnabledInstallment = 36;
    /// <summary>A non-empty list of distinct counts from 1 to 36 that contains 1. A duplicate is an error; it is not removed.</summary>
    internal static void EnabledInstallments(IReadOnlyList<int> counts)
    {
        if (counts.Count == 0)
            throw new LunixiConfigurationException("EnabledInstallments must not be empty; use null for no restriction or [1] for a single payment only.");
        var seen = new HashSet<int>();
        foreach (var count in counts)
        {
            if (count is < MinEnabledInstallment or > MaxEnabledInstallment)
                throw new LunixiConfigurationException($"EnabledInstallments values must be from {MinEnabledInstallment} to {MaxEnabledInstallment}.");
            if (!seen.Add(count))
                throw new LunixiConfigurationException($"EnabledInstallments has {count} more than once.");
        }
        if (!seen.Contains(MinEnabledInstallment))
            throw new LunixiConfigurationException("EnabledInstallments must contain 1 (single payment).");
    }
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
        if (!body.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new LunixiConfigurationException($"{name} must be positive integer minor units.");
        return Money.Parse(value.GetString()!, positive: true);
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
            if (pair.Key is "includeLegs" or "includeTotal" && pair.Value is not ("true" or "false")) throw new LunixiConfigurationException($"{pair.Key} must be true or false.");
        }
        return "?" + string.Join('&', filters.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
    }
    /// <summary>Report filters whose names the gateway owns: safe alphanumeric names and bounded text values.</summary>
    internal static string ReportQuery(IReadOnlyDictionary<string, string>? filters)
    {
        if (filters is null || filters.Count == 0) return "";
        foreach (var pair in filters)
        {
            if (pair.Key is not { Length: >= 1 and <= 64 } || !char.IsAsciiLetter(pair.Key[0]) || !pair.Key.All(char.IsAsciiLetterOrDigit))
                throw new LunixiConfigurationException("Report filter names must be alphanumeric identifiers.");
            Text(pair.Value, "filter", 1024);
        }
        return "?" + string.Join('&', filters.Select(p => p.Key + "=" + Uri.EscapeDataString(p.Value)));
    }
    /// <summary>
    /// Structure of a method block or of confirm <c>fields</c>: an object of at most 32 fields (providerCode not counted)
    /// whose values are bounded text, finite numbers, booleans, ADDRESS objects or GROUP lists. Fields named in
    /// <paramref name="skip"/> are validated by the caller (for example a promissory-note schedule).
    /// </summary>
    internal static void MethodBlock(JsonElement block, ProviderCodeRule providerCode, string path = "fields", IReadOnlyCollection<string>? skip = null)
    {
        if (block.ValueKind != JsonValueKind.Object)
            throw new LunixiConfigurationException($"{path} must be a JSON object of field values.");
        var hasProvider = block.TryGetProperty("providerCode", out var provider);
        if (hasProvider && providerCode == ProviderCodeRule.Forbidden)
            throw new LunixiConfigurationException($"{path} must not carry providerCode.");
        if (!hasProvider && providerCode == ProviderCodeRule.Required)
            throw new LunixiConfigurationException($"{path}.providerCode is required.");
        if (hasProvider) ProviderCode(provider.ValueKind == JsonValueKind.String ? provider.GetString() : null);
        if (block.EnumerateObject().Count(p => p.Name != "providerCode") > MaxMethodFields)
            throw new LunixiConfigurationException($"{path} can carry at most {MaxMethodFields} fields.");
        foreach (var property in block.EnumerateObject())
        {
            if (property.Name == "providerCode" || skip?.Contains(property.Name) == true) continue;
            FieldName(property.Name);
            if (property.Value.ValueKind == JsonValueKind.Object) ScalarObject(property.Value, 16);
            else if (property.Value.ValueKind == JsonValueKind.Array)
            {
                if (property.Value.GetArrayLength() > 10) throw new LunixiConfigurationException("Method groups support at most ten items.");
                foreach (var group in property.Value.EnumerateArray())
                {
                    if (group.ValueKind != JsonValueKind.Object || group.EnumerateObject().Count() > MaxMethodFields)
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
    internal const int MaxMethodFields = 32;
    /// <summary>A provider code from the method list: letters, digits, _ and -, 1–64 characters.</summary>
    internal static string ProviderCode(string? value)
    {
        if (value is null || value.Length is < 1 or > 64 || value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-')))
            throw new LunixiConfigurationException("providerCode must be a provider code from ListMethodsAsync (letters, digits, _ and -).");
        return value;
    }
    private static void ScalarObject(JsonElement value, int max)
    {
        if (value.EnumerateObject().Count() > max) throw new LunixiConfigurationException("Method address fields exceed the permitted count.");
        foreach (var property in value.EnumerateObject()) { FieldName(property.Name); Scalar(property.Value); }
    }
    private static void FieldName(string name)
    {
        if (name.Length is < 1 or > 64 || !char.IsAsciiLetter(name[0]) || name.Any(c => !char.IsAsciiLetterOrDigit(c)) || name is "constructor" or "prototype" or "__proto__")
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
