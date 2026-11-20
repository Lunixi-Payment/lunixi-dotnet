using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lunixi;

/// <summary>
/// A marketplace checkout intent answered 2xx without a <c>token</c> or a <c>checkoutFormContent</c> (for example a seller that
/// is not eligible under <c>ALL_OR_NOTHING</c>). Read <see cref="LunixiApiException.Code"/>; do not leave the buyer on an empty page.
/// </summary>
public sealed class MarketplaceCheckoutException : LunixiApiException
{
    internal MarketplaceCheckoutException(LunixiResponse response)
        : base("The marketplace checkout intent did not return a token or a checkout form. Inspect Code; a seller may not be eligible.",
            response.StatusCode, LunixiClient.Property(response.Envelope, "code"), response.RequestId, response.Envelope, null, null)
    { }
}

/// <summary>
/// Marketplace: split charges across sub-dealers, the sub-dealer lifecycle, commissions, payouts and the audit log.
/// <para>
/// Split charges (<c>/api/v1/payments/marketplace/*</c>) are signed per request and take an optional <c>Idempotency-Key</c>. A
/// split is aggregate per seller: group the basket by vendor and pass one <see cref="MarketplaceSeller"/> per sub-dealer; the
/// legs must add up to <c>amount</c>, or nothing is sent. Dealer, commission, payout and audit routes
/// (<c>/api/v1/marketplace/*</c>) are bearer-only. Calls are checked against <see cref="MarketplaceRoutes"/>.
/// </para>
/// <para>
/// Card data (card number, expiry, security code) is sent as given and never logged or put in an error message; a direct card
/// charge is PCI DSS SAQ-D scope, so most marketplaces use <see cref="CreateCheckoutIntentAsync"/> (hosted form). Amounts are
/// canonical minor-unit strings. Entity calls return the envelope's <c>data</c>; lists return <see cref="LunixiPage"/>.
/// </para>
/// </summary>
public sealed class MarketplaceClient
{
    private static readonly string[] RemovedAmountFields = ["price", "paidPrice"];
    private readonly RouteTableInvoker invoker;
    internal MarketplaceClient(LunixiClient client) => invoker = new RouteTableInvoker(client, MarketplaceRoutes.All);

    // Split charges

    /// <summary>
    /// Hosted-form split checkout (<c>POST /api/v1/payments/marketplace/intents</c>, signed): a <see cref="CreateIntentRequest"/>
    /// (or its JSON body) plus the seller legs. Returns the answer as it came (<c>token</c>, <c>checkoutFormContent</c>,
    /// <c>paymentId</c> …); an answer without a token or form throws <see cref="MarketplaceCheckoutException"/>. The order details
    /// are required as for <see cref="PaymentsClient.CreateIntentAsync"/> (<see cref="OrderDetails"/>; <c>buyer.ip</c> may be left
    /// out). Same route as <see cref="PaymentsClient.CreateMarketplaceIntentAsync"/>, which requires the key and leaves the policy
    /// out when null.
    /// </summary>
    public async Task<LunixiResponse> CreateCheckoutIntentAsync(object request, IReadOnlyList<MarketplaceSeller> sellers,
        string multiSellerPolicy = MarketplaceSellerPolicies.AllOrNothing, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        if (request is CreateIntentRequest intent) intent.Validate();
        var body = SplitBody(RouteTableInvoker.Changes(request, "createCheckoutIntent"), sellers, multiSellerPolicy);
        OrderDetails.Require(body, "A marketplace payment intent", checkoutIntent: true);
        var response = await invoker.SendAsync("createCheckoutIntent", null, body, null, idempotencyKey, cancellationToken).ConfigureAwait(false);
        if (!HasText(response, "token") && !HasText(response, "checkoutFormContent")) throw new MarketplaceCheckoutException(response);
        return response;
    }

    /// <summary>
    /// Direct split card charge from a raw body (<c>amount</c>, <c>currency</c>, <c>orderId</c>, <c>card</c>, <c>buyer</c>,
    /// <c>billingAddress</c>, <c>basketItems</c>, <c>sellers</c>, <c>multiSellerPolicy</c>, <c>preauth?</c> …):
    /// <c>POST /api/v1/payments/marketplace/3d</c> when <paramref name="threeDS"/> is true, <c>/2d</c> otherwise (signed). Prefer
    /// <see cref="ChargeMarketplaceCard2dAsync"/> / <see cref="ChargeMarketplaceCard3dAsync"/>, which check the body and the legs.
    /// The order details are checked before sending (<see cref="OrderDetails"/>).
    /// </summary>
    public Task<LunixiResponse> ChargeMarketplaceCardAsync(object payload, bool threeDS = false, string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        var body = RouteTableInvoker.Changes(payload, "chargeMarketplaceCard");
        ChargeAmount(body);
        OrderDetails.Require(body, "A marketplace card charge");
        return invoker.SendAsync(threeDS ? "chargeMarketplaceCard3d" : "chargeMarketplaceCard2d", null, body, null, idempotencyKey, cancellationToken);
    }

    /// <summary>Split card charge without 3-D Secure: the card payment body (<see cref="MarketplaceRoutes.ChargeRequiredFields"/>) plus the seller legs.</summary>
    public Task<LunixiResponse> ChargeMarketplaceCard2dAsync(object request, IReadOnlyList<MarketplaceSeller> sellers,
        string multiSellerPolicy = MarketplaceSellerPolicies.AllOrNothing, string? idempotencyKey = null, CancellationToken cancellationToken = default)
        => ChargeMarketplaceCardAsync(ChargeBody(request, sellers, multiSellerPolicy), false, idempotencyKey, cancellationToken);

    /// <summary>Split card charge with 3-D Secure: the card payment body (<see cref="MarketplaceRoutes.ChargeRequiredFields"/>) plus the seller legs.</summary>
    public Task<LunixiResponse> ChargeMarketplaceCard3dAsync(object request, IReadOnlyList<MarketplaceSeller> sellers,
        string multiSellerPolicy = MarketplaceSellerPolicies.AllOrNothing, string? idempotencyKey = null, CancellationToken cancellationToken = default)
        => ChargeMarketplaceCardAsync(ChargeBody(request, sellers, multiSellerPolicy), true, idempotencyKey, cancellationToken);

    // Sub-dealers

    /// <summary>
    /// Creates a sub-dealer: <c>{ legalName, taxId?, dealerType? (<see cref="MarketplaceDealerTypes"/>), categoryCode?, email?,
    /// externalRef? }</c>. The dealer then onboards itself (KYC, IBAN, contract) before it is <c>ACTIVE</c>.
    /// </summary>
    public Task<JsonElement> CreateDealerAsync(object request, CancellationToken cancellationToken = default)
    {
        var body = RouteTableInvoker.Changes(request, "createDealer");
        if (body["dealerType"] is { } type) RouteTableInvoker.OneOf(type, "dealerType", MarketplaceDealerTypes.All);
        return invoker.DataAsync("createDealer", body: body, cancellationToken: cancellationToken);
    }

    /// <summary>Creates several dealers at once (an import); each row as in <see cref="CreateDealerAsync"/>.</summary>
    public Task<JsonElement> BulkCreateDealersAsync(IReadOnlyList<object> dealers, CancellationToken cancellationToken = default)
    {
        if (dealers is null || dealers.Count == 0) throw new LunixiConfigurationException("bulkCreateDealers needs a non-empty list of dealers.");
        var rows = new JsonArray(dealers.Select((dealer, index) => (JsonNode?)RouteTableInvoker.Changes(dealer, $"dealers[{index}]")).ToArray());
        return invoker.DataAsync("bulkCreateDealers", body: new JsonObject { ["dealers"] = rows }, cancellationToken: cancellationToken);
    }

    /// <summary>Sub-dealers; filters <c>status</c>, <c>search</c>, <c>limit</c>, <c>offset</c>.</summary>
    public Task<LunixiPage> ListDealersAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => invoker.PageAsync("listDealers", null, filters, cancellationToken);

    /// <summary>One sub-dealer.</summary>
    public Task<JsonElement> GetDealerAsync(string dealerId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("getDealer", Dealer(dealerId), cancellationToken: cancellationToken);

    /// <summary>Changes a sub-dealer's status (<see cref="MarketplaceDealerStatuses"/>).</summary>
    public Task<JsonElement> UpdateDealerStatusAsync(string dealerId, string status, CancellationToken cancellationToken = default)
        => invoker.DataAsync("updateDealerStatus", Dealer(dealerId),
            new JsonObject { ["status"] = RouteTableInvoker.OneOf(status, "status", MarketplaceDealerStatuses.All) }, cancellationToken: cancellationToken);

    /// <summary>
    /// The self-onboarding link: <c>{ token, url, … }</c> (the gateway answers at the envelope root; <c>token</c> also starts the
    /// embedded onboarding SDK). <see cref="MarketplaceDealerOnboarding.Url"/> gives the URL.
    /// </summary>
    public Task<JsonElement> OnboardingTokenAsync(string dealerId, CancellationToken cancellationToken = default)
        => invoker.MergedAsync("onboardingToken", Dealer(dealerId), null, cancellationToken);

    /// <summary>The onboarding / application status.</summary>
    public Task<JsonElement> DealerApplicationAsync(string dealerId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("dealerApplication", Dealer(dealerId), cancellationToken: cancellationToken);

    /// <summary>Submits the dealer's application.</summary>
    public Task<JsonElement> SubmitDealerApplicationAsync(string dealerId, object? payload = null, CancellationToken cancellationToken = default)
        => invoker.DataAsync("submitDealerApplication", Dealer(dealerId), RouteRequest.ObjectOf(payload, "submitDealerApplication payload"),
            cancellationToken: cancellationToken);

    /// <summary>Sets the commission / terms definition.</summary>
    public Task<JsonElement> SetDealerTermsAsync(string dealerId, object payload, CancellationToken cancellationToken = default)
        => invoker.DataAsync("setDealerTerms", Dealer(dealerId), RouteTableInvoker.Changes(payload, "setDealerTerms"), cancellationToken: cancellationToken);

    /// <summary>Records the signature / contract acceptance.</summary>
    public Task<JsonElement> SignDealerContractAsync(string dealerId, object? payload = null, CancellationToken cancellationToken = default)
        => invoker.DataAsync("signDealerContract", Dealer(dealerId), RouteRequest.ObjectOf(payload, "signDealerContract payload"),
            cancellationToken: cancellationToken);

    /// <summary>Activates a dealer.</summary>
    public Task<JsonElement> ActivateDealerAsync(string dealerId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("activateDealer", Dealer(dealerId), cancellationToken: cancellationToken);

    /// <summary>Records a KYC outcome, when you run the dealer's KYC yourself.</summary>
    public Task<JsonElement> RecordDealerKycResultAsync(string dealerId, object payload, CancellationToken cancellationToken = default)
        => invoker.DataAsync("recordDealerKycResult", Dealer(dealerId), RouteTableInvoker.Changes(payload, "recordDealerKycResult"),
            cancellationToken: cancellationToken);

    // Reporting and commissions

    /// <summary>The payout ledger; filters <c>status</c>, <c>subDealerId</c>, <c>days</c>, <c>limit</c>.</summary>
    public Task<LunixiPage> PayoutLedgerAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => invoker.PageAsync("payoutLedger", null, filters, cancellationToken);

    /// <summary>Split transactions; filters <c>status</c>, <c>subDealerId</c>, <c>limit</c>.</summary>
    public Task<LunixiPage> TransactionsAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => invoker.PageAsync("transactions", null, filters, cancellationToken);

    /// <summary>Commission rules; filter <c>limit</c>.</summary>
    public Task<LunixiPage> CommissionRulesAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => invoker.PageAsync("commissionRules", null, filters, cancellationToken);

    /// <summary>Creates or replaces a commission rule.</summary>
    public Task<JsonElement> UpsertCommissionRuleAsync(object rule, CancellationToken cancellationToken = default)
        => invoker.DataAsync("upsertCommissionRule", body: RouteTableInvoker.Changes(rule, "upsertCommissionRule"), cancellationToken: cancellationToken);

    /// <summary>A dry run of a rule's effect.</summary>
    public Task<JsonElement> PreviewCommissionRuleAsync(object rule, CancellationToken cancellationToken = default)
        => invoker.DataAsync("previewCommissionRule", body: RouteTableInvoker.Changes(rule, "previewCommissionRule"), cancellationToken: cancellationToken);

    /// <summary>
    /// The effective commission of a dealer (and category) at a month-to-date volume (<paramref name="mtdVolumeMinor"/>, a
    /// minor-unit string).
    /// </summary>
    public Task<JsonElement> ResolveCommissionAsync(string subDealerId, string? categoryCode = null, string? mtdVolumeMinor = null,
        CancellationToken cancellationToken = default)
        => invoker.DataAsync("resolveCommission", query: RouteTableInvoker.Filters(
            ("subDealerId", RouteTableInvoker.RequireText(subDealerId, "subDealerId")),
            ("categoryCode", RouteTableInvoker.OptionalText(categoryCode)),
            ("mtdVolumeMinor", mtdVolumeMinor)), cancellationToken: cancellationToken);

    // Payouts

    /// <summary>A manual payout: <c>{ subDealerId, amountMinor, currency, reference?, … }</c> (<c>amountMinor</c> a positive minor-unit string).</summary>
    public Task<JsonElement> CreatePayoutAsync(object payload, CancellationToken cancellationToken = default)
    {
        var body = RouteTableInvoker.Changes(payload, "createPayout");
        if (body.ContainsKey("amountMinor")) RouteRequest.PositiveMinor(body["amountMinor"], "amountMinor");
        return invoker.DataAsync("createPayout", body: body, cancellationToken: cancellationToken);
    }

    /// <summary>Marks a payout as paid: <c>{ paidAt?, reference?, … }</c>.</summary>
    public Task<JsonElement> MarkPayoutPaidAsync(string payoutId, object? payload = null, CancellationToken cancellationToken = default)
        => invoker.DataAsync("markPayoutPaid", RouteTableInvoker.Ids(("payoutId", payoutId)), RouteRequest.ObjectOf(payload, "markPayoutPaid payload"),
            cancellationToken: cancellationToken);

    /// <summary>Marks several payouts as paid; <paramref name="extra"/> (for example <c>{ reference }</c>) is sent with the ids.</summary>
    public Task<JsonElement> BulkMarkPayoutsPaidAsync(IReadOnlyList<string> payoutIds, object? extra = null, CancellationToken cancellationToken = default)
    {
        if (payoutIds is null || payoutIds.Count == 0) throw new LunixiConfigurationException("bulkMarkPayoutsPaid needs a non-empty list of payout ids.");
        var body = RouteRequest.ObjectOf(extra, "bulkMarkPayoutsPaid extra");
        body["payoutIds"] = new JsonArray(payoutIds.Select((id, index) => (JsonNode?)RouteTableInvoker.RequireText(id, $"payoutIds[{index}]")).ToArray());
        return invoker.DataAsync("bulkMarkPayoutsPaid", body: body, cancellationToken: cancellationToken);
    }

    // Audit log (append-only, hash-chained)

    /// <summary>The audit log; filters <c>limit</c>, <c>type</c>.</summary>
    public Task<LunixiPage> AuditLogAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => invoker.PageAsync("auditLog", null, filters, cancellationToken);

    /// <summary>Verifies the integrity of the audit log's hash chain.</summary>
    public Task<JsonElement> VerifyAuditLogAsync(CancellationToken cancellationToken = default)
        => invoker.DataAsync("verifyAuditLog", cancellationToken: cancellationToken);

    private static IReadOnlyDictionary<string, string?> Dealer(string dealerId) => RouteTableInvoker.Ids(("dealerId", dealerId));

    /// <summary>The charge body of <see cref="ChargeMarketplaceCard2dAsync"/> / <c>3d</c>: the commercial context is required.</summary>
    private static JsonObject ChargeBody(object request, IReadOnlyList<MarketplaceSeller> sellers, string multiSellerPolicy)
    {
        var body = RouteTableInvoker.Changes(request, "marketplace charge");
        var missing = MarketplaceRoutes.ChargeRequiredFields.Where(field => body[field] is null || RouteRequest.Text(body[field]) == "").ToArray();
        if (missing.Length > 0) throw new LunixiConfigurationException($"A marketplace card charge requires {string.Join(", ", missing)}.");
        if (body["basketItems"] is not JsonArray { Count: > 0 })
            throw new LunixiConfigurationException("A marketplace card charge requires at least one basket item.");
        return SplitBody(body, sellers, multiSellerPolicy);
    }

    /// <summary>Adds the seller legs and the policy; the legs must add up to <c>amount</c>.</summary>
    private static JsonObject SplitBody(JsonObject body, IReadOnlyList<MarketplaceSeller> sellers, string multiSellerPolicy)
    {
        if (body.ContainsKey("sellers") || body.ContainsKey("multiSellerPolicy"))
            throw new LunixiConfigurationException("Pass the seller legs and multiSellerPolicy as arguments, not in the request body.");
        var amount = ChargeAmount(body);
        RouteTableInvoker.OneOf(multiSellerPolicy, "multiSellerPolicy", MarketplaceSellerPolicies.All);
        body["sellers"] = CardValidation.SellerLegs(sellers, amount);
        body["multiSellerPolicy"] = multiSellerPolicy;
        return body;
    }

    /// <summary><c>amount</c> (the basket total) is a positive minor-unit string; <c>price</c> / <c>paidPrice</c> are gone; <c>preauth</c> is a boolean.</summary>
    private static string ChargeAmount(JsonObject body)
    {
        var removed = RemovedAmountFields.Where(body.ContainsKey).ToArray();
        if (removed.Length > 0)
            throw new LunixiConfigurationException($"{string.Join(", ", removed)}: removed from the payment body; send the amount only in 'amount' (minor units, the basket total without the installment fee).");
        var amount = RouteRequest.PositiveMinor(body["amount"], "amount");
        if (body.ContainsKey("preauth")) RouteTableInvoker.Boolean(body, "preauth");
        return amount;
    }

    private static bool HasText(LunixiResponse response, string name)
        => LunixiClient.Property(response.Data, name) is { Length: > 0 } || LunixiClient.Property(response.Envelope, name) is { Length: > 0 };
}
