using System.Text.Json;

namespace Lunixi;

/// <summary>Subscription status (<c>status</c>).</summary>
public static class SubscriptionStatuses
{
    /// <summary><c>PENDING</c>: <c>startsAt</c> is in the future.</summary>
    public const string Pending = "PENDING";
    /// <summary><c>TRIALING</c></summary>
    public const string Trialing = "TRIALING";
    /// <summary><c>ACTIVE</c></summary>
    public const string Active = "ACTIVE";
    /// <summary><c>PAST_DUE</c>: a charge failed; dunning runs.</summary>
    public const string PastDue = "PAST_DUE";
    /// <summary><c>UNPAID</c>: dunning exhausted; grace period.</summary>
    public const string Unpaid = "UNPAID";
    /// <summary><c>PAUSED</c>: paused by the merchant; no charges.</summary>
    public const string Paused = "PAUSED";
    /// <summary><c>UPGRADED</c></summary>
    public const string Upgraded = "UPGRADED";
    /// <summary><c>DOWNGRADED</c></summary>
    public const string Downgraded = "DOWNGRADED";
    /// <summary><c>CANCELED</c></summary>
    public const string Canceled = "CANCELED";
    /// <summary><c>EXPIRED</c>: the recurrence ran out.</summary>
    public const string Expired = "EXPIRED";
    /// <summary>Every subscription status.</summary>
    public static IReadOnlyList<string> All { get; } = [Pending, Trialing, Active, PastDue, Unpaid, Paused, Upgraded, Downgraded, Canceled, Expired];
    /// <summary>Live states: the subscription may be billed.</summary>
    public static IReadOnlyList<string> ActiveStates { get; } = [Trialing, Active, PastDue];
    /// <summary>Terminal states: no further transition.</summary>
    public static IReadOnlyList<string> TerminalStates { get; } = [Upgraded, Downgraded, Canceled, Expired];

    /// <summary>True while the subscription may be billed (<c>TRIALING</c>, <c>ACTIVE</c>, <c>PAST_DUE</c>).</summary>
    public static bool IsActive(string? status) => status is not null && ActiveStates.Contains(status);

    /// <summary>True while the subscription (an answer with a <c>status</c>) may be billed.</summary>
    public static bool IsActive(JsonElement subscription) => IsActive(LunixiClient.Property(subscription, "status"));

    /// <summary>True for <c>UPGRADED</c>, <c>DOWNGRADED</c>, <c>CANCELED</c>, <c>EXPIRED</c>.</summary>
    public static bool IsTerminal(string? status) => status is not null && TerminalStates.Contains(status);

    /// <summary>True when the subscription (an answer with a <c>status</c>) can no longer change.</summary>
    public static bool IsTerminal(JsonElement subscription) => IsTerminal(LunixiClient.Property(subscription, "status"));
}

/// <summary>Invoice status: <c>DRAFT</c> → <c>OPEN</c> → <c>PAID</c> | <c>UNCOLLECTIBLE</c> | <c>VOID</c>.</summary>
public static class SubscriptionInvoiceStatuses
{
    /// <summary><c>DRAFT</c></summary>
    public const string Draft = "DRAFT";
    /// <summary><c>OPEN</c></summary>
    public const string Open = "OPEN";
    /// <summary><c>PAID</c></summary>
    public const string Paid = "PAID";
    /// <summary><c>UNCOLLECTIBLE</c></summary>
    public const string Uncollectible = "UNCOLLECTIBLE";
    /// <summary><c>VOID</c></summary>
    public const string Void = "VOID";
    /// <summary>Every invoice status.</summary>
    public static IReadOnlyList<string> All { get; } = [Draft, Open, Paid, Uncollectible, Void];
}

/// <summary>How invoices are collected (<c>collectionMethod</c>).</summary>
public static class SubscriptionCollectionMethods
{
    /// <summary><c>AUTO_CHARGE</c>: the saved card is charged each cycle (default).</summary>
    public const string AutoCharge = "AUTO_CHARGE";
    /// <summary><c>SEND_INVOICE</c>: the invoice is finalised but not charged.</summary>
    public const string SendInvoice = "SEND_INVOICE";
    /// <summary><c>MANUAL</c>: collection is up to the merchant.</summary>
    public const string Manual = "MANUAL";
    /// <summary>Every collection method.</summary>
    public static IReadOnlyList<string> All { get; } = [AutoCharge, SendInvoice, Manual];
}

/// <summary><c>billing.frequency</c> of a plan.</summary>
public static class SubscriptionBillingFrequencies
{
    /// <summary><c>DAILY</c></summary>
    public const string Daily = "DAILY";
    /// <summary><c>WEEKLY</c></summary>
    public const string Weekly = "WEEKLY";
    /// <summary><c>MONTHLY</c></summary>
    public const string Monthly = "MONTHLY";
    /// <summary><c>YEARLY</c></summary>
    public const string Yearly = "YEARLY";
    /// <summary>Every billing frequency.</summary>
    public static IReadOnlyList<string> All { get; } = [Daily, Weekly, Monthly, Yearly];
}

/// <summary><c>verificationMode</c> of a saved card.</summary>
public static class SubscriptionCardVerificationModes
{
    /// <summary><c>NONE</c></summary>
    public const string None = "NONE";
    /// <summary><c>ON_ADD</c></summary>
    public const string OnAdd = "ON_ADD";
    /// <summary><c>FIRST_CHARGE</c></summary>
    public const string FirstCharge = "FIRST_CHARGE";
    /// <summary>Every verification mode.</summary>
    public static IReadOnlyList<string> All { get; } = [None, OnAdd, FirstCharge];
}

/// <summary><c>behavior</c> of a plan change: now (prorated) or at the next period.</summary>
public static class SubscriptionPlanChangeBehaviors
{
    /// <summary><c>IMMEDIATE</c></summary>
    public const string Immediate = "IMMEDIATE";
    /// <summary><c>NEXT_PERIOD</c></summary>
    public const string NextPeriod = "NEXT_PERIOD";
    /// <summary>Every plan-change behaviour.</summary>
    public static IReadOnlyList<string> All { get; } = [Immediate, NextPeriod];
}

/// <summary><c>sdkMode</c> of a portal SDK session.</summary>
public static class SubscriptionPortalSdkModes
{
    /// <summary><c>FULL</c></summary>
    public const string Full = "FULL";
    /// <summary><c>HEADLESS</c></summary>
    public const string Headless = "HEADLESS";
    /// <summary><c>HYBRID</c></summary>
    public const string Hybrid = "HYBRID";
    /// <summary>Every SDK mode.</summary>
    public static IReadOnlyList<string> All { get; } = [Full, Headless, Hybrid];
}

/// <summary>Subscription webhook event types (same signed envelope as payment events; <c>data</c> amounts are minor-unit strings).</summary>
public static class SubscriptionEvents
{
    /// <summary><c>subscription.created</c></summary>
    public const string Created = "subscription.created";
    /// <summary><c>subscription.activated</c></summary>
    public const string Activated = "subscription.activated";
    /// <summary><c>subscription.trial_will_end</c></summary>
    public const string TrialWillEnd = "subscription.trial_will_end";
    /// <summary><c>subscription.paused</c></summary>
    public const string Paused = "subscription.paused";
    /// <summary><c>subscription.resumed</c></summary>
    public const string Resumed = "subscription.resumed";
    /// <summary><c>subscription.cancel_scheduled</c></summary>
    public const string CancelScheduled = "subscription.cancel_scheduled";
    /// <summary><c>subscription.canceled</c></summary>
    public const string Canceled = "subscription.canceled";
    /// <summary><c>subscription.upgraded</c></summary>
    public const string Upgraded = "subscription.upgraded";
    /// <summary><c>subscription.downgraded</c></summary>
    public const string Downgraded = "subscription.downgraded";
    /// <summary><c>subscription.plan_change_scheduled</c></summary>
    public const string PlanChangeScheduled = "subscription.plan_change_scheduled";
    /// <summary><c>subscription.pending_change_applied</c></summary>
    public const string PendingChangeApplied = "subscription.pending_change_applied";
    /// <summary><c>subscription.payment_card_swapped</c></summary>
    public const string PaymentCardSwapped = "subscription.payment_card_swapped";
    /// <summary><c>subscription.payment_method_lost</c></summary>
    public const string PaymentMethodLost = "subscription.payment_method_lost";
    /// <summary><c>invoice.created</c></summary>
    public const string InvoiceCreated = "invoice.created";
    /// <summary><c>invoice.paid</c></summary>
    public const string InvoicePaid = "invoice.paid";
    /// <summary><c>invoice.payment_failed</c></summary>
    public const string InvoicePaymentFailed = "invoice.payment_failed";
    /// <summary><c>invoice.voided</c></summary>
    public const string InvoiceVoided = "invoice.voided";
    /// <summary><c>card.added</c></summary>
    public const string CardAdded = "card.added";
    /// <summary><c>card.set_default</c></summary>
    public const string CardSetDefault = "card.set_default";
    /// <summary><c>card.updated</c></summary>
    public const string CardUpdated = "card.updated";
    /// <summary><c>card.deactivated</c></summary>
    public const string CardDeactivated = "card.deactivated";
    /// <summary><c>card.expiring_soon</c></summary>
    public const string CardExpiringSoon = "card.expiring_soon";
    /// <summary><c>card.expired</c></summary>
    public const string CardExpired = "card.expired";
    /// <summary>Every subscription, invoice and card event type.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Created, Activated, TrialWillEnd, Paused, Resumed, CancelScheduled, Canceled, Upgraded, Downgraded, PlanChangeScheduled,
        PendingChangeApplied, PaymentCardSwapped, PaymentMethodLost, InvoiceCreated, InvoicePaid, InvoicePaymentFailed, InvoiceVoided,
        CardAdded, CardSetDefault, CardUpdated, CardDeactivated, CardExpiringSoon, CardExpired,
    ];
}

/// <summary>The hosted customer portal: a portal link's <c>token</c> opens it.</summary>
public static class SubscriptionPortalLinks
{
    /// <summary>The hosted customer portal.</summary>
    public const string HostedBase = "https://portal.lunixi.com";

    /// <summary>The hosted portal URL of a portal link token.</summary>
    public static string HostedUrl(string token, string baseUrl = HostedBase)
        => baseUrl.TrimEnd('/') + "/?token=" + Uri.EscapeDataString(token ?? "");

    /// <summary>The hosted portal URL of a <see cref="SubscriptionPortalClient.CreatePortalLinkAsync"/> answer (its <c>token</c>).</summary>
    public static string HostedUrl(JsonElement link, string baseUrl = HostedBase)
        => HostedUrl(LunixiClient.Property(link, "token") ?? "", baseUrl);
}

/// <summary>
/// The single description of every subscription route the SDK calls (<c>/api/v1/subscriptions/*</c>, and the portal-link
/// routes under <c>/api/panel/v1/subscriptions/portal</c>): method, path template, the <c>Idempotency-Key</c> rule and the body
/// and query fields. The surface is bearer-only (no route is signed). Keys are the operation names (<c>plans.create</c>); the
/// table is the Node SDK's <c>SUBSCRIPTION_ROUTES</c>, row for row, and the shared catalog's group of the same name.
/// </summary>
public static class SubscriptionRoutes
{
    private const string Subs = "/api/v1/subscriptions";
    private const string PortalPanel = "/api/panel/v1/subscriptions/portal";
    private static readonly string[] CursorQuery = ["pageSize", "pageToken"];

    /// <summary>Fields of a plan's <c>billing</c> rule.</summary>
    public static IReadOnlyList<string> BillingRuleFields { get; } = ["frequency", "interval", "byMonthDay", "byWeekDay", "timezone", "lastDayOfMonthFix", "count"];

    /// <summary>Every route by name, in the Node table's order.</summary>
    public static IReadOnlyDictionary<string, LunixiRoute> All { get; } = Build();

    private static string[] Cursor(params string[] fields) => [.. fields, .. CursorQuery];

    private static IReadOnlyDictionary<string, LunixiRoute> Build()
    {
        var routes = new OrderedRoutes
        {
            ["create"] = LunixiRoute.Post(Subs + "/subscriptions").Keyed()
                .WithRequired("customerId", "planId", "idempotencyKey")
                .WithOptional("paymentCardId", "paymentMethodId", "collectionMethod", "priceUpdatePolicy", "entitlementPolicy",
                    "billingTimezone", "startsAt", "chargeOnCreate", "billingCycleAnchorOverride", "skipTrial", "metadata"),
            ["get"] = LunixiRoute.Get(Subs + "/subscriptions/:subscriptionId"),
            ["list"] = LunixiRoute.Get(Subs + "/subscriptions").WithQuery(Cursor("customerId", "status")),
            ["cancel"] = LunixiRoute.Post(Subs + "/subscriptions/:subscriptionId/cancel").WithRequired("cancelAtPeriodEnd").WithOptional("reason"),
            ["pause"] = LunixiRoute.Post(Subs + "/subscriptions/:subscriptionId/pause").WithOptional("resumeAt").WithOptionalBody(),
            ["resume"] = LunixiRoute.Post(Subs + "/subscriptions/:subscriptionId/resume"),
            ["changePlan"] = LunixiRoute.Post(Subs + "/subscriptions/:subscriptionId/change-plan").KeyAccepted()
                .WithRequired("newPlanId").WithOptional("behavior", "reason", "idempotencyKey"),
            ["auditLogs"] = LunixiRoute.Get(Subs + "/subscriptions/:subscriptionId/audit-logs").WithQuery("limit", "pageToken"),

            ["plans.create"] = LunixiRoute.Post(Subs + "/plans").KeyAccepted()
                .WithRequired("productId", "code", "name", "price", "currency", "billing")
                .WithOptional("description", "trialPeriodDays", "maxDunningAttempts", "dunningScheduleDays", "gracePeriodDays", "metadata"),
            ["plans.get"] = LunixiRoute.Get(Subs + "/plans/:planId"),
            ["plans.list"] = LunixiRoute.Get(Subs + "/plans").WithQuery(Cursor("productId", "includeArchived")),
            ["plans.update"] = LunixiRoute.Patch(Subs + "/plans/:planId").AsFree(),
            ["plans.archive"] = LunixiRoute.Delete(Subs + "/plans/:planId"),
            ["plans.translations"] = LunixiRoute.Get(Subs + "/plans/:planId/translations"),
            ["plans.upsertTranslation"] = LunixiRoute.Patch(Subs + "/plans/:planId/translations/:locale").AsFree(),

            ["customers.upsert"] = LunixiRoute.Put(Subs + "/customers").KeyAccepted()
                .WithRequired("externalId").WithOptional("email", "name", "phone", "locale", "metadata"),
            ["customers.get"] = LunixiRoute.Get(Subs + "/customers/:customerId"),
            ["customers.getByExternalId"] = LunixiRoute.Get(Subs + "/customers/by-external/:externalId"),
            ["customers.list"] = LunixiRoute.Get(Subs + "/customers").WithQuery(Cursor("search", "includeArchived")),
            ["customers.creditBalance"] = LunixiRoute.Get(Subs + "/customers/:customerId/credit-balance"),
            ["customers.adjustCredit"] = LunixiRoute.Post(Subs + "/customers/:customerId/credit-balance/adjust").AsFree(),
            ["customers.archive"] = LunixiRoute.Delete(Subs + "/customers/:customerId"),

            ["cards.add"] = LunixiRoute.Post(Subs + "/cards").KeyAccepted()
                .WithRequired("customerId", "paymentMethodToken")
                .WithOptional("brand", "last4", "bin", "expireMonth", "expireYear", "cardholderName", "isDefault", "nickname",
                    "verificationMode", "supportedCurrencies"),
            ["cards.get"] = LunixiRoute.Get(Subs + "/cards/:cardId"),
            ["cards.list"] = LunixiRoute.Get(Subs + "/cards").WithQuery(Cursor("customerId", "includeInactive")),
            ["cards.setDefault"] = LunixiRoute.Post(Subs + "/cards/:cardId/set-default"),
            ["cards.deactivate"] = LunixiRoute.Delete(Subs + "/cards/:cardId").WithOptional("reason").WithOptionalBody(),
            ["cards.verify"] = LunixiRoute.Post(Subs + "/cards/:cardId/verify").AsFree().WithOptionalBody(),
            ["cards.update"] = LunixiRoute.Patch(Subs + "/cards/:cardId").AsFree(),

            ["invoices.list"] = LunixiRoute.Get(Subs + "/invoices").WithQuery(Cursor("subscriptionId", "status")),
            ["invoices.get"] = LunixiRoute.Get(Subs + "/invoices/:invoiceId"),
            ["invoices.void"] = LunixiRoute.Post(Subs + "/invoices/:invoiceId/void").WithOptional("reason").WithOptionalBody(),
            ["invoices.retryCharge"] = LunixiRoute.Post(Subs + "/invoices/:invoiceId/retry-charge").KeyAccepted(),

            ["portal.createSdkSession"] = LunixiRoute.Post(Subs + "/portal/sdk-session")
                .WithRequired("customerId", "origin").WithOptional("sdkMode", "portalLinkId"),
            ["portal.createPortalLink"] = LunixiRoute.Post(PortalPanel + "/links").AsFree(),
            ["portal.listLinks"] = LunixiRoute.Get(PortalPanel + "/links").WithQuery("customerId", "status", "limit"),
            ["portal.getLink"] = LunixiRoute.Get(PortalPanel + "/links/:linkId"),
            ["portal.updateLink"] = LunixiRoute.Patch(PortalPanel + "/links/:linkId").AsFree(),
            ["portal.revokeLink"] = LunixiRoute.Delete(PortalPanel + "/links/:linkId").WithOptional("reason").WithOptionalBody(),
            ["portal.linkActivity"] = LunixiRoute.Get(PortalPanel + "/links/:linkId/activity"),
            ["portal.sendInvite"] = LunixiRoute.Post(PortalPanel + "/invite").AsFree(),
            ["portal.getSettings"] = LunixiRoute.Get(PortalPanel + "/settings"),
            ["portal.updateSettings"] = LunixiRoute.Put(PortalPanel + "/settings").AsFree(),

            ["products.create"] = LunixiRoute.Post(Subs + "/products").AsFree(),
            ["products.list"] = LunixiRoute.Get(Subs + "/products").WithQuery("limit", "cursor", "active"),
            ["products.get"] = LunixiRoute.Get(Subs + "/products/:productId"),
            ["products.update"] = LunixiRoute.Patch(Subs + "/products/:productId").AsFree(),
            ["products.archive"] = LunixiRoute.Delete(Subs + "/products/:productId"),
            ["products.translations"] = LunixiRoute.Get(Subs + "/products/:productId/translations"),
            ["products.upsertTranslation"] = LunixiRoute.Patch(Subs + "/products/:productId/translations/:locale").AsFree(),

            ["features.create"] = LunixiRoute.Post(Subs + "/features").AsFree(),
            ["features.list"] = LunixiRoute.Get(Subs + "/features").WithAnyQuery(),
            ["features.get"] = LunixiRoute.Get(Subs + "/features/:featureId"),
            ["features.update"] = LunixiRoute.Patch(Subs + "/features/:featureId").AsFree(),
            ["features.delete"] = LunixiRoute.Delete(Subs + "/features/:featureId"),
            ["features.translations"] = LunixiRoute.Get(Subs + "/features/:featureId/translations"),
            ["features.upsertTranslation"] = LunixiRoute.Patch(Subs + "/features/:featureId/translations/:locale").AsFree(),

            ["addons.create"] = LunixiRoute.Post(Subs + "/addons").AsFree(),
            ["addons.list"] = LunixiRoute.Get(Subs + "/addons").WithAnyQuery(),
            ["addons.update"] = LunixiRoute.Patch(Subs + "/addons/:addonId").AsFree(),
            ["addons.deactivate"] = LunixiRoute.Delete(Subs + "/addons/:addonId"),

            ["usage.record"] = LunixiRoute.Post(Subs + "/usage/record").AsFree(),
            ["usage.checkEntitlement"] = LunixiRoute.Post(Subs + "/usage/check-entitlement").AsFree(),
        };
        return routes.Freeze();
    }
}
