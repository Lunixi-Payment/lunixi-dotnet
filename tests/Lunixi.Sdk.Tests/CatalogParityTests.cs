using System.Reflection;
using System.Text.Json;

namespace Lunixi.Sdk.Tests;

/// <summary>
/// Checks the SDK surface against the shared operation catalog of the SDK monorepo (<c>contracts/products/*.json</c>).
/// Every <c>required</c> operation of the products below must exist as a public method; names that differ on purpose come
/// from <c>contracts/sdk-names.json</c> (<c>dotnet</c>, without the <c>Async</c> suffix; null means not offered). The route
/// tables (<c>WALLET_ROUTES</c>, <c>SANCTION_ROUTES</c>, <c>BANK_TRANSFER_ROUTES</c>, <c>SUBSCRIPTION_ROUTES</c>, <c>KYC_ROUTES</c>,
/// <c>IDENTITY_ROUTES</c>, <c>MARKETPLACE_ROUTES</c>), the value lists, the enum groups and the patterns are compared field by field. Outside the monorepo (the published SDK repository) the catalog is absent and the tests have
/// nothing to check.
/// </summary>
public class CatalogParityTests
{
    private static readonly string[] Products =
    [
        "payments", "fraud", "payment-links", "reference", "physical-pos", "wallet", "bank-connect", "sanction", "insurance",
        "subscriptions", "kyc", "identity", "marketplace",
    ];

    [Fact]
    public void EveryRequiredCatalogOperationExists()
    {
        var contracts = FindContracts();
        if (contracts is null) return;
        var names = ReadNames(Path.Combine(contracts, "sdk-names.json"));
        var missing = new List<string>();
        var checkedOperations = 0;
        foreach (var product in Products)
        {
            using var catalog = Catalog(contracts, product);
            foreach (var operation in catalog.RootElement.GetProperty("operations").EnumerateObject())
            {
                if (operation.Value.GetProperty("status").GetString() != "required") continue;
                checkedOperations++;
                if (!Exists(operation.Name, names)) missing.Add(operation.Name);
            }
        }
        Assert.True(checkedOperations > 0, "The catalog lists no required operations.");
        Assert.True(missing.Count == 0, "Missing .NET operations: " + string.Join(", ", missing));
    }

    /// <summary>
    /// Each route table matches the catalog's <c>routes</c> group row for row: the same route names in the same order, and per
    /// route the method, the path, the key rule, the step-up, screening, binary, free-body, optional-body and any-query flags,
    /// and the body / query field lists in order.
    /// </summary>
    [Fact]
    public void RouteTablesMatchTheCatalog()
    {
        var contracts = FindContracts();
        if (contracts is null) return;
        AssertRoutes(contracts, "wallet", "WALLET_ROUTES", WalletRoutes.All);
        AssertRoutes(contracts, "sanction", "SANCTION_ROUTES", SanctionRoutes.All);
        AssertRoutes(contracts, "bank-connect", "BANK_TRANSFER_ROUTES", BankTransferRoutes.All);
        AssertRoutes(contracts, "subscriptions", "SUBSCRIPTION_ROUTES", SubscriptionRoutes.All);
        AssertRoutes(contracts, "kyc", "KYC_ROUTES", KycRoutes.All);
        AssertRoutes(contracts, "identity", "IDENTITY_ROUTES", IdentityRoutes.All);
        AssertRoutes(contracts, "marketplace", "MARKETPLACE_ROUTES", MarketplaceRoutes.All);
    }

    [Fact]
    public void ValueListsMatchTheCatalog()
    {
        var contracts = FindContracts();
        if (contracts is null) return;
        var expected = new Dictionary<string, Dictionary<string, IReadOnlyList<string>>>
        {
            ["wallet"] = new()
            {
                ["WALLET_AMOUNT_FIELDS"] = WalletRoutes.AmountFields,
                ["WalletAccountType.ALL"] = WalletAccountTypes.All,
                ["WalletEndUserStatus.ALL"] = WalletEndUserStatuses.All,
                ["WalletKycLevel.ALL"] = WalletKycLevels.All,
                ["WalletOtpChannel.ALL"] = WalletOtpChannels.All,
                ["WalletQrType.ALL"] = WalletQrTypes.All,
                ["WalletQrImageFormat.ALL"] = WalletQrImageFormats.All,
                ["WalletBulkTargetType.ALL"] = WalletBulkTargetTypes.All,
                ["WalletCorporateAccountStatus.ALL"] = WalletCorporateAccountStatuses.All,
            },
            ["bank-connect"] = new()
            {
                ["BANK_CONNECT_EVENT_TYPES"] = BankConnectEvents.All,
                ["BANK_TRANSFER_EVENT_TYPES"] = BankTransferEvents.All,
                ["BankConnectionStatus.ALL"] = BankConnectionStatuses.All,
                ["BankAccountStatus.ALL"] = BankAccountStatuses.All,
                ["BankTransactionDirection.ALL"] = BankTransactionDirections.All,
                ["BankTransactionType.ALL"] = BankTransactionTypes.All,
                ["BankAvailability.ALL"] = BankAvailabilities.All,
                ["BankCapability.ALL"] = BankCapabilities.All,
                ["BankSyncStatus.ALL"] = BankSyncStatuses.All,
                ["BankTransferStatus.ALL"] = BankTransferStatuses.All,
                ["BankTransferRail.ALL"] = BankTransferRails.All,
                ["BankTransferInitiatedVia.ALL"] = BankTransferInitiatedVia.All,
                ["BankFxOrderStatus.ALL"] = BankFxOrderStatuses.All,
                ["BankFxRequestedSide.ALL"] = BankFxRequestedSides.All,
            },
            ["sanction"] = new()
            {
                ["RiskDomain.ALL"] = SanctionRiskDomains.All,
                ["ScreenMode.ALL"] = SanctionScreenModes.All,
                ["WalletScreenMode.ALL"] = SanctionWalletScreenModes.All,
                ["SubjectType.ALL"] = SanctionSubjectTypes.All,
                ["CoverageStatus.ALL"] = SanctionCoverageStatuses.All,
                ["HitType.ALL"] = SanctionHitTypes.All,
            },
            ["insurance"] = new()
            {
                ["INSURANCE_QUOTE_FIELDS"] = InsuranceFields.Quote,
                ["INSURANCE_LEAD_FIELDS"] = InsuranceFields.Lead,
                ["INSURANCE_PRODUCT_SEARCH_FIELDS"] = InsuranceFields.ProductSearch,
                ["INSURANCE_ORDER_FIELDS"] = InsuranceFields.Order,
                ["INSURANCE_CONSENT_FIELDS"] = InsuranceFields.Consent,
                ["INSURANCE_EXTERNAL_PAYMENT_FIELDS"] = InsuranceFields.ExternalPayment,
                ["INSURANCE_EXTERNAL_REFUND_FIELDS"] = InsuranceFields.ExternalRefund,
                ["INSURANCE_EVENT_TYPES"] = InsuranceEvents.All,
                ["InsuranceEvents.ORDER_EVENTS"] = InsuranceEvents.OrderEvents,
                ["InsuranceEvents.LEAD_EVENTS"] = InsuranceEvents.LeadEvents,
                ["InsurancePremiumCollectedBy.ALL"] = InsurancePremiumCollectedBy.All,
                ["InsuranceQuoteStatus.ALL"] = InsuranceQuoteStatuses.All,
                ["InsuranceOrderStatus.ALL"] = InsuranceOrderStatuses.All,
                ["InsuranceCollectionStatus.ALL"] = InsuranceCollectionStatuses.All,
                ["InsurancePolicyStatus.ALL"] = InsurancePolicyStatuses.All,
                ["InsuranceRefundStatus.ALL"] = InsuranceRefundStatuses.All,
                ["InsuranceDocumentStatus.ALL"] = InsuranceDocumentStatuses.All,
                ["InsurancePaymentMode.ALL"] = InsurancePaymentModes.All,
                ["InsuranceOrderSurface.ALL"] = InsuranceOrderSurfaces.All,
                ["InsuranceLeadStatus.ALL"] = InsuranceLeadStatuses.All,
                ["InsuranceReferenceKind.ALL"] = InsuranceReferenceKinds.All,
            },
            ["subscriptions"] = new()
            {
                ["CollectionMethod.ALL"] = SubscriptionCollectionMethods.All,
                ["SUBSCRIPTION_ACTIVE_STATES"] = SubscriptionStatuses.ActiveStates,
                ["SUBSCRIPTION_TERMINAL_STATES"] = SubscriptionStatuses.TerminalStates,
                ["SubscriptionStatus.ACTIVE_STATES"] = SubscriptionStatuses.ActiveStates,
                ["SubscriptionStatus.TERMINAL_STATES"] = SubscriptionStatuses.TerminalStates,
                ["SUBSCRIPTION_BILLING_RULE_FIELDS"] = SubscriptionRoutes.BillingRuleFields,
                ["SUBSCRIPTION_EVENT_TYPES"] = SubscriptionEvents.All,
            },
            ["kyc"] = new()
            {
                ["AchievedAssurance.DOMAINS"] = KycAssuranceDomains.All,
                ["EvidencePurpose.ALL"] = KycEvidencePurposes.All,
                ["KYC_EVENT_TYPES"] = KycEvents.All,
                ["KYC_SESSION_TERMINAL_STATES"] = KycSessionStatuses.TerminalStates,
                ["SessionStatus.TERMINAL_STATES"] = KycSessionStatuses.TerminalStates,
                ["SessionType.ALL"] = KycSessionTypes.All,
            },
            ["identity"] = new() { ["IDENTITY_ANALYZE_FIELDS"] = IdentityRoutes.AnalyzeFields },
            ["marketplace"] = new()
            {
                ["MARKETPLACE_CHARGE_REQUIRED_FIELDS"] = MarketplaceRoutes.ChargeRequiredFields,
                ["MARKETPLACE_SELLER_FIELDS"] = MarketplaceRoutes.SellerFields,
            },
        };
        foreach (var (route, filters) in BankConnectFilters.All) expected["bank-connect"]["BANK_CONNECT_FILTERS." + route] = filters;
        foreach (var (field, values) in SanctionRoutes.ValueSets) expected["sanction"]["SANCTION_VALUE_SETS." + field] = values;
        foreach (var (route, filters) in InsuranceFields.Filters) expected["insurance"]["INSURANCE_FILTERS." + route] = filters;

        var compared = 0;
        foreach (var (product, lists) in expected)
        {
            using var catalog = Catalog(contracts, product);
            var groups = catalog.RootElement.GetProperty("groups");
            foreach (var (name, values) in lists)
            {
                if (!groups.TryGetProperty(name, out var group)) continue;
                Assert.Equal(Strings(group.GetProperty("values")), values);
                compared++;
            }
        }
        Assert.True(compared > 0, "No catalog list was compared.");
    }

    /// <summary>The value constants (Node's frozen enums, the catalog's <c>enum</c> groups) carry the same values in the same order.</summary>
    [Fact]
    public void EnumGroupsMatchTheCatalog()
    {
        var contracts = FindContracts();
        if (contracts is null) return;
        var expected = new Dictionary<string, Dictionary<string, IReadOnlyList<string>>>
        {
            ["subscriptions"] = new()
            {
                ["SubscriptionStatus"] = SubscriptionStatuses.All,
                ["SubscriptionInvoiceStatus"] = SubscriptionInvoiceStatuses.All,
                ["SubscriptionCollectionMethod"] = SubscriptionCollectionMethods.All,
                ["SubscriptionBillingFrequency"] = SubscriptionBillingFrequencies.All,
                ["SubscriptionCardVerificationMode"] = SubscriptionCardVerificationModes.All,
                ["SubscriptionPlanChangeBehavior"] = SubscriptionPlanChangeBehaviors.All,
                ["SubscriptionPortalSdkMode"] = SubscriptionPortalSdkModes.All,
                ["SubscriptionEvents"] = SubscriptionEvents.All,
            },
            ["kyc"] = new()
            {
                ["KycSessionType"] = KycSessionTypes.All,
                ["KycSessionStatus"] = KycSessionStatuses.All,
                ["KycEvidencePurpose"] = KycEvidencePurposes.All,
                ["KycAssuranceLevel"] = KycAssuranceLevels.All,
                ["KycAssuranceDomain"] = KycAssuranceDomains.All,
                ["KycEvents"] = KycEvents.All,
            },
            ["marketplace"] = new()
            {
                ["MarketplaceDealerStatus"] = MarketplaceDealerStatuses.All,
                ["MarketplaceDealerType"] = MarketplaceDealerTypes.All,
                ["MarketplaceMultiSellerPolicy"] = MarketplaceSellerPolicies.All,
            },
        };
        var compared = 0;
        foreach (var (product, enums) in expected)
        {
            using var catalog = Catalog(contracts, product);
            var groups = catalog.RootElement.GetProperty("groups");
            foreach (var (name, values) in enums)
            {
                if (!groups.TryGetProperty(name, out var group)) continue;
                Assert.Equal("enum", group.GetProperty("kind").GetString());
                Assert.Equal(group.GetProperty("values").EnumerateObject().Select(entry => entry.Value.GetString()!).ToArray(), values);
                compared++;
            }
        }
        Assert.True(compared > 0, "No catalog enum was compared.");
    }

    [Fact]
    public void PatternsAndSubscriptionsMatchTheCatalog()
    {
        var contracts = FindContracts();
        if (contracts is null) return;
        var expected = new Dictionary<string, Dictionary<string, (string Key, string Value)>>
        {
            ["wallet"] = new() { ["AMOUNT_PATTERN"] = ("$pattern", WalletRoutes.AmountPattern) },
            ["bank-connect"] = new()
            {
                ["BANK_TRANSFER_IDEMPOTENCY_KEY_PATTERN"] = ("$pattern", BankTransferRoutes.IdempotencyKeyPattern),
                ["BANK_TRANSFER_PAYMENT_PURPOSE_PATTERN"] = ("$pattern", BankTransferRoutes.PaymentPurposePattern),
                ["BankConnectEvents"] = ("ALL_EVENTS", BankConnectEvents.AllEvents),
                ["BankTransferEvents"] = ("ALL_EVENTS", BankTransferEvents.AllEvents),
            },
            ["insurance"] = new()
            {
                ["INSURANCE_IDEMPOTENCY_KEY_PATTERN"] = ("$pattern", InsuranceFields.IdempotencyKeyPattern),
                ["InsuranceEvents"] = ("ALL_EVENTS", InsuranceEvents.AllEvents),
            },
            ["subscriptions"] = new() { ["PortalLink"] = ("DEFAULT_HOSTED_BASE", SubscriptionPortalLinks.HostedBase) },
            ["marketplace"] = new() { ["DealerOnboardingLink"] = ("DEFAULT_HOSTED_BASE", MarketplaceDealerOnboarding.HostedBase) },
        };
        var compared = 0;
        foreach (var (product, values) in expected)
        {
            using var catalog = Catalog(contracts, product);
            var groups = catalog.RootElement.GetProperty("groups");
            foreach (var (name, (key, value)) in values)
            {
                if (!groups.TryGetProperty(name, out var group) || !group.GetProperty("values").TryGetProperty(key, out var catalogValue)) continue;
                Assert.Equal(catalogValue.GetString(), value);
                compared++;
            }
        }
        Assert.True(compared > 0, "No catalog pattern was compared.");
    }

    private static void AssertRoutes(string contracts, string product, string groupName, IReadOnlyDictionary<string, LunixiRoute> table)
    {
        using var catalog = Catalog(contracts, product);
        if (!catalog.RootElement.GetProperty("groups").TryGetProperty(groupName, out var group)) return;
        Assert.Equal("routes", group.GetProperty("kind").GetString());
        var rows = group.GetProperty("values").EnumerateObject().ToArray();
        Assert.Equal(rows.Select(row => row.Name).ToArray(), table.Keys.ToArray());
        foreach (var row in rows)
        {
            var route = table[row.Name];
            var label = groupName + "." + row.Name;
            Assert.True(row.Value.GetProperty("method").GetString() == route.Method, label + " method");
            Assert.True(row.Value.GetProperty("path").GetString() == route.Path, label + " path");
            Assert.True(KeyRule(row.Value) == route.IdempotencyKeyRule, label + " idempotencyKey");
            Assert.True(Flag(row.Value, "stepUp") == route.StepUp, label + " stepUp");
            Assert.True(Flag(row.Value, "screening") == route.Screening, label + " screening");
            Assert.True(Flag(row.Value, "binary") == route.Binary, label + " binary");
            Assert.True(Flag(row.Value, "free") == route.Free, label + " free");
            Assert.True(Flag(row.Value, "optionalBody") == route.OptionalBody, label + " optionalBody");
            Assert.True(Flag(row.Value, "anyQuery") == route.AnyQuery, label + " anyQuery");
            Assert.True(List(row.Value, "required").SequenceEqual(route.Required), label + " required");
            Assert.True(List(row.Value, "optional").SequenceEqual(route.Optional), label + " optional");
            Assert.True(List(row.Value, "anyOf").SequenceEqual(route.AnyOf), label + " anyOf");
            Assert.True(List(row.Value, "query").SequenceEqual(route.Query), label + " query");
        }
    }

    private static bool Flag(JsonElement row, string name) => row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    /// <summary>The key rule of a catalog row: a string (<c>required</c> / <c>optional</c> / <c>none</c>) or the older boolean.</summary>
    private static string KeyRule(JsonElement row)
    {
        if (!row.TryGetProperty("idempotencyKey", out var value)) return LunixiRoute.KeyNone;
        if (value.ValueKind == JsonValueKind.String) return value.GetString()!;
        return value.ValueKind == JsonValueKind.True ? LunixiRoute.KeyRequired : LunixiRoute.KeyNone;
    }

    private static string[] List(JsonElement row, string name) => row.TryGetProperty(name, out var value) ? Strings(value) : [];

    private static string[] Strings(JsonElement list) => list.EnumerateArray().Select(entry => entry.GetString()!).ToArray();

    private static JsonDocument Catalog(string contracts, string product)
        => JsonDocument.Parse(File.ReadAllText(Path.Combine(contracts, "products", product + ".json")));

    /// <summary>
    /// <c>product.sub.leaf</c>: the product is a <see cref="LunixiClient"/> property, each sub-segment a property of the
    /// previous client and the leaf a public instance method (with or without <c>Async</c>). A renamed operation is looked
    /// up by its <c>sdk-names.json</c> name on the product client.
    /// </summary>
    private static bool Exists(string operation, IReadOnlyDictionary<string, string?> names)
    {
        var segments = operation.Split('.');
        var type = PropertyType(typeof(LunixiClient), segments[0]);
        if (type is null) return false;
        if (names.TryGetValue(operation, out var renamed))
            return renamed is null || HasMethod(type, renamed);
        foreach (var segment in segments[1..^1])
        {
            type = PropertyType(type, segment);
            if (type is null) return false;
        }
        return HasMethod(type, Pascal(segments[^1]));
    }

    private static Type? PropertyType(Type owner, string segment)
        => owner.GetProperty(Pascal(segment), BindingFlags.Public | BindingFlags.Instance)?.PropertyType;

    private static bool HasMethod(Type type, string name)
        => type.GetMethods(BindingFlags.Public | BindingFlags.Instance).Any(m => m.Name == name || m.Name == name + "Async");

    private static string Pascal(string name) => char.ToUpperInvariant(name[0]) + name[1..];

    private static Dictionary<string, string?> ReadNames(string path)
    {
        var names = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (!File.Exists(path)) return names;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("dotnet", out var dotnet)) return names;
        foreach (var entry in dotnet.EnumerateObject())
            names[entry.Name] = entry.Value.ValueKind == JsonValueKind.Null ? null : entry.Value.GetString();
        return names;
    }

    /// <summary>The monorepo's <c>contracts</c> folder above the test output, or null when this is not the monorepo.</summary>
    private static string? FindContracts()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var contracts = Path.Combine(directory.FullName, "contracts");
            if (Products.All(product => File.Exists(Path.Combine(contracts, "products", product + ".json")))) return contracts;
        }
        return null;
    }
}
