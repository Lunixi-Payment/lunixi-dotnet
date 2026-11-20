namespace Lunixi;

/// <summary><c>accountType</c> of a wallet end user.</summary>
public static class WalletAccountTypes
{
    /// <summary><c>PERSONAL</c></summary>
    public const string Personal = "PERSONAL";
    /// <summary><c>BUSINESS</c></summary>
    public const string Business = "BUSINESS";
    /// <summary>Every account type.</summary>
    public static IReadOnlyList<string> All { get; } = [Personal, Business];
}

/// <summary><c>status</c> of a wallet end user.</summary>
public static class WalletEndUserStatuses
{
    /// <summary><c>ACTIVE</c></summary>
    public const string Active = "ACTIVE";
    /// <summary><c>FROZEN</c></summary>
    public const string Frozen = "FROZEN";
    /// <summary><c>CLOSED</c></summary>
    public const string Closed = "CLOSED";
    /// <summary>Every end-user status.</summary>
    public static IReadOnlyList<string> All { get; } = [Active, Frozen, Closed];
}

/// <summary><c>kycLevel</c> of a wallet end user.</summary>
public static class WalletKycLevels
{
    /// <summary><c>NONE</c></summary>
    public const string None = "NONE";
    /// <summary><c>LOW</c></summary>
    public const string Low = "LOW";
    /// <summary><c>SUBSTANTIAL</c></summary>
    public const string Substantial = "SUBSTANTIAL";
    /// <summary><c>HIGH</c></summary>
    public const string High = "HIGH";
    /// <summary>Every KYC level, lowest first.</summary>
    public static IReadOnlyList<string> All { get; } = [None, Low, Substantial, High];
}

/// <summary><c>otpChannel</c> of a two-step wallet operation.</summary>
public static class WalletOtpChannels
{
    /// <summary><c>EMAIL</c></summary>
    public const string Email = "EMAIL";
    /// <summary><c>SMS</c></summary>
    public const string Sms = "SMS";
    /// <summary><c>PUSH</c></summary>
    public const string Push = "PUSH";
    /// <summary>Every OTP channel.</summary>
    public static IReadOnlyList<string> All { get; } = [Email, Sms, Push];
}

/// <summary><c>qrType</c> of a wallet QR code. Static codes carry no amount; dynamic ones require it.</summary>
public static class WalletQrTypes
{
    /// <summary><c>P2P_STATIC</c></summary>
    public const string P2PStatic = "P2P_STATIC";
    /// <summary><c>P2P_DYNAMIC</c></summary>
    public const string P2PDynamic = "P2P_DYNAMIC";
    /// <summary><c>MERCHANT_STATIC</c></summary>
    public const string MerchantStatic = "MERCHANT_STATIC";
    /// <summary><c>MERCHANT_DYNAMIC</c></summary>
    public const string MerchantDynamic = "MERCHANT_DYNAMIC";
    /// <summary>Every QR type.</summary>
    public static IReadOnlyList<string> All { get; } = [P2PStatic, P2PDynamic, MerchantStatic, MerchantDynamic];
}

/// <summary>Image format of <see cref="WalletQrClient.ImageAsync"/>.</summary>
public static class WalletQrImageFormats
{
    /// <summary><c>svg</c> (the default).</summary>
    public const string Svg = "svg";
    /// <summary><c>png</c></summary>
    public const string Png = "png";
    /// <summary>Every image format.</summary>
    public static IReadOnlyList<string> All { get; } = [Svg, Png];
}

/// <summary><c>targetType</c> of a bulk payout item.</summary>
public static class WalletBulkTargetTypes
{
    /// <summary><c>WALLET</c>: <c>targetRef</c> is a wallet number.</summary>
    public const string Wallet = "WALLET";
    /// <summary><c>IBAN</c>: <c>targetRef</c> is an IBAN.</summary>
    public const string Iban = "IBAN";
    /// <summary>Every target type.</summary>
    public static IReadOnlyList<string> All { get; } = [Wallet, Iban];
}

/// <summary><c>status</c> of a corporate account.</summary>
public static class WalletCorporateAccountStatuses
{
    /// <summary><c>ACTIVE</c></summary>
    public const string Active = "ACTIVE";
    /// <summary><c>ARCHIVED</c></summary>
    public const string Archived = "ARCHIVED";
    /// <summary>Every corporate account status.</summary>
    public static IReadOnlyList<string> All { get; } = [Active, Archived];
}

/// <summary>The fields of a nested row or item list the gateway validates element by element.</summary>
/// <param name="Field">The body field holding the list.</param>
/// <param name="Required">Fields each element needs.</param>
/// <param name="Optional">Other fields an element may carry.</param>
public sealed record LunixiNestedFields(string Field, IReadOnlyList<string> Required, IReadOnlyList<string> Optional);

/// <summary>
/// The single description of every wallet route the SDK calls (<c>/api/v1/wallet/*</c>): method, path template, whether the
/// gateway requires an <c>Idempotency-Key</c>, and the body and query fields the gateway DTO accepts (it runs with
/// <c>forbidNonWhitelisted</c>, so an unknown body field would be a 400). <see cref="WalletClient"/> checks every call against
/// it before sending. The table is the Node SDK's <c>WALLET_ROUTES</c>, row for row, and the shared catalog's group of the
/// same name.
/// </summary>
public static class WalletRoutes
{
    /// <summary>Minor units as a digit string: <c>"12550"</c> is 125.50 TRY. Never a JSON number.</summary>
    public const string AmountPattern = @"^\d{1,18}$";
    /// <summary>Longest <c>Idempotency-Key</c> the wallet routes accept.</summary>
    public const int IdempotencyKeyMaxLength = 128;
    /// <summary>Body fields that carry a minor-unit amount string.</summary>
    public static IReadOnlyList<string> AmountFields { get; } = ["amount"];

    private const string Base = "/api/v1/wallet";
    private static readonly string[] Page = ["limit", "cursor"];
    private static readonly string[] IbanRequired = ["endUserId", "sourceWalletAccountId", "beneficiaryName", "iban", "amount", "currency"];
    private static readonly string[] IbanInitiateOptional = ["bankCode", "paymentPurpose", "scheduleVersionId", "otpDestination", "otpChannel", "locale"];
    private static readonly string[] CompleteRequired = ["endUserId", "operationId", "integrityHash", "nonce"];
    private static readonly string[] CompleteOptional = ["otpCode", "trustedActionToken"];

    /// <summary>Nested list rules by route name.</summary>
    public static IReadOnlyDictionary<string, LunixiNestedFields> NestedFields { get; } = new Dictionary<string, LunixiNestedFields>(StringComparer.Ordinal)
    {
        ["importCorporateEmployees"] = new("rows", ["phone"], ["displayName", "externalCustomerId", "kycRef"]),
        ["offboardCorporateEmployees"] = new("rows", [], ["endUserId", "externalCustomerId"]),
        ["createBulkPayout"] = new("items", ["targetType", "targetRef", "amount"], ["beneficiaryName"]),
    };

    /// <summary>Every route by name, in the Node table's order.</summary>
    public static IReadOnlyDictionary<string, LunixiRoute> All { get; } = Build();

    private static LunixiRoute Get(string path) => LunixiRoute.Get(Base + path);
    private static LunixiRoute Post(string path) => LunixiRoute.Post(Base + path);
    private static LunixiRoute Put(string path) => LunixiRoute.Put(Base + path);
    private static string[] Paged(params string[] fields) => [.. fields, .. Page];

    private static IReadOnlyDictionary<string, LunixiRoute> Build()
    {
        var routes = new OrderedRoutes
        {
            ["ping"] = Get("/ping"),

            ["listMyPrograms"] = Get("/programs/me"),
            ["setCollectionAnchor"] = Put("/programs/:programId/collection-anchor").WithOptional("collectionEndUserId", "reason", "expectedVersion"),

            ["createEndUser"] = Post("/end-users").Keyed().WithRequired("programId", "accountType", "phone")
                .WithOptional("externalCustomerId", "kycRef", "kycLevelCache", "displayName", "corporateAccountId", "defaultCurrency"),
            ["listEndUsers"] = Get("/end-users").WithQuery(Paged("programId", "accountType", "status", "corporateAccountId", "merchantCategoryState")),
            ["getEndUserByExternalId"] = Get("/end-users/by-external/:externalCustomerId").WithQuery("programId"),
            ["getEndUser"] = Get("/end-users/:endUserId"),
            ["freezeEndUser"] = Post("/end-users/:endUserId/freeze").Keyed().WithOptional("reason"),
            ["getBalances"] = Get("/end-users/:endUserId/balances"),
            ["getKycLevel"] = Get("/end-users/:endUserId/kyc-level"),
            ["updateKycLevel"] = Put("/end-users/:endUserId/kyc-level").Keyed().WithRequired("kycLevel", "sourceRef")
                .WithOptional("externalCustomerId", "programId", "reason", "effectiveAt", "expectedCurrentLevel"),
            ["getLimits"] = Get("/end-users/:endUserId/limits").WithQuery("operationType", "currency"),
            ["createWallet"] = Post("/end-users/:endUserId/wallets").Keyed().WithOptional("name", "currencies"),

            ["createWalletAccount"] = Post("/wallets/:walletId/accounts").Keyed().WithRequired("currency"),
            ["lookupWallet"] = Post("/wallets/lookup").WithOptional("programId", "phone", "walletNo"),

            ["listDepositInstructions"] = Get("/end-users/:endUserId/deposit-instructions"),
            ["createDepositInstruction"] = Post("/end-users/:endUserId/deposit-instructions").WithRequired("walletAccountId").WithOptional("currency"),
            ["simulateInboundCredit"] = Post("/bank-integrations/inbound-credits").WithRequired("programId", "railId", "amount", "currency", "rawReference")
                .WithOptional("senderName", "senderIban", "creditedIban", "receiptNumber"),

            ["createCorporateAccount"] = Post("/corporate-accounts").Keyed().WithRequired("programId", "code", "name", "ownerEndUserId").WithOptional("fundingCurrency"),
            ["listCorporateAccounts"] = Get("/corporate-accounts").WithQuery(Paged("programId", "status")),
            ["importCorporateEmployees"] = Post("/corporate-accounts/:corporateAccountId/employees/import").Keyed().WithRequired("rows"),
            ["offboardCorporateEmployees"] = Post("/corporate-accounts/:corporateAccountId/employees/offboard").Keyed().WithRequired("rows").WithOptional("reason"),

            ["quoteFees"] = Post("/fees/quote").WithRequired("programId", "operationType", "currency", "amount").WithOptional("at"),

            ["quoteW2W"] = Post("/transfers/w2w/quote").WithRequired("endUserId", "sourceWalletAccountId", "amount", "currency")
                .WithOptional("destinationWalletNo", "destinationPhone", "paymentPurpose"),
            ["initiateW2W"] = Post("/transfers/w2w").Keyed().WithRequired("endUserId", "sourceWalletAccountId", "amount", "currency")
                .WithOptional("destinationWalletNo", "destinationPhone", "paymentPurpose", "scheduleVersionId", "otpDestination", "otpChannel", "locale"),
            ["completeW2W"] = Post("/transfers/w2w/:operationId/complete").Keyed().WithRequired(CompleteRequired).WithOptional(CompleteOptional),

            ["quoteW2Iban"] = Post("/transfers/w2iban/quote").WithRequired(IbanRequired).WithOptional("bankCode", "paymentPurpose"),
            ["initiateW2Iban"] = Post("/transfers/w2iban").Keyed().WithRequired(IbanRequired).WithOptional(IbanInitiateOptional),
            ["completeW2Iban"] = Post("/transfers/w2iban/:operationId/complete").Keyed().WithRequired(CompleteRequired).WithOptional(CompleteOptional),

            ["quoteBankWithdrawal"] = Post("/withdrawals/bank/quote").WithRequired(IbanRequired).WithOptional("bankCode", "paymentPurpose"),
            ["initiateBankWithdrawal"] = Post("/withdrawals/bank").Keyed().WithRequired(IbanRequired).WithOptional(IbanInitiateOptional),
            ["completeBankWithdrawal"] = Post("/withdrawals/bank/:operationId/complete").Keyed().WithRequired(CompleteRequired).WithOptional(CompleteOptional),
            ["getWithdrawal"] = Get("/withdrawals/:operationId"),

            ["listOperations"] = Get("/operations").WithQuery(Paged("endUserId", "walletAccountId", "status", "operationType", "destinationWalletAccountId")),
            ["getOperation"] = Get("/operations/:operationId"),
            ["getOperationReceipt"] = Get("/operations/:operationId/receipt"),

            // The payment provider requires the buyer and the billing address (buyer.ip: the user's browser IP); a missing
            // field is 400 payment.order.field_required with the paths in details.fields.
            ["startCardTopup"] = Post("/topups/card").Keyed().WithRequired("creditWalletAccountId", "amount", "currency", "buyer", "billingAddress")
                .WithOptional("fundingMerchantId", "savedCardUserKey", "savedCardToken", "savedConsumerToken", "savedPublicCardStorageToken", "returnUrl",
                    "shippingAddress"),
            ["getTopup"] = Get("/topups/:operationId"),
            // The gateway DTO marks approverPrincipal optional, but the wallet service refuses the refund without it.
            ["refundTopup"] = Post("/topups/:operationId/refund").WithRequired("reason", "requesterPrincipal", "approverPrincipal"),
            ["agentTopup"] = Post("/agent-topups").Keyed().WithRequired("agentId", "creditWalletAccountId", "amount", "currency"),

            ["requestPayment"] = Post("/payments/buy").Keyed().WithRequired("amount", "currency", "paymentPurpose")
                .WithOptional("buyerWalletNo", "buyerPhone", "orderRef", "cashierRef", "otpDestination", "otpChannel", "locale"),
            ["approvePayment"] = Post("/payments/:operationId/approve").Keyed().WithRequired("buyerEndUserId", "integrityHash", "nonce").WithOptional(CompleteOptional),
            ["collectByCode"] = Post("/collect").Keyed().WithRequired("code", "amount", "currency").WithOptional("orderRef", "paymentPurpose", "locale"),
            ["getPayment"] = Get("/payments/:operationId"),
            ["getBusinessCommission"] = Get("/business-accounts/:businessAccountId/commission"),

            ["createQr"] = Post("/qr").Keyed().WithRequired("qrType", "currency")
                .WithOptional("targetEndUserId", "targetExternalCustomerId", "programId", "amount", "expiresInSeconds", "metadata"),
            ["listQr"] = Get("/qr").WithQuery(Paged("programId", "qrType", "status", "targetEndUserId")),
            ["getQr"] = Get("/qr/:qrId"),
            ["getQrImage"] = Get("/qr/:qrId/image").AsBinary().WithQuery("format", "scale"),
            ["revokeQr"] = Post("/qr/:qrId/revoke"),
            ["parseQr"] = Post("/qr/parse").WithRequired("payload"),

            ["createBulkPayout"] = Post("/bulk-payouts").Keyed().WithRequired("programId", "sourceWalletAccountId", "currency", "items").WithOptional("reason"),
            ["listBulkPayouts"] = Get("/bulk-payouts").WithQuery("programId", "status", "limit"),
            ["getBulkPayout"] = Get("/bulk-payouts/:batchId"),
            ["listBulkPayoutItems"] = Get("/bulk-payouts/:batchId/items").WithQuery("status", "limit"),
            ["submitBulkPayout"] = Post("/bulk-payouts/:batchId/submit"),
            ["approveBulkPayout"] = Post("/bulk-payouts/:batchId/approve").WithRequired("approve").WithOptional("reason"),
            ["rejectBulkPayout"] = Post("/bulk-payouts/:batchId/reject").WithOptional("reason"),
            ["processBulkPayout"] = Post("/bulk-payouts/:batchId/process"),
            ["retryFailedBulkPayout"] = Post("/bulk-payouts/:batchId/retry-failed"),
        };
        return routes.Freeze();
    }
}
