namespace Lunixi;

/// <summary>Risk domains a screening can be narrowed to (<c>riskDomains</c>). Left out: every domain.</summary>
public static class SanctionRiskDomains
{
    /// <summary><c>sanctions</c></summary>
    public const string Sanctions = "sanctions";
    /// <summary><c>pep</c>: politically exposed persons.</summary>
    public const string Pep = "pep";
    /// <summary><c>rca</c>: relatives and close associates.</summary>
    public const string Rca = "rca";
    /// <summary><c>wanted</c></summary>
    public const string Wanted = "wanted";
    /// <summary><c>regulatory</c></summary>
    public const string Regulatory = "regulatory";
    /// <summary><c>debarment</c></summary>
    public const string Debarment = "debarment";
    /// <summary><c>ownership</c></summary>
    public const string Ownership = "ownership";
    /// <summary><c>vessel</c></summary>
    public const string Vessel = "vessel";
    /// <summary><c>securities</c></summary>
    public const string Securities = "securities";
    /// <summary>Every risk domain.</summary>
    public static IReadOnlyList<string> All { get; } = [Sanctions, Pep, Rca, Wanted, Regulatory, Debarment, Ownership, Vessel, Securities];
}

/// <summary>Name-screening presets (<c>mode</c>). Sent with <c>riskDomains</c>, their intersection is screened.</summary>
public static class SanctionScreenModes
{
    /// <summary><c>sanctions_only</c></summary>
    public const string SanctionsOnly = "sanctions_only";
    /// <summary><c>sanctions_pep</c></summary>
    public const string SanctionsPep = "sanctions_pep";
    /// <summary><c>full_risk</c></summary>
    public const string FullRisk = "full_risk";
    /// <summary>Every name-screening mode.</summary>
    public static IReadOnlyList<string> All { get; } = [SanctionsOnly, SanctionsPep, FullRisk];
}

/// <summary>Wallet-screening depth (<c>mode</c>); the wider modes add exposure and crypto-intelligence hits.</summary>
public static class SanctionWalletScreenModes
{
    /// <summary><c>wallet_sanctions</c></summary>
    public const string WalletSanctions = "wallet_sanctions";
    /// <summary><c>wallet_risk</c></summary>
    public const string WalletRisk = "wallet_risk";
    /// <summary><c>wallet_full_aml</c></summary>
    public const string WalletFullAml = "wallet_full_aml";
    /// <summary>Every wallet-screening mode.</summary>
    public static IReadOnlyList<string> All { get; } = [WalletSanctions, WalletRisk, WalletFullAml];
}

/// <summary>The subject of a name screening or a monitoring registration (<c>subjectType</c>).</summary>
public static class SanctionSubjectTypes
{
    /// <summary><c>person</c></summary>
    public const string Person = "person";
    /// <summary><c>organization</c></summary>
    public const string Organization = "organization";
    /// <summary>Every subject type.</summary>
    public static IReadOnlyList<string> All { get; } = [Person, Organization];
}

/// <summary>
/// How much of the requested scope a screening covered (<c>coverageStatus</c>). <c>COMPLETE</c>: every requested risk domain
/// was screened. <c>PARTIAL</c>: some requested domain has no list (<c>uncoveredRiskDomains</c>, <c>uncoveredSources</c>); no
/// match is clear only for what was screened. <c>DEGRADED</c>: the candidate search was limited; screen again.
/// </summary>
public static class SanctionCoverageStatuses
{
    /// <summary><c>COMPLETE</c></summary>
    public const string Complete = "COMPLETE";
    /// <summary><c>PARTIAL</c></summary>
    public const string Partial = "PARTIAL";
    /// <summary><c>DEGRADED</c></summary>
    public const string Degraded = "DEGRADED";
    /// <summary>Every coverage status.</summary>
    public static IReadOnlyList<string> All { get; } = [Complete, Partial, Degraded];
}

/// <summary>Strength of a screening hit.</summary>
public static class SanctionHitTypes
{
    /// <summary><c>exact</c></summary>
    public const string Exact = "exact";
    /// <summary><c>probable</c></summary>
    public const string Probable = "probable";
    /// <summary><c>possible</c></summary>
    public const string Possible = "possible";
    /// <summary>Every hit type.</summary>
    public static IReadOnlyList<string> All { get; } = [Exact, Probable, Possible];
}

/// <summary>
/// The single description of every sanction route the SDK calls (<c>/api/v1/sanction/*</c>): method, path template, whether
/// the route is a screening (its answer must be <c>status: "success"</c>), the body fields the gateway forwards to the sanction
/// service and the query parameters it reads. The body is not validated by the gateway and a field outside the service's
/// contract is silently dropped there, so <see cref="SanctionClient"/> refuses it before sending. The table is the Node SDK's
/// <c>SANCTION_ROUTES</c>, row for row, and the shared catalog's group of the same name. Only routes an API key can reach are
/// listed (they need <c>sanction:view_results</c>).
/// </summary>
public static class SanctionRoutes
{
    private const string Base = "/api/v1/sanction";
    private static readonly string[] Page = ["limit", "cursor"];

    /// <summary>Fields of the nested lists, checked element by element, keyed by <c>route.field</c>.</summary>
    public static IReadOnlyDictionary<string, LunixiNestedFields> NestedFields { get; } = new Dictionary<string, LunixiNestedFields>(StringComparer.Ordinal)
    {
        ["screenName.identifiers"] = new("identifiers", ["type", "value"], []),
        ["screenTransaction.parties"] = new("parties", ["name"], ["role", "subjectType", "countryCode", "customerId", "dateOfBirth", "nationalId"]),
        ["screenTransaction.accounts"] = new("accounts", ["accountType", "value"], ["role", "providerCode", "chain"]),
    };

    /// <summary>Value sets checked before sending, keyed by <c>route.field</c>; the sanction service refuses anything else with 400.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ValueSets { get; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        ["screenName.riskDomains"] = SanctionRiskDomains.All,
        ["screenName.mode"] = SanctionScreenModes.All,
        ["screenName.subjectType"] = SanctionSubjectTypes.All,
        ["screenWallet.mode"] = SanctionWalletScreenModes.All,
        ["registerMonitoring.subjectType"] = SanctionSubjectTypes.All,
        ["registerMonitoring.riskDomainScope"] = SanctionRiskDomains.All,
    };

    /// <summary>Every route by name, in the Node table's order.</summary>
    public static IReadOnlyDictionary<string, LunixiRoute> All { get; } = Build();

    private static LunixiRoute Screening(string path) => LunixiRoute.Post(Base + path).AsScreening();
    private static LunixiRoute Read(string path) => LunixiRoute.Get(Base + path);

    private static IReadOnlyDictionary<string, LunixiRoute> Build()
    {
        var routes = new OrderedRoutes
        {
            ["screenName"] = Screening("/screening/name").WithRequired("name")
                .WithOptional("subjectType", "country", "dateOfBirth", "identifiers", "riskDomains", "mode", "threshold", "customerReference"),
            ["screenWallet"] = Screening("/screening/wallet").WithRequired("walletAddress").WithOptional("chain", "mode", "customerReference"),
            ["screenAccount"] = Screening("/screening/account").WithRequired("accountValue", "accountScheme")
                .WithOptional("providerCode", "productChannel", "customerReference"),
            // Neither parties nor accounts: the sanction service holds the transaction as not screenable.
            ["screenTransaction"] = Screening("/screening/transaction")
                .WithOptional("transactionType", "direction", "amount", "currency", "channel", "occurredAt", "parties", "accounts", "metadataJson")
                .WithAnyOf("parties", "accounts"),
            ["screenPublicProcurementBan"] = Screening("/intelligence/public-procurement-ban")
                .WithOptional("entityName", "tckn", "vkn", "entityType", "onlyActive", "limit", "customerReference", "productChannel")
                .WithAnyOf("entityName", "tckn", "vkn"),
            ["screenAdverseMedia"] = Screening("/intelligence/adverse-media")
                .WithOptional("entityName", "entityType", "tckn", "vkn", "countryCode", "limit", "onlyActive", "customerReference", "productChannel"),
            ["evaluateBehavioralAlert"] = Screening("/intelligence/behavioral-alert")
                .WithOptional("subjectReference", "customerReference", "productChannel", "velocityScore", "unusualPattern", "countryRiskLevel", "metadataJson"),
            ["listScreeningHistory"] = Read("/screening/history").WithQuery(Page),

            ["registerMonitoring"] = LunixiRoute.Post(Base + "/monitoring/register").WithRequired("externalCustomerId")
                .WithOptional("subjectType", "profileJson", "frequency", "riskDomainScope"),
            ["listMonitoringAlerts"] = Read("/monitoring/alerts").WithQuery("status", "limit", "cursor", "includeTotal"),
            ["listMonitoredEntities"] = Read("/monitoring/entities").WithQuery("activeOnly", "limit", "cursor", "includeTotal"),
            ["listMonitoringMatches"] = Read("/monitoring/matches").WithQuery("status", "limit", "cursor"),
            ["getDatasetFreshness"] = Read("/monitoring/dataset-freshness"),

            ["searchEntities"] = Read("/intelligence/entity-search").WithQuery("q", "limit"),
            ["getEntityProfile"] = Read("/intelligence/entity-profiles/:entityId").WithQuery("screeningRequestId"),
            ["getEntityGraph"] = Read("/intelligence/entity-graph/:entityId").WithQuery("depth"),
            ["getIndirectExposure"] = Read("/intelligence/indirect-exposure/:entityId").WithQuery("maxDepth", "entityIds"),
            ["getOwnershipAggregation"] = Read("/intelligence/ownership-aggregation/:entityId").WithQuery("depth", "entityIds"),

            ["listBlacklistEntries"] = Read("/blacklist").WithQuery(Page),
        };
        return routes.Freeze();
    }
}
