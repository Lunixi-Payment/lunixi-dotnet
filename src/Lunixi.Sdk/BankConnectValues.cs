namespace Lunixi;

/// <summary>
/// Status of a bank connection. Data is read only while a connection is <c>ACTIVE</c> or <c>DEGRADED</c> (the last reads from
/// the bank failed, so the data may be older; see the account's <c>freshness</c>). <see cref="BankConnectClient.RequestSyncAsync"/>
/// on any other status is 409 <c>bank_connect.connection.not_active</c>.
/// </summary>
public static class BankConnectionStatuses
{
    /// <summary><c>PENDING_DOCUMENTS</c></summary>
    public const string PendingDocuments = "PENDING_DOCUMENTS";
    /// <summary><c>UNDER_REVIEW</c></summary>
    public const string UnderReview = "UNDER_REVIEW";
    /// <summary><c>PENDING_SETUP</c></summary>
    public const string PendingSetup = "PENDING_SETUP";
    /// <summary><c>ACTIVE</c></summary>
    public const string Active = "ACTIVE";
    /// <summary><c>DEGRADED</c></summary>
    public const string Degraded = "DEGRADED";
    /// <summary><c>SUSPENDED</c></summary>
    public const string Suspended = "SUSPENDED";
    /// <summary><c>DISCONNECTED</c></summary>
    public const string Disconnected = "DISCONNECTED";
    /// <summary>Every connection status.</summary>
    public static IReadOnlyList<string> All { get; } = [PendingDocuments, UnderReview, PendingSetup, Active, Degraded, Suspended, Disconnected];
}

/// <summary>Status of a connected bank account.</summary>
public static class BankAccountStatuses
{
    /// <summary><c>ACTIVE</c></summary>
    public const string Active = "ACTIVE";
    /// <summary><c>INACTIVE</c></summary>
    public const string Inactive = "INACTIVE";
    /// <summary>Every account status.</summary>
    public static IReadOnlyList<string> All { get; } = [Active, Inactive];
}

/// <summary>Money in (<c>CREDIT</c>) or out (<c>DEBIT</c>) of an account; <c>amountMinor</c> is always positive.</summary>
public static class BankTransactionDirections
{
    /// <summary><c>CREDIT</c></summary>
    public const string Credit = "CREDIT";
    /// <summary><c>DEBIT</c></summary>
    public const string Debit = "DEBIT";
    /// <summary>Both directions.</summary>
    public static IReadOnlyList<string> All { get; } = [Credit, Debit];
}

/// <summary>Lunixi's bank-independent transaction type (<c>canonicalType</c>).</summary>
public static class BankTransactionTypes
{
    /// <summary><c>TRANSFER_IN</c></summary>
    public const string TransferIn = "TRANSFER_IN";
    /// <summary><c>TRANSFER_OUT</c></summary>
    public const string TransferOut = "TRANSFER_OUT";
    /// <summary><c>INTERNAL_IN</c></summary>
    public const string InternalIn = "INTERNAL_IN";
    /// <summary><c>INTERNAL_OUT</c></summary>
    public const string InternalOut = "INTERNAL_OUT";
    /// <summary><c>POS_SETTLEMENT</c></summary>
    public const string PosSettlement = "POS_SETTLEMENT";
    /// <summary><c>POS_SETTLEMENT_REFUND</c></summary>
    public const string PosSettlementRefund = "POS_SETTLEMENT_REFUND";
    /// <summary><c>FEE</c></summary>
    public const string Fee = "FEE";
    /// <summary><c>TAX</c></summary>
    public const string Tax = "TAX";
    /// <summary><c>CASH_IN</c></summary>
    public const string CashIn = "CASH_IN";
    /// <summary><c>CASH_OUT</c></summary>
    public const string CashOut = "CASH_OUT";
    /// <summary><c>CARD</c></summary>
    public const string Card = "CARD";
    /// <summary><c>LOAN</c></summary>
    public const string Loan = "LOAN";
    /// <summary><c>FX</c></summary>
    public const string Fx = "FX";
    /// <summary><c>INTEREST</c></summary>
    public const string Interest = "INTEREST";
    /// <summary><c>OTHER</c></summary>
    public const string Other = "OTHER";
    /// <summary>Every transaction type.</summary>
    public static IReadOnlyList<string> All { get; } =
        [TransferIn, TransferOut, InternalIn, InternalOut, PosSettlement, PosSettlementRefund, Fee, Tax, CashIn, CashOut, Card, Loan, Fx, Interest, Other];
}

/// <summary>Whether a bank can be connected (<c>availability</c>); connections are started in the Lunixi dashboard.</summary>
public static class BankAvailabilities
{
    /// <summary><c>AVAILABLE</c></summary>
    public const string Available = "AVAILABLE";
    /// <summary><c>COMING_SOON</c></summary>
    public const string ComingSoon = "COMING_SOON";
    /// <summary>Both values.</summary>
    public static IReadOnlyList<string> All { get; } = [Available, ComingSoon];
}

/// <summary>
/// What a bank or a connection offers (<c>capabilities</c>). <c>OUTGOING_TRANSFER</c> and <c>FX_EXCHANGE</c> open
/// <see cref="BankConnectClient.Transfers"/> and <see cref="BankConnectClient.Fx"/>.
/// </summary>
public static class BankCapabilities
{
    /// <summary><c>ACCOUNT_LIST</c></summary>
    public const string AccountList = "ACCOUNT_LIST";
    /// <summary><c>BALANCE</c></summary>
    public const string Balance = "BALANCE";
    /// <summary><c>TRANSACTIONS</c></summary>
    public const string Transactions = "TRANSACTIONS";
    /// <summary><c>OUTGOING_TRANSFER</c></summary>
    public const string OutgoingTransfer = "OUTGOING_TRANSFER";
    /// <summary><c>FX_EXCHANGE</c></summary>
    public const string FxExchange = "FX_EXCHANGE";
    /// <summary>Every capability.</summary>
    public static IReadOnlyList<string> All { get; } = [AccountList, Balance, Transactions, OutgoingTransfer, FxExchange];
}

/// <summary><c>syncStatus</c> of <see cref="BankConnectClient.RequestSyncAsync"/>: <c>QUEUED</c> runs now or already waits, <c>SCHEDULED</c> runs at <c>scheduledAt</c>.</summary>
public static class BankSyncStatuses
{
    /// <summary><c>QUEUED</c></summary>
    public const string Queued = "QUEUED";
    /// <summary><c>SCHEDULED</c></summary>
    public const string Scheduled = "SCHEDULED";
    /// <summary>Both values.</summary>
    public static IReadOnlyList<string> All { get; } = [Queued, Scheduled];
}

/// <summary>
/// Bank-account webhook event types, delivered in the same signed envelope as every Lunixi webhook (verify with
/// <see cref="WebhookVerifier"/>, read with <see cref="BankAccountTransactionReceivedData.FromWebhook"/>).
/// </summary>
public static class BankConnectEvents
{
    /// <summary>
    /// <c>bank_account.transaction.received</c>: a new transaction (CREDIT or DEBIT) was read from one of your connected
    /// accounts, once per transaction; the history loaded when an account is first connected does not send it. The event id
    /// is the same on every redelivery: deduplicate on it.
    /// </summary>
    public const string TransactionReceived = "bank_account.transaction.received";
    /// <summary>Subscription pattern covering every bank-account event.</summary>
    public const string AllEvents = "bank_account.*";
    /// <summary>Every bank-account event type.</summary>
    public static IReadOnlyList<string> All { get; } = [TransactionReceived];
}

/// <summary>
/// Status of an outgoing transfer. <c>PENDING_OTP</c>: started in the dashboard, waiting for the user's e-mail code (an API-key
/// transfer never has it). <c>SUBMITTED</c>: queued or sent to the bank. <c>PROCESSING</c>: the bank is processing it.
/// <c>COMPLETED</c>: the bank completed it; it can still become <c>RETURNED</c>. <c>FAILED</c>, <c>RETURNED</c> and
/// <c>CANCELED</c> are final.
/// </summary>
public static class BankTransferStatuses
{
    /// <summary><c>PENDING_OTP</c></summary>
    public const string PendingOtp = "PENDING_OTP";
    /// <summary><c>SUBMITTED</c></summary>
    public const string Submitted = "SUBMITTED";
    /// <summary><c>PROCESSING</c></summary>
    public const string Processing = "PROCESSING";
    /// <summary><c>COMPLETED</c></summary>
    public const string Completed = "COMPLETED";
    /// <summary><c>FAILED</c></summary>
    public const string Failed = "FAILED";
    /// <summary><c>RETURNED</c></summary>
    public const string Returned = "RETURNED";
    /// <summary><c>CANCELED</c></summary>
    public const string Canceled = "CANCELED";
    /// <summary>Every transfer status.</summary>
    public static IReadOnlyList<string> All { get; } = [PendingOtp, Submitted, Processing, Completed, Failed, Returned, Canceled];
}

/// <summary>The rail the bank chose for a transfer: own accounts, same bank, FAST, EFT; <c>UNKNOWN</c> until the bank says.</summary>
public static class BankTransferRails
{
    /// <summary><c>INTERNAL</c></summary>
    public const string Internal = "INTERNAL";
    /// <summary><c>INTRABANK</c></summary>
    public const string Intrabank = "INTRABANK";
    /// <summary><c>FAST</c></summary>
    public const string Fast = "FAST";
    /// <summary><c>EFT</c></summary>
    public const string Eft = "EFT";
    /// <summary><c>UNKNOWN</c></summary>
    public const string Unknown = "UNKNOWN";
    /// <summary>Every rail.</summary>
    public static IReadOnlyList<string> All { get; } = [Internal, Intrabank, Fast, Eft, Unknown];
}

/// <summary>Where a transfer or an exchange order was started (<c>initiatedVia</c>).</summary>
public static class BankTransferInitiatedVia
{
    /// <summary><c>PANEL</c></summary>
    public const string Panel = "PANEL";
    /// <summary><c>API</c></summary>
    public const string Api = "API";
    /// <summary>Both values.</summary>
    public static IReadOnlyList<string> All { get; } = [Panel, Api];
}

/// <summary>Status of a currency exchange order; an order is created <c>SUBMITTED</c>.</summary>
public static class BankFxOrderStatuses
{
    /// <summary><c>SUBMITTED</c></summary>
    public const string Submitted = "SUBMITTED";
    /// <summary><c>COMPLETED</c></summary>
    public const string Completed = "COMPLETED";
    /// <summary><c>FAILED</c></summary>
    public const string Failed = "FAILED";
    /// <summary>Every exchange order status.</summary>
    public static IReadOnlyList<string> All { get; } = [Submitted, Completed, Failed];
}

/// <summary>Which amount of an exchange quote you fixed (<c>requestedSide</c>): <c>SOURCE</c> (sell amount) or <c>TARGET</c> (buy amount).</summary>
public static class BankFxRequestedSides
{
    /// <summary><c>SOURCE</c></summary>
    public const string Source = "SOURCE";
    /// <summary><c>TARGET</c></summary>
    public const string Target = "TARGET";
    /// <summary>Both values.</summary>
    public static IReadOnlyList<string> All { get; } = [Source, Target];
}

/// <summary>
/// Outgoing transfer webhook event types (product <c>BANK_CONNECT</c>, read with <see cref="BankTransferWebhookData.FromWebhook"/>).
/// One event per transfer and type; the event id is the same on every redelivery. They are distinct from the payment
/// <c>payment.bank_transfer.*</c> events (<see cref="PaymentBankTransferEvents"/>). Exchange orders have no webhook.
/// </summary>
public static class BankTransferEvents
{
    /// <summary><c>bank_transfer.created</c>: a transfer was created (API: <c>SUBMITTED</c>; dashboard: <c>PENDING_OTP</c>).</summary>
    public const string Created = "bank_transfer.created";
    /// <summary><c>bank_transfer.completed</c>: the bank completed the transfer.</summary>
    public const string Completed = "bank_transfer.completed";
    /// <summary><c>bank_transfer.failed</c>: the transfer failed; <c>failureCode</c> says why.</summary>
    public const string Failed = "bank_transfer.failed";
    /// <summary><c>bank_transfer.returned</c>: the receiving bank sent a completed transfer back.</summary>
    public const string Returned = "bank_transfer.returned";
    /// <summary>Subscription pattern covering every transfer event.</summary>
    public const string AllEvents = "bank_transfer.*";
    /// <summary>Every transfer event type.</summary>
    public static IReadOnlyList<string> All { get; } = [Created, Completed, Failed, Returned];
}

/// <summary>
/// Query filters of the bank-connect read routes (the gateway rejects any other with 400) and their limits. The filter lists
/// are the Node SDK's <c>BANK_CONNECT_FILTERS</c>.
/// </summary>
public static class BankConnectFilters
{
    /// <summary>Largest page (<c>limit</c> 1–100; the gateway's default is 25).</summary>
    public const int MaxLimit = 100;
    /// <summary>Most account ids one <c>accountId</c> filter may carry.</summary>
    public const int MaxAccountIds = 100;
    /// <summary>Widest <c>bookedFrom</c> … <c>bookedTo</c> window of one transaction request, in days.</summary>
    public const int BookedRangeMaxDays = 92;
    /// <summary>Longest cursor the gateway accepts.</summary>
    public const int CursorMaxLength = 2048;

    /// <summary>Filters by list route name.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> All { get; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        ["listBanks"] = ["limit", "cursor"],
        ["listConnections"] = ["limit", "cursor", "status", "bankId"],
        ["listAccounts"] = ["limit", "cursor", "connectionId", "bankId", "currency", "status"],
        ["listBalances"] = ["limit", "cursor", "accountId", "currency"],
        ["listTransactions"] = ["limit", "cursor", "accountId", "bookedFrom", "bookedTo", "direction", "minAmountMinor", "maxAmountMinor", "canonicalType"],
    };
}

/// <summary>
/// Transfer and exchange routes (<c>/api/v1/bank-connect</c>): the single source of method, path, body fields, query fields
/// and the <c>Idempotency-Key</c> rule of <see cref="BankTransferClient"/> and <see cref="BankFxClient"/> (the gateway runs
/// with <c>forbidNonWhitelisted</c>: any other field is a 400). Every route is signed and needs <c>bank-connect:transfer</c>
/// plus a key with an IP allowlist. The table is the Node SDK's <c>BANK_TRANSFER_ROUTES</c>, row for row, and the shared
/// catalog's group of the same name.
/// </summary>
public static class BankTransferRoutes
{
    /// <summary><c>Idempotency-Key</c> of a transfer or an exchange order: 8–200 printable ASCII characters.</summary>
    public const string IdempotencyKeyPattern = @"^[\x21-\x7E]{8,200}$";
    /// <summary><c>paymentPurpose</c>: a code from <see cref="BankTransferClient.ListPurposesAsync"/>.</summary>
    public const string PaymentPurposePattern = "^[A-Za-z0-9_]{1,64}$";
    /// <summary>Longest <c>description</c> of a transfer or an exchange order, in characters.</summary>
    public const int DescriptionMaxLength = 140;
    /// <summary>Shortest <c>receiverName</c>, in characters.</summary>
    public const int ReceiverNameMinLength = 2;
    /// <summary>Longest <c>receiverName</c>, in characters.</summary>
    public const int ReceiverNameMaxLength = 140;
    /// <summary>Longest <c>receiverIban</c> the gateway accepts (spaces included); the service checks the TR IBAN checksum.</summary>
    public const int IbanMaxLength = 42;

    private const string Base = "/api/v1/bank-connect";

    /// <summary>Every route by name, in the Node table's order.</summary>
    public static IReadOnlyDictionary<string, LunixiRoute> All { get; } = new OrderedRoutes
    {
        ["listTransferPurposes"] = LunixiRoute.Get(Base + "/transfer-purposes").WithQuery("connectionId"),
        ["createTransfer"] = LunixiRoute.Post(Base + "/transfers").Keyed().WithRequired("accountId", "receiverIban", "receiverName", "amountMinor")
            .WithOptional("currency", "paymentPurpose", "description"),
        ["listTransfers"] = LunixiRoute.Get(Base + "/transfers").WithQuery("limit", "cursor", "accountId", "status", "createdFrom", "createdTo"),
        ["getTransfer"] = LunixiRoute.Get(Base + "/transfers/:transferId"),
        ["listFxRates"] = LunixiRoute.Get(Base + "/fx/rates").WithQuery("connectionId"),
        ["createFxQuote"] = LunixiRoute.Post(Base + "/fx/quotes").WithRequired("sourceAccountId", "targetAccountId").WithOptional("sourceAmountMinor", "targetAmountMinor"),
        ["createFxOrder"] = LunixiRoute.Post(Base + "/fx/orders").Keyed().WithRequired("quoteId").WithOptional("description"),
        ["listFxOrders"] = LunixiRoute.Get(Base + "/fx/orders").WithQuery("limit", "cursor", "status"),
        ["getFxOrder"] = LunixiRoute.Get(Base + "/fx/orders/:orderId"),
    }.Freeze();
}
