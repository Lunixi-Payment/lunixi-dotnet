using System.Text.Json;

namespace Lunixi;

/// <summary>Sub-dealer lifecycle (<c>status</c>). Only <c>ACTIVE</c> dealers can receive a split.</summary>
public static class MarketplaceDealerStatuses
{
    /// <summary><c>DRAFT</c></summary>
    public const string Draft = "DRAFT";
    /// <summary><c>PENDING_KYC</c></summary>
    public const string PendingKyc = "PENDING_KYC";
    /// <summary><c>ACTIVE</c></summary>
    public const string Active = "ACTIVE";
    /// <summary><c>SUSPENDED</c></summary>
    public const string Suspended = "SUSPENDED";
    /// <summary><c>HOLD</c></summary>
    public const string Hold = "HOLD";
    /// <summary><c>CLOSED</c></summary>
    public const string Closed = "CLOSED";
    /// <summary>Every dealer status.</summary>
    public static IReadOnlyList<string> All { get; } = [Draft, PendingKyc, Active, Suspended, Hold, Closed];
}

/// <summary><c>dealerType</c> of a sub-dealer.</summary>
public static class MarketplaceDealerTypes
{
    /// <summary><c>LEGAL_ENTITY</c></summary>
    public const string LegalEntity = "LEGAL_ENTITY";
    /// <summary><c>INDIVIDUAL</c></summary>
    public const string Individual = "INDIVIDUAL";
    /// <summary>Every dealer type.</summary>
    public static IReadOnlyList<string> All { get; } = [LegalEntity, Individual];
}

/// <summary>The dealer self-onboarding link of <see cref="MarketplaceClient.OnboardingTokenAsync"/>.</summary>
public static class MarketplaceDealerOnboarding
{
    /// <summary>The hosted dealer onboarding wizard, used when the answer carries no <c>url</c>.</summary>
    public const string HostedBase = "https://altbayi.lunixi.com";

    /// <summary>The onboarding URL of an <see cref="MarketplaceClient.OnboardingTokenAsync"/> answer: its <c>url</c>, else the hosted wizard with its <c>token</c>.</summary>
    public static string Url(JsonElement link, string baseUrl = HostedBase)
    {
        if (LunixiClient.Property(link, "url") is { Length: > 0 } url) return url;
        return baseUrl.TrimEnd('/') + "/m/" + Uri.EscapeDataString(LunixiClient.Property(link, "token") ?? "");
    }
}

/// <summary>
/// The single description of every marketplace route the SDK calls: the split charges under
/// <c>/api/v1/payments/marketplace/*</c> (signed per request, <c>Idempotency-Key</c> optional, body checked by
/// <see cref="MarketplaceClient"/>) and the dealer, commission, payout and audit routes under <c>/api/v1/marketplace/*</c>
/// (bearer-only). The table is the Node SDK's <c>MARKETPLACE_ROUTES</c>, row for row, and the shared catalog's group of the same name.
/// </summary>
public static class MarketplaceRoutes
{
    private const string Mp = "/api/v1/marketplace";
    private const string Pay = "/api/v1/payments/marketplace";

    /// <summary>Fields of a seller leg: the aggregate amount of one sub-dealer (<see cref="MarketplaceSeller"/>).</summary>
    public static IReadOnlyList<string> SellerFields { get; } = ["subDealerId", "amountMinor", "scenario"];

    /// <summary>
    /// Fields a split card charge (<see cref="MarketplaceClient.ChargeMarketplaceCard2dAsync"/> / <c>3d</c>) needs besides the
    /// seller legs: the commercial context of a traceable card payment.
    /// </summary>
    public static IReadOnlyList<string> ChargeRequiredFields { get; } = ["amount", "currency", "orderId", "card", "buyer", "billingAddress", "basketItems"];

    /// <summary>Every route by name, in the Node table's order.</summary>
    public static IReadOnlyDictionary<string, LunixiRoute> All { get; } = Build();

    private static LunixiRoute Charge(string path) => LunixiRoute.Post(Pay + path).Signed().KeyAccepted().AsFree();

    private static IReadOnlyDictionary<string, LunixiRoute> Build()
    {
        var routes = new OrderedRoutes
        {
            ["createCheckoutIntent"] = Charge("/intents"),
            ["chargeMarketplaceCard2d"] = Charge("/2d"),
            ["chargeMarketplaceCard3d"] = Charge("/3d"),

            ["createDealer"] = LunixiRoute.Post(Mp + "/dealers").WithRequired("legalName").WithOptional("taxId", "dealerType", "categoryCode", "email", "externalRef"),
            ["bulkCreateDealers"] = LunixiRoute.Post(Mp + "/dealers/bulk").WithRequired("dealers"),
            ["listDealers"] = LunixiRoute.Get(Mp + "/dealers").WithQuery("status", "search", "limit", "offset"),
            ["getDealer"] = LunixiRoute.Get(Mp + "/dealers/:dealerId"),
            ["updateDealerStatus"] = LunixiRoute.Put(Mp + "/dealers/:dealerId/status").WithRequired("status"),
            ["onboardingToken"] = LunixiRoute.Post(Mp + "/dealers/:dealerId/onboarding-token"),
            ["dealerApplication"] = LunixiRoute.Get(Mp + "/dealers/:dealerId/application"),
            ["submitDealerApplication"] = LunixiRoute.Post(Mp + "/dealers/:dealerId/submit").AsFree(),
            ["setDealerTerms"] = LunixiRoute.Post(Mp + "/dealers/:dealerId/terms").AsFree(),
            ["signDealerContract"] = LunixiRoute.Post(Mp + "/dealers/:dealerId/sign").AsFree(),
            ["activateDealer"] = LunixiRoute.Post(Mp + "/dealers/:dealerId/activate"),
            ["recordDealerKycResult"] = LunixiRoute.Post(Mp + "/dealers/:dealerId/kyc-result").AsFree(),

            ["payoutLedger"] = LunixiRoute.Get(Mp + "/payout-ledger").WithQuery("status", "subDealerId", "days", "limit"),
            ["transactions"] = LunixiRoute.Get(Mp + "/transactions").WithQuery("status", "subDealerId", "limit"),
            ["commissionRules"] = LunixiRoute.Get(Mp + "/commission-rules").WithQuery("limit"),
            ["upsertCommissionRule"] = LunixiRoute.Post(Mp + "/commission-rules").AsFree(),
            ["previewCommissionRule"] = LunixiRoute.Post(Mp + "/commission-rules/preview").AsFree(),
            ["resolveCommission"] = LunixiRoute.Get(Mp + "/commission/resolve").WithQuery("subDealerId", "categoryCode", "mtdVolumeMinor"),

            ["createPayout"] = LunixiRoute.Post(Mp + "/payouts").AsFree(),
            ["markPayoutPaid"] = LunixiRoute.Post(Mp + "/payouts/:payoutId/mark-paid").AsFree(),
            ["bulkMarkPayoutsPaid"] = LunixiRoute.Post(Mp + "/payouts/bulk-mark-paid").AsFree(),

            ["auditLog"] = LunixiRoute.Get(Mp + "/audit-log").WithQuery("limit", "type"),
            ["verifyAuditLog"] = LunixiRoute.Get(Mp + "/audit-log/verify"),
        };
        return routes.Freeze();
    }
}
