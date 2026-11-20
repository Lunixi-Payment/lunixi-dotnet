using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Lunixi;

/// <summary>
/// Wallet (<c>/api/v1/wallet/*</c>), the merchant server-to-server surface of the closed-loop wallet. Every call is signed,
/// reads included; the merchant, its programs and its end users come from the signing key. The key needs the
/// <c>WALLET_SERVICE</c> product (403 <c>PRODUCT_NOT_ENABLED</c>) and the wallet permission behind the route (403
/// <c>WALLET_INSUFFICIENT_SCOPE</c>).
/// <para>
/// Calls are checked against <see cref="WalletRoutes"/> before anything is sent: an unknown body field or filter, a missing
/// required field, an amount that is not a minor-unit digit string, or a missing <c>Idempotency-Key</c> on a route that
/// requires one is a <see cref="LunixiConfigurationException"/>. Amounts are minor units as strings (<c>"12550"</c> is 125.50
/// TRY). A call that moves money for an end user names that end user (<c>endUserId</c>, <c>buyerEndUserId</c>). Methods
/// return the envelope's <c>data</c>; lists return <see cref="LunixiPage"/>.
/// </para>
/// </summary>
public sealed class WalletClient
{
    private readonly WalletInvoker invoker;
    internal WalletClient(LunixiClient client)
    {
        invoker = new WalletInvoker(client);
        Programs = new WalletProgramsClient(invoker); EndUsers = new WalletEndUsersClient(invoker);
        Wallets = new WalletAccountsClient(invoker); Deposits = new WalletDepositsClient(invoker);
        Corporate = new WalletCorporateClient(invoker); Fees = new WalletFeesClient(invoker);
        Transfers = new WalletTransfersClient(invoker); Withdrawals = new WalletWithdrawalsClient(invoker);
        Operations = new WalletOperationsClient(invoker); Topups = new WalletTopupsClient(invoker);
        Payments = new WalletPaymentsClient(invoker); Qr = new WalletQrClient(invoker); BulkPayouts = new WalletBulkPayoutsClient(invoker);
    }

    /// <summary>Wallet programs of the signing merchant.</summary>
    public WalletProgramsClient Programs { get; }
    /// <summary>End users, their balances, limits, KYC level and wallets.</summary>
    public WalletEndUsersClient EndUsers { get; }
    /// <summary>Wallet accounts and wallet lookup.</summary>
    public WalletAccountsClient Wallets { get; }
    /// <summary>Top-up by bank transfer.</summary>
    public WalletDepositsClient Deposits { get; }
    /// <summary>Corporate accounts and their employees.</summary>
    public WalletCorporateClient Corporate { get; }
    /// <summary>Fee quotes.</summary>
    public WalletFeesClient Fees { get; }
    /// <summary>Wallet-to-wallet and wallet-to-IBAN transfers.</summary>
    public WalletTransfersClient Transfers { get; }
    /// <summary>Withdrawals to the end user's own bank account.</summary>
    public WalletWithdrawalsClient Withdrawals { get; }
    /// <summary>Operation history and receipts.</summary>
    public WalletOperationsClient Operations { get; }
    /// <summary>Card and agent top-ups.</summary>
    public WalletTopupsClient Topups { get; }
    /// <summary>Payments to the merchant.</summary>
    public WalletPaymentsClient Payments { get; }
    /// <summary>QR codes.</summary>
    public WalletQrClient Qr { get; }
    /// <summary>Bulk payouts (maker-checker).</summary>
    public WalletBulkPayoutsClient BulkPayouts { get; }

    /// <summary>Checks the key reaches the wallet (signature, product and a wallet management permission).</summary>
    public async Task<JsonElement> PingAsync(CancellationToken cancellationToken = default)
        => (await invoker.SendAsync("ping", null, null, null, null, cancellationToken).ConfigureAwait(false)).Data;
}

/// <summary>Wallet programs (<c>/programs</c>).</summary>
public sealed class WalletProgramsClient
{
    private readonly WalletInvoker invoker;
    internal WalletProgramsClient(WalletInvoker invoker) => this.invoker = invoker;

    /// <summary>The wallet programs of the signing merchant.</summary>
    public Task<LunixiPage> ListMineAsync(CancellationToken cancellationToken = default)
        => invoker.PageAsync("listMyPrograms", null, null, cancellationToken);

    /// <summary>
    /// Sets the end user whose wallet collects the program's merchant payments: <c>{ collectionEndUserId?, reason?,
    /// expectedVersion? }</c>. Leaving <c>collectionEndUserId</c> out clears the anchor.
    /// </summary>
    public Task<JsonElement> SetCollectionAnchorAsync(string programId, object? request = null, CancellationToken cancellationToken = default)
        => invoker.DataAsync("setCollectionAnchor", WalletInvoker.Ids(("programId", programId)), request, null, null, cancellationToken);
}

/// <summary>Wallet end users (<c>/end-users</c>).</summary>
public sealed class WalletEndUsersClient
{
    private readonly WalletInvoker invoker;
    internal WalletEndUsersClient(WalletInvoker invoker) => this.invoker = invoker;

    /// <summary>
    /// Creates an end user and their default wallet: <c>{ programId, accountType, phone, externalCustomerId?, kycRef?,
    /// kycLevelCache?, displayName?, corporateAccountId?, defaultCurrency? }</c>.
    /// </summary>
    public Task<JsonElement> CreateAsync(object request, string idempotencyKey, CancellationToken cancellationToken = default)
        => invoker.DataAsync("createEndUser", null, request, null, idempotencyKey, cancellationToken);

    /// <summary>End users; filters <c>programId</c>, <c>accountType</c>, <c>status</c>, <c>corporateAccountId</c>, <c>merchantCategoryState</c>, <c>limit</c>, <c>cursor</c>.</summary>
    public Task<LunixiPage> ListAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => invoker.PageAsync("listEndUsers", null, filters, cancellationToken);

    /// <summary>Every end user of <see cref="ListAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => RouteRequest.IterateAsync(ListAsync, filters, cancellationToken);

    /// <summary>One end user.</summary>
    public Task<JsonElement> GetAsync(string endUserId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("getEndUser", WalletInvoker.Ids(("endUserId", endUserId)), null, null, null, cancellationToken);

    /// <summary>Finds an end user by your own customer id, optionally within one program.</summary>
    public Task<JsonElement> GetByExternalIdAsync(string externalCustomerId, string? programId = null, CancellationToken cancellationToken = default)
        => invoker.DataAsync("getEndUserByExternalId", WalletInvoker.Ids(("externalCustomerId", externalCustomerId)), null,
            WalletInvoker.Filters(("programId", programId)), null, cancellationToken);

    /// <summary>Freezes an end user.</summary>
    public Task<JsonElement> FreezeAsync(string endUserId, string idempotencyKey, string? reason = null, CancellationToken cancellationToken = default)
        => invoker.DataAsync("freezeEndUser", WalletInvoker.Ids(("endUserId", endUserId)), WalletInvoker.Body(("reason", reason)), null, idempotencyKey, cancellationToken);

    /// <summary>The end user's balances.</summary>
    public Task<JsonElement> GetBalancesAsync(string endUserId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("getBalances", WalletInvoker.Ids(("endUserId", endUserId)), null, null, null, cancellationToken);

    /// <summary>The end user's KYC level.</summary>
    public Task<JsonElement> GetKycLevelAsync(string endUserId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("getKycLevel", WalletInvoker.Ids(("endUserId", endUserId)), null, null, null, cancellationToken);

    /// <summary>
    /// Records the KYC level from your own verification: <c>{ kycLevel, sourceRef, externalCustomerId?, programId?, reason?,
    /// effectiveAt?, expectedCurrentLevel? }</c> (<c>sourceRef</c> is your KYC session id).
    /// </summary>
    public Task<JsonElement> UpdateKycLevelAsync(string endUserId, object request, string idempotencyKey, CancellationToken cancellationToken = default)
        => invoker.DataAsync("updateKycLevel", WalletInvoker.Ids(("endUserId", endUserId)), request, null, idempotencyKey, cancellationToken);

    /// <summary>Limit usage per operation type; <paramref name="operationType"/> and <paramref name="currency"/> narrow the answer.</summary>
    public Task<JsonElement> GetLimitsAsync(string endUserId, string? operationType = null, string? currency = null, CancellationToken cancellationToken = default)
        => invoker.DataAsync("getLimits", WalletInvoker.Ids(("endUserId", endUserId)), null,
            WalletInvoker.Filters(("operationType", operationType), ("currency", currency)), null, cancellationToken);

    /// <summary>Creates another wallet for the end user, optionally named and with its currencies.</summary>
    public Task<JsonElement> CreateWalletAsync(string endUserId, string idempotencyKey, string? name = null, IReadOnlyList<string>? currencies = null,
        CancellationToken cancellationToken = default)
        => invoker.DataAsync("createWallet", WalletInvoker.Ids(("endUserId", endUserId)), WalletInvoker.Body(("name", name), ("currencies", currencies)), null,
            idempotencyKey, cancellationToken);
}

/// <summary>Wallets and their currency accounts (<c>/wallets</c>).</summary>
public sealed class WalletAccountsClient
{
    private readonly WalletInvoker invoker;
    internal WalletAccountsClient(WalletInvoker invoker) => this.invoker = invoker;

    /// <summary>Adds a currency account to a wallet.</summary>
    public Task<JsonElement> CreateAccountAsync(string walletId, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
        => invoker.DataAsync("createWalletAccount", WalletInvoker.Ids(("walletId", walletId)), WalletInvoker.Body(("currency", currency)), null, idempotencyKey, cancellationToken);

    /// <summary>Resolves a wallet number or phone to <c>{ exists, walletNo, maskedName }</c> before a transfer: <c>{ programId?, phone?, walletNo? }</c>.</summary>
    public Task<JsonElement> LookupAsync(object request, CancellationToken cancellationToken = default)
        => invoker.DataAsync("lookupWallet", null, request, null, null, cancellationToken);
}

/// <summary>Top-up by bank transfer (<c>/deposit-instructions</c>).</summary>
public sealed class WalletDepositsClient
{
    private readonly WalletInvoker invoker;
    internal WalletDepositsClient(WalletInvoker invoker) => this.invoker = invoker;

    /// <summary>The bank transfer details the end user pays into to top up.</summary>
    public Task<JsonElement> ListInstructionsAsync(string endUserId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("listDepositInstructions", WalletInvoker.Ids(("endUserId", endUserId)), null, null, null, cancellationToken);

    /// <summary>Creates a deposit instruction for one of the end user's accounts.</summary>
    public Task<JsonElement> CreateInstructionAsync(string endUserId, string walletAccountId, string? currency = null, CancellationToken cancellationToken = default)
        => invoker.DataAsync("createDepositInstruction", WalletInvoker.Ids(("endUserId", endUserId)),
            WalletInvoker.Body(("walletAccountId", walletAccountId), ("currency", currency)), null, null, cancellationToken);

    /// <summary>
    /// Test environment only: books an incoming bank transfer as the bank rail would: <c>{ programId, railId, amount, currency,
    /// rawReference, senderName?, senderIban?, creditedIban?, receiptNumber? }</c>.
    /// </summary>
    public Task<JsonElement> SimulateInboundCreditAsync(object request, CancellationToken cancellationToken = default)
        => invoker.DataAsync("simulateInboundCredit", null, request, null, null, cancellationToken);
}

/// <summary>Corporate accounts (<c>/corporate-accounts</c>).</summary>
public sealed class WalletCorporateClient
{
    private readonly WalletInvoker invoker;
    internal WalletCorporateClient(WalletInvoker invoker) => this.invoker = invoker;

    /// <summary>Creates a corporate account: <c>{ programId, code, name, ownerEndUserId, fundingCurrency? }</c>.</summary>
    public Task<JsonElement> CreateAsync(object request, string idempotencyKey, CancellationToken cancellationToken = default)
        => invoker.DataAsync("createCorporateAccount", null, request, null, idempotencyKey, cancellationToken);

    /// <summary>Corporate accounts; filters <c>programId</c>, <c>status</c>, <c>limit</c>, <c>cursor</c>.</summary>
    public Task<LunixiPage> ListAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => invoker.PageAsync("listCorporateAccounts", null, filters, cancellationToken);

    /// <summary>Every corporate account of <see cref="ListAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => RouteRequest.IterateAsync(ListAsync, filters, cancellationToken);

    /// <summary>Imports up to 1000 employees at once (<c>{ phone, displayName?, externalCustomerId?, kycRef? }</c> rows); all or nothing.</summary>
    public Task<JsonElement> ImportEmployeesAsync(string corporateAccountId, IReadOnlyList<object> rows, string idempotencyKey, CancellationToken cancellationToken = default)
        => invoker.DataAsync("importCorporateEmployees", WalletInvoker.Ids(("corporateAccountId", corporateAccountId)), WalletInvoker.Body(("rows", rows)), null,
            idempotencyKey, cancellationToken);

    /// <summary>Offboards employees (<c>{ endUserId?, externalCustomerId? }</c> rows).</summary>
    public Task<JsonElement> OffboardEmployeesAsync(string corporateAccountId, IReadOnlyList<object> rows, string idempotencyKey, string? reason = null,
        CancellationToken cancellationToken = default)
        => invoker.DataAsync("offboardCorporateEmployees", WalletInvoker.Ids(("corporateAccountId", corporateAccountId)),
            WalletInvoker.Body(("rows", rows), ("reason", reason)), null, idempotencyKey, cancellationToken);
}

/// <summary>Fee quotes (<c>/fees</c>).</summary>
public sealed class WalletFeesClient
{
    private readonly WalletInvoker invoker;
    internal WalletFeesClient(WalletInvoker invoker) => this.invoker = invoker;

    /// <summary>The fee, tax and total charge an operation would cost: <c>{ programId, operationType, currency, amount, at? }</c>.</summary>
    public Task<JsonElement> QuoteAsync(object request, CancellationToken cancellationToken = default)
        => invoker.DataAsync("quoteFees", null, request, null, null, cancellationToken);
}

/// <summary>
/// Wallet-to-wallet and wallet-to-IBAN transfers (<c>/transfers</c>). Money moves in two steps: <c>Initiate…</c> reserves the
/// limit and returns <c>{ operationId, integrityHash, nonce, otpRequired, … }</c>; <c>Complete…</c> with the same
/// <c>integrityHash</c> and <c>nonce</c> (and the OTP when <c>otpRequired</c>) commits it. Quote first to show the fee and
/// pass its <c>scheduleVersionId</c> to <c>Initiate…</c> to hold that fee. The complete calls put <c>operationId</c> into the
/// body themselves.
/// </summary>
public sealed class WalletTransfersClient
{
    private readonly WalletInvoker invoker;
    internal WalletTransfersClient(WalletInvoker invoker) => this.invoker = invoker;

    /// <summary>Quotes a wallet-to-wallet transfer: <c>{ endUserId, sourceWalletAccountId, amount, currency, destinationWalletNo?, destinationPhone?, paymentPurpose? }</c>.</summary>
    public Task<JsonElement> QuoteW2WAsync(object request, CancellationToken cancellationToken = default)
        => invoker.DataAsync("quoteW2W", null, request, null, null, cancellationToken);

    /// <summary>Starts a wallet-to-wallet transfer (the quote fields plus <c>scheduleVersionId?</c>, <c>otpDestination?</c>, <c>otpChannel?</c>, <c>locale?</c>).</summary>
    public Task<JsonElement> InitiateW2WAsync(object request, string idempotencyKey, CancellationToken cancellationToken = default)
        => invoker.DataAsync("initiateW2W", null, request, null, idempotencyKey, cancellationToken);

    /// <summary>Completes a wallet-to-wallet transfer: <c>{ endUserId, integrityHash, nonce, otpCode?, trustedActionToken? }</c>.</summary>
    public Task<JsonElement> CompleteW2WAsync(string operationId, object request, string idempotencyKey, CancellationToken cancellationToken = default)
        => invoker.CompleteAsync("completeW2W", operationId, request, idempotencyKey, cancellationToken);

    /// <summary>Quotes a transfer to an IBAN: <c>{ endUserId, sourceWalletAccountId, beneficiaryName, iban, amount, currency, bankCode?, paymentPurpose? }</c>.</summary>
    public Task<JsonElement> QuoteW2IbanAsync(object request, CancellationToken cancellationToken = default)
        => invoker.DataAsync("quoteW2Iban", null, request, null, null, cancellationToken);

    /// <summary>Starts a transfer to an IBAN (the quote fields plus <c>scheduleVersionId?</c>, <c>otpDestination?</c>, <c>otpChannel?</c>, <c>locale?</c>).</summary>
    public Task<JsonElement> InitiateW2IbanAsync(object request, string idempotencyKey, CancellationToken cancellationToken = default)
        => invoker.DataAsync("initiateW2Iban", null, request, null, idempotencyKey, cancellationToken);

    /// <summary>Completes a transfer to an IBAN: <c>{ endUserId, integrityHash, nonce, otpCode?, trustedActionToken? }</c>.</summary>
    public Task<JsonElement> CompleteW2IbanAsync(string operationId, object request, string idempotencyKey, CancellationToken cancellationToken = default)
        => invoker.CompleteAsync("completeW2Iban", operationId, request, idempotencyKey, cancellationToken);
}

/// <summary>Withdrawals to the end user's own bank account (<c>/withdrawals</c>), in the same two steps as transfers.</summary>
public sealed class WalletWithdrawalsClient
{
    private readonly WalletInvoker invoker;
    internal WalletWithdrawalsClient(WalletInvoker invoker) => this.invoker = invoker;

    /// <summary>Quotes a withdrawal: <c>{ endUserId, sourceWalletAccountId, beneficiaryName, iban, amount, currency, bankCode?, paymentPurpose? }</c>.</summary>
    public Task<JsonElement> QuoteAsync(object request, CancellationToken cancellationToken = default)
        => invoker.DataAsync("quoteBankWithdrawal", null, request, null, null, cancellationToken);

    /// <summary>Starts a withdrawal (the quote fields plus <c>scheduleVersionId?</c>, <c>otpDestination?</c>, <c>otpChannel?</c>, <c>locale?</c>).</summary>
    public Task<JsonElement> InitiateAsync(object request, string idempotencyKey, CancellationToken cancellationToken = default)
        => invoker.DataAsync("initiateBankWithdrawal", null, request, null, idempotencyKey, cancellationToken);

    /// <summary>Completes a withdrawal: <c>{ endUserId, integrityHash, nonce, otpCode?, trustedActionToken? }</c>.</summary>
    public Task<JsonElement> CompleteAsync(string operationId, object request, string idempotencyKey, CancellationToken cancellationToken = default)
        => invoker.CompleteAsync("completeBankWithdrawal", operationId, request, idempotencyKey, cancellationToken);

    /// <summary><c>{ operation, history }</c> of a withdrawal.</summary>
    public Task<JsonElement> GetAsync(string operationId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("getWithdrawal", WalletInvoker.Ids(("operationId", operationId)), null, null, null, cancellationToken);
}

/// <summary>Wallet operations (<c>/operations</c>).</summary>
public sealed class WalletOperationsClient
{
    private readonly WalletInvoker invoker;
    internal WalletOperationsClient(WalletInvoker invoker) => this.invoker = invoker;

    /// <summary>Operations; filters <c>endUserId</c>, <c>walletAccountId</c>, <c>status</c>, <c>operationType</c>, <c>destinationWalletAccountId</c>, <c>limit</c>, <c>cursor</c>.</summary>
    public Task<LunixiPage> ListAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => invoker.PageAsync("listOperations", null, filters, cancellationToken);

    /// <summary>Every operation of <see cref="ListAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => RouteRequest.IterateAsync(ListAsync, filters, cancellationToken);

    /// <summary><c>{ operation, history }</c> of one operation.</summary>
    public Task<JsonElement> GetAsync(string operationId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("getOperation", WalletInvoker.Ids(("operationId", operationId)), null, null, null, cancellationToken);

    /// <summary>The receipt of one operation.</summary>
    public Task<JsonElement> GetReceiptAsync(string operationId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("getOperationReceipt", WalletInvoker.Ids(("operationId", operationId)), null, null, null, cancellationToken);
}

/// <summary>Top-ups (<c>/topups</c>, <c>/agent-topups</c>).</summary>
public sealed class WalletTopupsClient
{
    private readonly WalletInvoker invoker;
    internal WalletTopupsClient(WalletInvoker invoker) => this.invoker = invoker;

    /// <summary>
    /// Starts a card top-up: <c>{ creditWalletAccountId, amount, currency, buyer, billingAddress, shippingAddress?,
    /// fundingMerchantId?, savedCardUserKey?, savedCardToken?, savedConsumerToken?, savedPublicCardStorageToken?, returnUrl? }</c>.
    /// The buyer (<c>ip</c>: the user's browser IP) and the billing address are required and checked before sending
    /// (<see cref="OrderDetails"/>, no basket). Render <c>checkoutFormContent</c> (or follow <c>redirectUrl</c> /
    /// <c>threeDsHtml</c>); the wallet is credited when the payment completes.
    /// </summary>
    public Task<JsonElement> StartCardAsync(object request, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = RouteRequest.ObjectOf(request, "startCardTopup body");
        OrderDetails.Require(body, "A wallet card top-up", basket: false);
        return invoker.DataAsync("startCardTopup", null, body, null, idempotencyKey, cancellationToken);
    }

    /// <summary><c>{ operation, history }</c> of a top-up.</summary>
    public Task<JsonElement> GetAsync(string operationId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("getTopup", WalletInvoker.Ids(("operationId", operationId)), null, null, null, cancellationToken);

    /// <summary>
    /// Refunds a card top-up to the card: <c>{ reason, requesterPrincipal, approverPrincipal }</c>. Four eyes: the approver must
    /// differ from the requester; binding them to two real people is your approval flow's job.
    /// </summary>
    public Task<JsonElement> RefundAsync(string operationId, object request, CancellationToken cancellationToken = default)
        => invoker.DataAsync("refundTopup", WalletInvoker.Ids(("operationId", operationId)), request, null, null, cancellationToken);

    /// <summary>Cash top-up through an agent of the program: <c>{ agentId, creditWalletAccountId, amount, currency }</c>.</summary>
    public Task<JsonElement> AgentAsync(object request, string idempotencyKey, CancellationToken cancellationToken = default)
        => invoker.DataAsync("agentTopup", null, request, null, idempotencyKey, cancellationToken);
}

/// <summary>
/// Payments to the merchant (<c>/payments</c>, <c>/collect</c>). The creditor is the wallet terminal bound to the signing key;
/// a <c>businessAccountId</c> in the body is refused.
/// </summary>
public sealed class WalletPaymentsClient
{
    private readonly WalletInvoker invoker;
    internal WalletPaymentsClient(WalletInvoker invoker) => this.invoker = invoker;

    /// <summary>
    /// Asks the buyer (by wallet number or phone) to pay: <c>{ amount, currency, paymentPurpose, buyerWalletNo?, buyerPhone?,
    /// orderRef?, cashierRef?, otpDestination?, otpChannel?, locale? }</c>; approve with <see cref="ApproveAsync"/>.
    /// </summary>
    public Task<JsonElement> RequestAsync(object request, string idempotencyKey, CancellationToken cancellationToken = default)
        => invoker.DataAsync("requestPayment", null, request, null, idempotencyKey, cancellationToken);

    /// <summary>Approves a payment request: <c>{ buyerEndUserId, integrityHash, nonce, otpCode?, trustedActionToken? }</c>.</summary>
    public Task<JsonElement> ApproveAsync(string operationId, object request, string idempotencyKey, CancellationToken cancellationToken = default)
        => invoker.DataAsync("approvePayment", WalletInvoker.Ids(("operationId", operationId)), request, null, idempotencyKey, cancellationToken);

    /// <summary>Collects with the payment code the buyer shows: <c>{ code, amount, currency, orderRef?, paymentPurpose?, locale? }</c>.</summary>
    public Task<JsonElement> CollectAsync(object request, string idempotencyKey, CancellationToken cancellationToken = default)
        => invoker.DataAsync("collectByCode", null, request, null, idempotencyKey, cancellationToken);

    /// <summary><c>{ operation, history }</c> of a payment.</summary>
    public Task<JsonElement> GetAsync(string operationId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("getPayment", WalletInvoker.Ids(("operationId", operationId)), null, null, null, cancellationToken);

    /// <summary>The commission of a business account.</summary>
    public Task<JsonElement> GetCommissionAsync(string businessAccountId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("getBusinessCommission", WalletInvoker.Ids(("businessAccountId", businessAccountId)), null, null, null, cancellationToken);
}

/// <summary>Wallet QR codes (<c>/qr</c>).</summary>
public sealed class WalletQrClient
{
    private const string ImageAccept = "image/svg+xml, image/png, application/json";
    private readonly WalletInvoker invoker;
    internal WalletQrClient(WalletInvoker invoker) => this.invoker = invoker;

    /// <summary>
    /// Creates a QR code: <c>{ qrType, currency, targetEndUserId?, targetExternalCustomerId?, programId?, amount?,
    /// expiresInSeconds?, metadata? }</c>. Static codes carry no amount; dynamic ones require it.
    /// </summary>
    public Task<JsonElement> CreateAsync(object request, string idempotencyKey, CancellationToken cancellationToken = default)
        => invoker.DataAsync("createQr", null, request, null, idempotencyKey, cancellationToken);

    /// <summary>QR codes; filters <c>programId</c>, <c>qrType</c>, <c>status</c>, <c>targetEndUserId</c>, <c>limit</c>, <c>cursor</c>.</summary>
    public Task<LunixiPage> ListAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => invoker.PageAsync("listQr", null, filters, cancellationToken);

    /// <summary>Every QR code of <see cref="ListAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => RouteRequest.IterateAsync(ListAsync, filters, cancellationToken);

    /// <summary>One QR code.</summary>
    public Task<JsonElement> GetAsync(string qrId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("getQr", WalletInvoker.Ids(("qrId", qrId)), null, null, null, cancellationToken);

    /// <summary>
    /// The QR code as an image: <paramref name="format"/> is <see cref="WalletQrImageFormats.Svg"/> (the gateway default) or
    /// <see cref="WalletQrImageFormats.Png"/>, <paramref name="scale"/> 1–20. Errors are still read as the JSON envelope.
    /// </summary>
    public Task<LunixiBinaryResponse> ImageAsync(string qrId, string? format = null, int? scale = null, CancellationToken cancellationToken = default)
    {
        if (format is not null && !WalletQrImageFormats.All.Contains(format))
            throw new LunixiConfigurationException($"QR image format must be one of {string.Join(", ", WalletQrImageFormats.All)}.");
        if (scale is < 1 or > 20) throw new LunixiConfigurationException("QR image scale must be an integer 1-20.");
        return invoker.BinaryAsync("getQrImage", WalletInvoker.Ids(("qrId", qrId)), WalletInvoker.Filters(("format", format), ("scale", scale)), ImageAccept, cancellationToken);
    }

    /// <summary>Revokes a QR code.</summary>
    public Task<JsonElement> RevokeAsync(string qrId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("revokeQr", WalletInvoker.Ids(("qrId", qrId)), null, null, null, cancellationToken);

    /// <summary>Decodes a scanned QR payload.</summary>
    public Task<JsonElement> ParseAsync(string payload, CancellationToken cancellationToken = default)
        => invoker.DataAsync("parseQr", null, WalletInvoker.Body(("payload", payload)), null, null, cancellationToken);
}

/// <summary>
/// Bulk payouts (<c>/bulk-payouts</c>), maker-checker: the approving key must differ from the creating key (the principal is
/// the signing key id). Creating needs <c>wallet:bulk:create</c>; submit, approve, process and retry-failed need
/// <c>wallet:bulk:approve</c>.
/// </summary>
public sealed class WalletBulkPayoutsClient
{
    private readonly WalletInvoker invoker;
    internal WalletBulkPayoutsClient(WalletInvoker invoker) => this.invoker = invoker;

    /// <summary>
    /// Creates a batch: <c>{ programId, sourceWalletAccountId, currency, items, reason? }</c>; each item is <c>{ targetType,
    /// targetRef, amount, beneficiaryName? }</c>.
    /// </summary>
    public Task<JsonElement> CreateAsync(object request, string idempotencyKey, CancellationToken cancellationToken = default)
        => invoker.DataAsync("createBulkPayout", null, request, null, idempotencyKey, cancellationToken);

    /// <summary>Batches; filters <c>programId</c>, <c>status</c>, <c>limit</c>.</summary>
    public Task<LunixiPage> ListAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => invoker.PageAsync("listBulkPayouts", null, filters, cancellationToken);

    /// <summary>One batch.</summary>
    public Task<JsonElement> GetAsync(string batchId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("getBulkPayout", WalletInvoker.Ids(("batchId", batchId)), null, null, null, cancellationToken);

    /// <summary>The items of a batch; filters <c>status</c>, <c>limit</c>.</summary>
    public Task<LunixiPage> ListItemsAsync(string batchId, IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => invoker.PageAsync("listBulkPayoutItems", WalletInvoker.Ids(("batchId", batchId)), filters, cancellationToken);

    /// <summary>Submits a batch for approval.</summary>
    public Task<JsonElement> SubmitAsync(string batchId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("submitBulkPayout", WalletInvoker.Ids(("batchId", batchId)), null, null, null, cancellationToken);

    /// <summary>Approves a batch; <paramref name="approve"/> false declines it with <paramref name="reason"/>.</summary>
    public Task<JsonElement> ApproveAsync(string batchId, bool approve = true, string? reason = null, CancellationToken cancellationToken = default)
        => invoker.DataAsync("approveBulkPayout", WalletInvoker.Ids(("batchId", batchId)), WalletInvoker.Body(("approve", approve), ("reason", reason)), null, null,
            cancellationToken);

    /// <summary>Rejects a batch.</summary>
    public Task<JsonElement> RejectAsync(string batchId, string? reason = null, CancellationToken cancellationToken = default)
        => invoker.DataAsync("rejectBulkPayout", WalletInvoker.Ids(("batchId", batchId)), WalletInvoker.Body(("reason", reason)), null, null, cancellationToken);

    /// <summary>Starts paying out an approved batch.</summary>
    public Task<JsonElement> ProcessAsync(string batchId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("processBulkPayout", WalletInvoker.Ids(("batchId", batchId)), null, null, null, cancellationToken);

    /// <summary>Retries the failed items of a batch.</summary>
    public Task<JsonElement> RetryFailedAsync(string batchId, CancellationToken cancellationToken = default)
        => invoker.DataAsync("retryFailedBulkPayout", WalletInvoker.Ids(("batchId", batchId)), null, null, null, cancellationToken);
}

/// <summary>
/// Sends one route of <see cref="WalletRoutes"/>, signed. The body and query are checked against the route before anything
/// leaves the process.
/// </summary>
internal sealed class WalletInvoker
{
    private static readonly Regex Amount = new(WalletRoutes.AmountPattern, RegexOptions.CultureInvariant);
    private readonly LunixiClient client;
    internal WalletInvoker(LunixiClient client) => this.client = client;

    internal static IReadOnlyDictionary<string, string?> Ids(params (string Name, string? Value)[] values)
        => values.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);

    internal static IReadOnlyDictionary<string, object?> Filters(params (string Name, object? Value)[] values)
        => values.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);

    internal static Dictionary<string, object?> Body(params (string Name, object? Value)[] values)
        => values.Where(value => value.Value is not null).ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);

    internal async Task<JsonElement> DataAsync(string routeName, IReadOnlyDictionary<string, string?>? ids, object? body,
        IReadOnlyDictionary<string, object?>? query, string? idempotencyKey, CancellationToken cancellationToken)
        => (await SendAsync(routeName, ids, body, query, idempotencyKey, cancellationToken).ConfigureAwait(false)).Data;

    internal async Task<LunixiPage> PageAsync(string routeName, IReadOnlyDictionary<string, string?>? ids, IReadOnlyDictionary<string, object?>? filters,
        CancellationToken cancellationToken)
        => new(await SendAsync(routeName, ids, null, filters, null, cancellationToken).ConfigureAwait(false));

    /// <summary>The complete DTOs require <c>operationId</c> in the body as well; it must match the path.</summary>
    internal Task<JsonElement> CompleteAsync(string routeName, string operationId, object request, string idempotencyKey, CancellationToken cancellationToken)
    {
        var id = RouteRequest.Id(operationId, "operationId");
        var body = RouteRequest.ObjectOf(request, routeName + " body");
        if (body["operationId"] is { } given && RouteRequest.Text(given) != id)
            throw new LunixiConfigurationException("operationId in the body must match the operation being completed.");
        body["operationId"] = id;
        return DataAsync(routeName, Ids(("operationId", id)), body, null, idempotencyKey, cancellationToken);
    }

    internal Task<LunixiResponse> SendAsync(string routeName, IReadOnlyDictionary<string, string?>? ids, object? body,
        IReadOnlyDictionary<string, object?>? query, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var route = WalletRoutes.All[routeName];
        var path = RouteRequest.FillPath(route, ids) + RouteRequest.Query(query, route.Query, routeName);
        var payload = Payload(routeName, route, body);
        var key = RouteRequest.Key(route, idempotencyKey, routeName);
        return client.SendCoreAsync(route.HttpMethod, path, payload, key, true, WalletRoutes.IdempotencyKeyMaxLength, cancellationToken);
    }

    internal Task<LunixiBinaryResponse> BinaryAsync(string routeName, IReadOnlyDictionary<string, string?> ids, IReadOnlyDictionary<string, object?> query,
        string accept, CancellationToken cancellationToken)
    {
        var route = WalletRoutes.All[routeName];
        var path = RouteRequest.FillPath(route, ids) + RouteRequest.Query(query, route.Query, routeName);
        return client.SendBinaryAsync(path, accept, cancellationToken);
    }

    /// <summary>The checked body of a route with body fields; a body for any other route is an error.</summary>
    private static JsonObject? Payload(string routeName, LunixiRoute route, object? body)
    {
        if (route.Method == "GET" || !route.HasBody)
        {
            if (body is not null) throw new LunixiConfigurationException($"{routeName} takes no body.");
            return null;
        }
        var plain = RouteRequest.Fields(RouteRequest.ObjectOf(body, routeName + " body"), route.Required, route.Optional, route.AnyOf, routeName);
        foreach (var field in WalletRoutes.AmountFields)
            if (plain.ContainsKey(field)) CheckAmount(plain[field], $"{routeName}.{field}");
        if (WalletRoutes.NestedFields.TryGetValue(routeName, out var nested))
            plain[nested.Field] = Rows(plain[nested.Field], nested, $"{routeName}.{nested.Field}");
        return plain;
    }

    private static JsonArray Rows(JsonNode? value, LunixiNestedFields spec, string label)
    {
        if (value is not JsonArray rows || rows.Count == 0) throw new LunixiConfigurationException($"{label} must be a non-empty array.");
        var checkedRows = new JsonArray();
        for (var index = 0; index < rows.Count; index++)
        {
            var rowLabel = $"{label}[{index}]";
            if (rows[index] is not JsonObject row) throw new LunixiConfigurationException($"{rowLabel} must be an object.");
            var plain = RouteRequest.Fields(RouteRequest.Compact((JsonObject)row.DeepClone()), spec.Required, spec.Optional, [], rowLabel);
            if (plain.ContainsKey("amount")) CheckAmount(plain["amount"], rowLabel + ".amount");
            checkedRows.Add(plain);
        }
        return checkedRows;
    }

    /// <summary>Minor units only: a digit string of at most 18 digits, positive and canonical. A number or decimal is refused, never rounded.</summary>
    private static void CheckAmount(JsonNode? value, string label)
    {
        var text = RouteRequest.Text(value);
        var valid = text is not null && Amount.IsMatch(text);
        if (valid)
        {
            try { Money.Parse(text!, positive: true); }
            catch (LunixiConfigurationException) { valid = false; }
        }
        if (!valid)
            throw new LunixiConfigurationException($"{label} must be minor units as a digit string (for example \"12550\" for 125.50), not a JSON number or a decimal.");
    }
}
