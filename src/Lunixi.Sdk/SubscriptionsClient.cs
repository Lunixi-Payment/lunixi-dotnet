using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lunixi;

/// <summary>
/// Subscriptions (<c>/api/v1/subscriptions/*</c>). Calls carry the access token minted from your API key (no per-request
/// signature). Creating a subscription needs an <c>Idempotency-Key</c> (reuse it when retrying the same subscription); plan,
/// customer, card, plan-change and charge-retry writes accept one.
/// <para>
/// Calls are checked against <see cref="SubscriptionRoutes"/> before anything is sent: an unknown body field or filter, a
/// missing required field or a value outside its set is a <see cref="LunixiConfigurationException"/>. Amounts are canonical
/// minor-unit strings (<c>"9900"</c> is 99.00 TRY; <c>"0"</c> is a free plan), checked in bodies, filters and answers.
/// Methods return the envelope's <c>data</c>; lists return <see cref="LunixiPage"/> (subscription, plan, customer, card and
/// invoice lists page with <c>pageToken</c>, which the <c>Iterate…Async</c> walkers send back). The related resources hang
/// off <see cref="Plans"/>, <see cref="Customers"/>, <see cref="Cards"/>, <see cref="Invoices"/>, <see cref="Portal"/>,
/// <see cref="Products"/>, <see cref="Features"/>, <see cref="Addons"/> and <see cref="Usage"/>.
/// </para>
/// </summary>
public sealed class SubscriptionsClient
{
    private readonly RouteTableInvoker invoker;

    internal SubscriptionsClient(LunixiClient client)
    {
        invoker = new RouteTableInvoker(client, SubscriptionRoutes.All);
        Plans = new SubscriptionPlansClient(invoker); Customers = new SubscriptionCustomersClient(invoker);
        Cards = new SubscriptionCardsClient(invoker); Invoices = new SubscriptionInvoicesClient(invoker);
        Portal = new SubscriptionPortalClient(invoker); Products = new SubscriptionProductsClient(invoker);
        Features = new SubscriptionFeaturesClient(invoker); Addons = new SubscriptionAddonsClient(invoker);
        Usage = new SubscriptionUsageClient(invoker);
    }

    /// <summary>Plans: the templates a subscription is created against.</summary>
    public SubscriptionPlansClient Plans { get; }
    /// <summary>Customers, upserted by your own <c>externalId</c>.</summary>
    public SubscriptionCustomersClient Customers { get; }
    /// <summary>Saved cards for automatic charges.</summary>
    public SubscriptionCardsClient Cards { get; }
    /// <summary>Invoices: billing history, void and charge retry.</summary>
    public SubscriptionInvoicesClient Invoices { get; }
    /// <summary>The customer portal: embedded SDK sessions and hosted portal links.</summary>
    public SubscriptionPortalClient Portal { get; }
    /// <summary>Catalog products (groups of plans).</summary>
    public SubscriptionProductsClient Products { get; }
    /// <summary>Entitlement features.</summary>
    public SubscriptionFeaturesClient Features { get; }
    /// <summary>Add-ons.</summary>
    public SubscriptionAddonsClient Addons { get; }
    /// <summary>Metered usage and entitlement checks.</summary>
    public SubscriptionUsageClient Usage { get; }

    /// <summary>
    /// Creates a subscription: <c>{ customerId, planId, paymentCardId?, paymentMethodId?, collectionMethod?, priceUpdatePolicy?,
    /// entitlementPolicy?, billingTimezone?, startsAt?, chargeOnCreate?, billingCycleAnchorOverride?, skipTrial?, metadata? }</c>.
    /// The <c>Idempotency-Key</c> is required (the argument or the request's own <c>idempotencyKey</c>; both given, they must
    /// agree); it is sent in the header and the body.
    /// </summary>
    public Task<JsonElement> CreateAsync(object request, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        var body = RouteTableInvoker.Changes(request, "create");
        var key = RouteTableInvoker.BodyKey(body, idempotencyKey, "create");
        if (body["collectionMethod"] is { } method) RouteTableInvoker.OneOf(method, "collectionMethod", SubscriptionCollectionMethods.All);
        RouteTableInvoker.Boolean(body, "chargeOnCreate");
        RouteTableInvoker.Boolean(body, "skipTrial");
        RouteTableInvoker.ObjectField(body, "metadata");
        return invoker.DataAsync("create", body: body, idempotencyKey: key, cancellationToken: cancellationToken);
    }

    /// <summary>One subscription.</summary>
    public Task<JsonElement> GetAsync(string subscriptionId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("get", Id(subscriptionId), cancellationToken: cancellationToken);

    /// <summary>Subscriptions; filters <c>customerId</c>, <c>status</c>, <c>pageSize</c>, <c>pageToken</c>.</summary>
    public Task<LunixiPage> ListAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => invoker.PageAsync("list", null, filters, cancellationToken);

    /// <summary>Every subscription of <see cref="ListAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => RouteRequest.IterateAsync(ListAsync, filters, cancellationToken, "pageToken");

    /// <summary>Cancels now, or at the period end with <paramref name="atPeriodEnd"/>.</summary>
    public Task<JsonElement> CancelAsync(string subscriptionId, bool atPeriodEnd = false, string? reason = null, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject { ["cancelAtPeriodEnd"] = atPeriodEnd };
        if (RouteTableInvoker.OptionalText(reason) is { } text) body["reason"] = text;
        return invoker.DataAsync("cancel", Id(subscriptionId), body, cancellationToken: cancellationToken);
    }

    /// <summary>Pauses billing; <paramref name="resumeAt"/> resumes it automatically (sent as a UTC instant).</summary>
    public Task<JsonElement> PauseAsync(string subscriptionId, DateTimeOffset? resumeAt = null, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject();
        if (resumeAt is { } time) body["resumeAt"] = RouteRequest.Instant(time);
        return invoker.DataAsync("pause", Id(subscriptionId), body, cancellationToken: cancellationToken);
    }

    /// <summary>Resumes a paused subscription.</summary>
    public Task<JsonElement> ResumeAsync(string subscriptionId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("resume", Id(subscriptionId), cancellationToken: cancellationToken);

    /// <summary>
    /// Changes the plan: <c>{ newPlanId, behavior? (<see cref="SubscriptionPlanChangeBehaviors"/>), reason? }</c>. An
    /// <c>Idempotency-Key</c> (the argument or the request's own) is sent in the header and the body.
    /// </summary>
    public Task<JsonElement> ChangePlanAsync(string subscriptionId, object request, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        var body = RouteTableInvoker.Changes(request, "changePlan");
        var key = RouteTableInvoker.BodyKey(body, idempotencyKey, "changePlan");
        if (body["behavior"] is { } behavior)
            body["behavior"] = RouteTableInvoker.OneOf(behavior, "behavior", SubscriptionPlanChangeBehaviors.All, upper: true);
        return invoker.DataAsync("changePlan", Id(subscriptionId), body, idempotencyKey: key, cancellationToken: cancellationToken);
    }

    /// <summary>The audit trail of a subscription; filters <c>limit</c>, <c>pageToken</c>.</summary>
    public Task<LunixiPage> AuditLogsAsync(string subscriptionId, IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => invoker.PageAsync("auditLogs", Id(subscriptionId), filters, cancellationToken);

    private static IReadOnlyDictionary<string, string?> Id(string subscriptionId) => RouteTableInvoker.Ids(("subscriptionId", subscriptionId));
}

/// <summary>Subscription plans (<c>/plans</c>).</summary>
public sealed class SubscriptionPlansClient
{
    private readonly RouteTableInvoker invoker;
    internal SubscriptionPlansClient(RouteTableInvoker invoker) => this.invoker = invoker;

    /// <summary>
    /// Creates a plan: <c>{ productId, code, name, price, currency, billing, description?, trialPeriodDays?, maxDunningAttempts?,
    /// dunningScheduleDays?, gracePeriodDays?, metadata? }</c>. <c>price</c> is a minor-unit string (<c>"0"</c> for a free plan);
    /// <c>billing</c> is <c>{ frequency, interval?, byMonthDay?, byWeekDay?, timezone?, lastDayOfMonthFix?, count? }</c>
    /// (<c>interval</c> defaults to 1; <see cref="SubscriptionRoutes.BillingRuleFields"/>).
    /// </summary>
    public Task<JsonElement> CreateAsync(object request, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        var body = RouteTableInvoker.Changes(request, "plans.create");
        foreach (var field in new[] { "productId", "code", "name" })
            if (body[field] is { } value) body[field] = RouteTableInvoker.RequireText(RouteRequest.Text(value), field);
        if (body["price"] is null) throw new LunixiConfigurationException("plans.create requires price.");
        if (body["currency"] is { } currency) body["currency"] = Money.SupportedCurrency(RouteRequest.Text(currency));
        if (body["billing"] is { } billing) body["billing"] = BillingRule(billing);
        foreach (var field in new[] { "trialPeriodDays", "maxDunningAttempts", "gracePeriodDays" }) RouteTableInvoker.Count(body, field, 0);
        if (body["dunningScheduleDays"] is { } days) IntegerList(days, "dunningScheduleDays", 0, long.MaxValue);
        return invoker.DataAsync("plans.create", body: body, idempotencyKey: idempotencyKey, cancellationToken: cancellationToken);
    }

    /// <summary>One plan.</summary>
    public Task<JsonElement> GetAsync(string planId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("plans.get", Id(planId), cancellationToken: cancellationToken);

    /// <summary>Plans; filters <c>productId</c>, <c>includeArchived</c>, <c>pageSize</c>, <c>pageToken</c>.</summary>
    public Task<LunixiPage> ListAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => invoker.PageAsync("plans.list", null, filters, cancellationToken);

    /// <summary>Every plan of <see cref="ListAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => RouteRequest.IterateAsync(ListAsync, filters, cancellationToken, "pageToken");

    /// <summary>Changes price, billing, metadata…; a <c>price</c> is a minor-unit string.</summary>
    public Task<JsonElement> UpdateAsync(string planId, object changes, CancellationToken cancellationToken = default)
        => invoker.DataAsync("plans.update", Id(planId), RouteTableInvoker.Changes(changes, "plans.update"), cancellationToken: cancellationToken);

    /// <summary>Archives a plan.</summary>
    public Task<JsonElement> ArchiveAsync(string planId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("plans.archive", Id(planId), cancellationToken: cancellationToken);

    /// <summary>The plan's translations.</summary>
    public Task<JsonElement> TranslationsAsync(string planId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("plans.translations", Id(planId), cancellationToken: cancellationToken);

    /// <summary>Creates or replaces the plan's translation for one locale.</summary>
    public Task<JsonElement> UpsertTranslationAsync(string planId, string locale, object translation, CancellationToken cancellationToken = default)
        => invoker.DataAsync("plans.upsertTranslation", RouteTableInvoker.Ids(("planId", planId), ("locale", locale)),
            RouteTableInvoker.Changes(translation, "plans.upsertTranslation"), cancellationToken: cancellationToken);

    private static IReadOnlyDictionary<string, string?> Id(string planId) => RouteTableInvoker.Ids(("planId", planId));

    private static JsonObject BillingRule(JsonNode value)
    {
        if (value is not JsonObject given) throw new LunixiConfigurationException("billing must be an object.");
        var rule = RouteRequest.Compact((JsonObject)given.DeepClone());
        var unknown = rule.Select(property => property.Key).Where(name => !SubscriptionRoutes.BillingRuleFields.Contains(name)).ToArray();
        if (unknown.Length > 0) throw new LunixiConfigurationException($"billing does not accept {string.Join(", ", unknown)}.");
        rule["frequency"] = RouteTableInvoker.OneOf(rule["frequency"], "billing.frequency", SubscriptionBillingFrequencies.All, upper: true);
        if (!rule.ContainsKey("interval")) rule["interval"] = 1;
        RouteTableInvoker.Count(rule, "interval", 1, "billing.interval");
        RouteTableInvoker.Count(rule, "count", 0, "billing.count");
        if (rule["byMonthDay"] is { } monthDays) IntegerList(monthDays, "billing.byMonthDay", 1, 31);
        if (rule["byWeekDay"] is { } weekDays && (weekDays is not JsonArray days || days.Any(day => RouteRequest.Text(day) is null)))
            throw new LunixiConfigurationException("billing.byWeekDay must be an array of week days (MO, TU, WE, TH, FR, SA, SU).");
        RouteTableInvoker.Boolean(rule, "lastDayOfMonthFix", "billing.lastDayOfMonthFix");
        return rule;
    }

    private static void IntegerList(JsonNode value, string field, long min, long max)
    {
        if (value is not JsonArray list || list.Any(entry => RouteTableInvoker.Integer(entry) is not { } number || number < min || number > max))
            throw new LunixiConfigurationException(max == long.MaxValue
                ? $"{field} must be an array of integers from {min}."
                : $"{field} must be an array of integers from {min} to {max}.");
    }
}

/// <summary>Subscription customers (<c>/customers</c>), upserted by your own <c>externalId</c> (one stable customer per user).</summary>
public sealed class SubscriptionCustomersClient
{
    private readonly RouteTableInvoker invoker;
    internal SubscriptionCustomersClient(RouteTableInvoker invoker) => this.invoker = invoker;

    /// <summary>
    /// Creates or updates a customer: <c>{ externalId, name, surname, email, gsmNumber, identityNumber, billingAddress,
    /// billingCity, billingCountry, billingZipCode, billingContactName?, shippingContactName?, shippingAddress?, shippingCity?,
    /// shippingCountry?, shippingZipCode? }</c>. Renewals take the buyer from this record; the gateway answers a missing or
    /// invalid field with <see cref="SubscriptionCustomerErrorCodes"/>.
    /// </summary>
    public Task<JsonElement> UpsertAsync(object request, string? idempotencyKey = null, CancellationToken cancellationToken = default)
        => invoker.DataAsync("customers.upsert", body: RouteTableInvoker.Changes(request, "customers.upsert"), idempotencyKey: idempotencyKey,
            cancellationToken: cancellationToken);

    /// <summary>One customer.</summary>
    public Task<JsonElement> GetAsync(string customerId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("customers.get", Id(customerId), cancellationToken: cancellationToken);

    /// <summary>The customer with your own <c>externalId</c>.</summary>
    public Task<JsonElement> GetByExternalIdAsync(string externalId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("customers.getByExternalId", RouteTableInvoker.Ids(("externalId", externalId)), cancellationToken: cancellationToken);

    /// <summary>Customers; filters <c>search</c>, <c>includeArchived</c>, <c>pageSize</c>, <c>pageToken</c>.</summary>
    public Task<LunixiPage> ListAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => invoker.PageAsync("customers.list", null, filters, cancellationToken);

    /// <summary>Every customer of <see cref="ListAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => RouteRequest.IterateAsync(ListAsync, filters, cancellationToken, "pageToken");

    /// <summary>The customer's credit balance.</summary>
    public Task<JsonElement> CreditBalanceAsync(string customerId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("customers.creditBalance", Id(customerId), cancellationToken: cancellationToken);

    /// <summary>Grants or deducts credit: <c>{ amountMinor, currency, reason, type, … }</c> (<c>amountMinor</c> a minor-unit string).</summary>
    public Task<JsonElement> AdjustCreditAsync(string customerId, object adjustment, CancellationToken cancellationToken = default)
        => invoker.DataAsync("customers.adjustCredit", Id(customerId), RouteTableInvoker.Changes(adjustment, "customers.adjustCredit"),
            cancellationToken: cancellationToken);

    /// <summary>Archives a customer.</summary>
    public Task<JsonElement> ArchiveAsync(string customerId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("customers.archive", Id(customerId), cancellationToken: cancellationToken);

    private static IReadOnlyDictionary<string, string?> Id(string customerId) => RouteTableInvoker.Ids(("customerId", customerId));
}

/// <summary>
/// Saved cards for automatic charges (<c>/cards</c>). A card is added from the checkout SDK's <c>paymentMethodToken</c>; a card
/// number or security code is never sent (the fields are refused, and the refusal names only the field).
/// </summary>
public sealed class SubscriptionCardsClient
{
    private readonly RouteTableInvoker invoker;
    internal SubscriptionCardsClient(RouteTableInvoker invoker) => this.invoker = invoker;

    /// <summary>
    /// Adds a card: <c>{ customerId, paymentMethodToken, brand?, last4?, bin?, expireMonth?, expireYear?, cardholderName?,
    /// isDefault?, nickname?, verificationMode? (<see cref="SubscriptionCardVerificationModes"/>), supportedCurrencies? }</c>.
    /// </summary>
    public Task<JsonElement> AddAsync(object request, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        var body = RouteTableInvoker.Changes(request, "cards.add");
        if (body["verificationMode"] is { } mode) RouteTableInvoker.OneOf(mode, "verificationMode", SubscriptionCardVerificationModes.All);
        RouteTableInvoker.Count(body, "expireMonth", 1);
        RouteTableInvoker.Count(body, "expireYear", 1);
        RouteTableInvoker.Boolean(body, "isDefault");
        if (body["supportedCurrencies"] is { } currencies)
        {
            if (currencies is not JsonArray list) throw new LunixiConfigurationException("supportedCurrencies must be an array.");
            body["supportedCurrencies"] = new JsonArray(list.Select(entry => (JsonNode?)Money.SupportedCurrency(RouteRequest.Text(entry), "supportedCurrencies")).ToArray());
        }
        return invoker.DataAsync("cards.add", body: body, idempotencyKey: idempotencyKey, cancellationToken: cancellationToken);
    }

    /// <summary>One saved card.</summary>
    public Task<JsonElement> GetAsync(string cardId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("cards.get", Id(cardId), cancellationToken: cancellationToken);

    /// <summary>A customer's cards (<c>includeInactive</c> is always sent).</summary>
    public Task<LunixiPage> ListAsync(string customerId, bool includeInactive = false, string? pageToken = null, int? pageSize = null,
        CancellationToken cancellationToken = default)
        => invoker.PageAsync("cards.list", null, RouteTableInvoker.Filters(("customerId", RouteTableInvoker.RequireText(customerId, "customerId")),
            ("includeInactive", includeInactive), ("pageSize", pageSize), ("pageToken", pageToken)), cancellationToken);

    /// <summary>Makes the card the customer's default.</summary>
    public Task<JsonElement> SetDefaultAsync(string cardId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("cards.setDefault", Id(cardId), cancellationToken: cancellationToken);

    /// <summary>Deactivates a card.</summary>
    public Task<JsonElement> DeactivateAsync(string cardId, string? reason = null, CancellationToken cancellationToken = default)
        => invoker.DataAsync("cards.deactivate", Id(cardId), Reason(reason), cancellationToken: cancellationToken);

    /// <summary>Verifies a saved card (a small authorisation or a 3-D check); <paramref name="options"/> as the gateway documents them.</summary>
    public Task<JsonElement> VerifyAsync(string cardId, object? options = null, CancellationToken cancellationToken = default)
        => invoker.DataAsync("cards.verify", Id(cardId), options, cancellationToken: cancellationToken);

    /// <summary>Changes card metadata (label, expiry, billing…); never a card number.</summary>
    public Task<JsonElement> UpdateAsync(string cardId, object changes, CancellationToken cancellationToken = default)
        => invoker.DataAsync("cards.update", Id(cardId), RouteTableInvoker.Changes(changes, "cards.update"), cancellationToken: cancellationToken);

    private static IReadOnlyDictionary<string, string?> Id(string cardId) => RouteTableInvoker.Ids(("cardId", cardId));

    internal static JsonObject Reason(string? reason)
        => RouteTableInvoker.OptionalText(reason) is { } text ? new JsonObject { ["reason"] = text } : new JsonObject();
}

/// <summary>Subscription invoices (<c>/invoices</c>): billing history, void and charge retry.</summary>
public sealed class SubscriptionInvoicesClient
{
    private readonly RouteTableInvoker invoker;
    internal SubscriptionInvoicesClient(RouteTableInvoker invoker) => this.invoker = invoker;

    /// <summary>Invoices; filters <c>subscriptionId</c>, <c>status</c> (<see cref="SubscriptionInvoiceStatuses"/>), <c>pageSize</c>, <c>pageToken</c>.</summary>
    public Task<LunixiPage> ListAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => invoker.PageAsync("invoices.list", null, filters, cancellationToken);

    /// <summary>Every invoice of <see cref="ListAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => RouteRequest.IterateAsync(ListAsync, filters, cancellationToken, "pageToken");

    /// <summary>One invoice.</summary>
    public Task<JsonElement> GetAsync(string invoiceId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("invoices.get", Id(invoiceId), cancellationToken: cancellationToken);

    /// <summary>Voids an open invoice.</summary>
    public Task<JsonElement> VoidAsync(string invoiceId, string? reason = null, CancellationToken cancellationToken = default)
        => invoker.DataAsync("invoices.void", Id(invoiceId), SubscriptionCardsClient.Reason(reason), cancellationToken: cancellationToken);

    /// <summary>Charges the invoice again; pass an <c>Idempotency-Key</c> to retry the retry safely.</summary>
    public Task<JsonElement> RetryChargeAsync(string invoiceId, string? idempotencyKey = null, CancellationToken cancellationToken = default)
        => invoker.DataAsync("invoices.retryCharge", Id(invoiceId), idempotencyKey: idempotencyKey, cancellationToken: cancellationToken);

    private static IReadOnlyDictionary<string, string?> Id(string invoiceId) => RouteTableInvoker.Ids(("invoiceId", invoiceId));
}

/// <summary>
/// The customer portal. Embedded: <see cref="CreateSdkSessionAsync"/> mints an origin-bound session for the browser portal SDK
/// (the origin must be allow-listed). Hosted: <see cref="CreatePortalLinkAsync"/> returns a link whose <c>token</c> opens the
/// hosted portal (<see cref="SubscriptionPortalLinks.HostedUrl(JsonElement, string)"/>); the customer verifies with a one-time
/// code. The link and settings routes live under <c>/api/panel/v1/subscriptions/portal</c>.
/// </summary>
public sealed class SubscriptionPortalClient
{
    private readonly RouteTableInvoker invoker;
    internal SubscriptionPortalClient(RouteTableInvoker invoker) => this.invoker = invoker;

    /// <summary>
    /// Mints a portal SDK session. Returns <c>{ portalToken, sessionProof, proofNonce, proofExpiresAt, … }</c> (the gateway
    /// answers at the envelope root; <c>nonce</c> / <c>expiresAt</c> are read as the proof fields); hand those four to the browser.
    /// </summary>
    public async Task<JsonElement> CreateSdkSessionAsync(string customerId, string origin, string? sdkMode = null, string? portalLinkId = null,
        CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["customerId"] = RouteTableInvoker.RequireText(customerId, "customerId"),
            ["origin"] = RouteTableInvoker.RequireText(origin, "origin"),
        };
        if (RouteTableInvoker.OptionalText(sdkMode) is { } mode) body["sdkMode"] = RouteTableInvoker.OneOf(mode, "sdkMode", SubscriptionPortalSdkModes.All);
        if (RouteTableInvoker.OptionalText(portalLinkId) is { } linkId) body["portalLinkId"] = linkId;
        var session = JsonNode.Parse((await invoker.MergedAsync("portal.createSdkSession", null, body, cancellationToken).ConfigureAwait(false)).GetRawText())!.AsObject();
        if (!session.ContainsKey("proofNonce") && session["nonce"] is { } nonce) session["proofNonce"] = nonce.DeepClone();
        if (!session.ContainsKey("proofExpiresAt") && session["expiresAt"] is { } expiresAt) session["proofExpiresAt"] = expiresAt.DeepClone();
        return CanonicalJson.Element(session);
    }

    /// <summary>
    /// Creates a hosted portal link; <paramref name="options"/>: <c>allowedPlanIds</c>, <c>defaultPlanId</c>, <c>permissions</c>,
    /// <c>sessionDurationMinutes</c>, <c>expiresAt</c>, <c>requireEmailVerification</c>, …
    /// </summary>
    public Task<JsonElement> CreatePortalLinkAsync(string customerId, object? options = null, CancellationToken cancellationToken = default)
    {
        var body = RouteRequest.ObjectOf(options, "createPortalLink options");
        body["customerId"] = RouteTableInvoker.RequireText(customerId, "customerId");
        return invoker.DataAsync("portal.createPortalLink", body: body, cancellationToken: cancellationToken);
    }

    /// <summary>Portal links; filters <c>customerId</c>, <c>status</c>, <c>limit</c>.</summary>
    public Task<LunixiPage> ListLinksAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => invoker.PageAsync("portal.listLinks", null, filters, cancellationToken);

    /// <summary>One portal link.</summary>
    public Task<JsonElement> GetLinkAsync(string linkId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("portal.getLink", Id(linkId), cancellationToken: cancellationToken);

    /// <summary>Changes a portal link.</summary>
    public Task<JsonElement> UpdateLinkAsync(string linkId, object changes, CancellationToken cancellationToken = default)
        => invoker.DataAsync("portal.updateLink", Id(linkId), RouteTableInvoker.Changes(changes, "portal.updateLink"), cancellationToken: cancellationToken);

    /// <summary>Revokes a portal link.</summary>
    public Task<JsonElement> RevokeLinkAsync(string linkId, string? reason = null, CancellationToken cancellationToken = default)
        => invoker.DataAsync("portal.revokeLink", Id(linkId), SubscriptionCardsClient.Reason(reason), cancellationToken: cancellationToken);

    /// <summary>The activity of a portal link.</summary>
    public Task<JsonElement> LinkActivityAsync(string linkId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("portal.linkActivity", Id(linkId), cancellationToken: cancellationToken);

    /// <summary>Emails a portal invite: <c>{ customerId, email?, allowedPlanIds?, … }</c>.</summary>
    public Task<JsonElement> SendInviteAsync(object invite, CancellationToken cancellationToken = default)
        => invoker.DataAsync("portal.sendInvite", body: RouteTableInvoker.Changes(invite, "portal.sendInvite"), cancellationToken: cancellationToken);

    /// <summary>Portal settings (branding, allowed origins).</summary>
    public Task<JsonElement> GetSettingsAsync(CancellationToken cancellationToken = default)
        => invoker.DataAsync("portal.getSettings", cancellationToken: cancellationToken);

    /// <summary>Replaces the portal settings.</summary>
    public Task<JsonElement> UpdateSettingsAsync(object settings, CancellationToken cancellationToken = default)
        => invoker.DataAsync("portal.updateSettings", body: RouteTableInvoker.Changes(settings, "portal.updateSettings"), cancellationToken: cancellationToken);

    private static IReadOnlyDictionary<string, string?> Id(string linkId) => RouteTableInvoker.Ids(("linkId", linkId));
}

/// <summary>Catalog products (<c>/products</c>), groups of plans.</summary>
public sealed class SubscriptionProductsClient
{
    private readonly RouteTableInvoker invoker;
    internal SubscriptionProductsClient(RouteTableInvoker invoker) => this.invoker = invoker;

    /// <summary>Creates a product.</summary>
    public Task<JsonElement> CreateAsync(object product, CancellationToken cancellationToken = default)
        => invoker.DataAsync("products.create", body: RouteTableInvoker.Changes(product, "products.create"), cancellationToken: cancellationToken);

    /// <summary>Products; filters <c>limit</c>, <c>cursor</c>, <c>active</c>.</summary>
    public Task<LunixiPage> ListAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => invoker.PageAsync("products.list", null, filters, cancellationToken);

    /// <summary>Every product of <see cref="ListAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => RouteRequest.IterateAsync(ListAsync, filters, cancellationToken);

    /// <summary>One product.</summary>
    public Task<JsonElement> GetAsync(string productId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("products.get", Id(productId), cancellationToken: cancellationToken);

    /// <summary>Changes a product.</summary>
    public Task<JsonElement> UpdateAsync(string productId, object changes, CancellationToken cancellationToken = default)
        => invoker.DataAsync("products.update", Id(productId), RouteTableInvoker.Changes(changes, "products.update"), cancellationToken: cancellationToken);

    /// <summary>Archives a product.</summary>
    public Task<JsonElement> ArchiveAsync(string productId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("products.archive", Id(productId), cancellationToken: cancellationToken);

    /// <summary>The product's translations.</summary>
    public Task<JsonElement> TranslationsAsync(string productId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("products.translations", Id(productId), cancellationToken: cancellationToken);

    /// <summary>Creates or replaces the product's translation for one locale.</summary>
    public Task<JsonElement> UpsertTranslationAsync(string productId, string locale, object translation, CancellationToken cancellationToken = default)
        => invoker.DataAsync("products.upsertTranslation", RouteTableInvoker.Ids(("productId", productId), ("locale", locale)),
            RouteTableInvoker.Changes(translation, "products.upsertTranslation"), cancellationToken: cancellationToken);

    private static IReadOnlyDictionary<string, string?> Id(string productId) => RouteTableInvoker.Ids(("productId", productId));
}

/// <summary>Entitlement features (<c>/features</c>).</summary>
public sealed class SubscriptionFeaturesClient
{
    private readonly RouteTableInvoker invoker;
    internal SubscriptionFeaturesClient(RouteTableInvoker invoker) => this.invoker = invoker;

    /// <summary>Creates a feature.</summary>
    public Task<JsonElement> CreateAsync(object feature, CancellationToken cancellationToken = default)
        => invoker.DataAsync("features.create", body: RouteTableInvoker.Changes(feature, "features.create"), cancellationToken: cancellationToken);

    /// <summary>Features; any scalar filter is passed on as given.</summary>
    public Task<LunixiPage> ListAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => invoker.PageAsync("features.list", null, filters, cancellationToken);

    /// <summary>One feature.</summary>
    public Task<JsonElement> GetAsync(string featureId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("features.get", Id(featureId), cancellationToken: cancellationToken);

    /// <summary>Changes a feature.</summary>
    public Task<JsonElement> UpdateAsync(string featureId, object changes, CancellationToken cancellationToken = default)
        => invoker.DataAsync("features.update", Id(featureId), RouteTableInvoker.Changes(changes, "features.update"), cancellationToken: cancellationToken);

    /// <summary>Deletes a feature.</summary>
    public Task<JsonElement> DeleteAsync(string featureId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("features.delete", Id(featureId), cancellationToken: cancellationToken);

    /// <summary>The feature's translations.</summary>
    public Task<JsonElement> TranslationsAsync(string featureId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("features.translations", Id(featureId), cancellationToken: cancellationToken);

    /// <summary>Creates or replaces the feature's translation for one locale.</summary>
    public Task<JsonElement> UpsertTranslationAsync(string featureId, string locale, object translation, CancellationToken cancellationToken = default)
        => invoker.DataAsync("features.upsertTranslation", RouteTableInvoker.Ids(("featureId", featureId), ("locale", locale)),
            RouteTableInvoker.Changes(translation, "features.upsertTranslation"), cancellationToken: cancellationToken);

    private static IReadOnlyDictionary<string, string?> Id(string featureId) => RouteTableInvoker.Ids(("featureId", featureId));
}

/// <summary>Add-ons (<c>/addons</c>).</summary>
public sealed class SubscriptionAddonsClient
{
    private readonly RouteTableInvoker invoker;
    internal SubscriptionAddonsClient(RouteTableInvoker invoker) => this.invoker = invoker;

    /// <summary>Creates an add-on.</summary>
    public Task<JsonElement> CreateAsync(object addon, CancellationToken cancellationToken = default)
        => invoker.DataAsync("addons.create", body: RouteTableInvoker.Changes(addon, "addons.create"), cancellationToken: cancellationToken);

    /// <summary>Add-ons; any scalar filter is passed on as given.</summary>
    public Task<LunixiPage> ListAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => invoker.PageAsync("addons.list", null, filters, cancellationToken);

    /// <summary>Changes an add-on.</summary>
    public Task<JsonElement> UpdateAsync(string addonId, object changes, CancellationToken cancellationToken = default)
        => invoker.DataAsync("addons.update", Id(addonId), RouteTableInvoker.Changes(changes, "addons.update"), cancellationToken: cancellationToken);

    /// <summary>Deactivates an add-on.</summary>
    public Task<JsonElement> DeactivateAsync(string addonId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("addons.deactivate", Id(addonId), cancellationToken: cancellationToken);

    private static IReadOnlyDictionary<string, string?> Id(string addonId) => RouteTableInvoker.Ids(("addonId", addonId));
}

/// <summary>Metered usage and entitlement checks (<c>/usage</c>).</summary>
public sealed class SubscriptionUsageClient
{
    private readonly RouteTableInvoker invoker;
    internal SubscriptionUsageClient(RouteTableInvoker invoker) => this.invoker = invoker;

    /// <summary>Records a metered-usage event: <c>{ customerId | subscriptionId, featureKey, quantity, timestamp?, idempotencyKey?, … }</c>.</summary>
    public Task<JsonElement> RecordAsync(object usage, CancellationToken cancellationToken = default)
        => invoker.DataAsync("usage.record", body: RouteTableInvoker.Changes(usage, "usage.record"), cancellationToken: cancellationToken);

    /// <summary>Whether the customer is entitled / has quota left: <c>{ customerId | subscriptionId, featureKey, requested?, … }</c>.</summary>
    public Task<JsonElement> CheckEntitlementAsync(object check, CancellationToken cancellationToken = default)
        => invoker.DataAsync("usage.checkEntitlement", body: RouteTableInvoker.Changes(check, "usage.checkEntitlement"), cancellationToken: cancellationToken);
}
