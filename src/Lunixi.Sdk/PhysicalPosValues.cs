namespace Lunixi;

/// <summary>
/// <c>operation</c> of a physical POS payment. The first three start a payment (<see cref="PhysicalPosClient.CreatePaymentAsync"/>);
/// the rest reverse one (<see cref="PhysicalPosClient.CreateReversalAsync"/>). Wire sets are open: never reject a value
/// missing here.
/// </summary>
public static class PhysicalPosPaymentOperations
{
    /// <summary>A sale.</summary>
    public const string Sale = "SALE";
    /// <summary>Opens a pre-authorisation.</summary>
    public const string PreauthOpen = "PREAUTH_OPEN";
    /// <summary>A refund that is not linked to a payment of this account.</summary>
    public const string RefundUnmatched = "REFUND_UNMATCHED";
    /// <summary>Voids an approved payment before end-of-day.</summary>
    public const string Void = "VOID";
    /// <summary>Refunds an approved payment, by amount or by items.</summary>
    public const string Refund = "REFUND";
    /// <summary>Closes (captures) a pre-authorisation.</summary>
    public const string PreauthClose = "PREAUTH_CLOSE";
    /// <summary>Cancels a pre-authorisation.</summary>
    public const string PreauthCancel = "PREAUTH_CANCEL";
    /// <summary>Operations that start a payment.</summary>
    public static IReadOnlyList<string> Root { get; } = [Sale, PreauthOpen, RefundUnmatched];
    /// <summary>Operations that reverse a payment.</summary>
    public static IReadOnlyList<string> Reversal { get; } = [Void, Refund, PreauthClose, PreauthCancel];
    /// <summary>Every operation known to this release.</summary>
    public static IReadOnlyList<string> All { get; } = [.. Root, .. Reversal];
}

/// <summary><c>paymentStatus</c> of a physical POS payment. Decide on this value; ignore a <c>statusReason</c> you do not know.</summary>
public static class PhysicalPosPaymentStatuses
{
    /// <summary>Accepted, not yet sent to the device.</summary>
    public const string NotStarted = "NOT_STARTED";
    /// <summary>Running on the device.</summary>
    public const string InProgress = "IN_PROGRESS";
    /// <summary>Approved: money moved.</summary>
    public const string Approved = "APPROVED";
    /// <summary>Approved for less than requested (<c>approvedAmountMinor</c>).</summary>
    public const string PartiallyApproved = "PARTIALLY_APPROVED";
    /// <summary>Declined; no money moved.</summary>
    public const string Declined = "DECLINED";
    /// <summary>Started and cancelled; no money moved.</summary>
    public const string Cancelled = "CANCELLED";
    /// <summary>Never started (the start window passed, or it was cancelled before dispatch).</summary>
    public const string Expired = "EXPIRED";
    /// <summary>Never started because of an error.</summary>
    public const string Failed = "FAILED";
    /// <summary>The outcome is unknown and money may have moved: never start the payment again.</summary>
    public const string Unknown = "UNKNOWN";
    /// <summary>Automatic verification ended without a result; resolved in the panel.</summary>
    public const string ManualReview = "MANUAL_REVIEW";
    /// <summary>Every status known to this release.</summary>
    public static IReadOnlyList<string> All { get; } =
        [NotStarted, InProgress, Approved, PartiallyApproved, Declined, Cancelled, Expired, Failed, Unknown, ManualReview];
}

/// <summary><c>reversalStatus</c> of a root physical POS payment.</summary>
public static class PhysicalPosReversalStatuses
{
    /// <summary>Not reversed.</summary>
    public const string None = "NONE";
    /// <summary>Partly refunded.</summary>
    public const string PartiallyRefunded = "PARTIALLY_REFUNDED";
    /// <summary>Fully refunded.</summary>
    public const string Refunded = "REFUNDED";
    /// <summary>Voided.</summary>
    public const string Voided = "VOIDED";
    /// <summary>Pre-authorisation closed.</summary>
    public const string PreauthClosed = "PREAUTH_CLOSED";
    /// <summary>Pre-authorisation cancelled.</summary>
    public const string PreauthCancelled = "PREAUTH_CANCELLED";
    /// <summary>Pre-authorisation expired.</summary>
    public const string PreauthExpired = "PREAUTH_EXPIRED";
    /// <summary>Every reversal status known to this release.</summary>
    public static IReadOnlyList<string> All { get; } =
        [None, PartiallyRefunded, Refunded, Voided, PreauthClosed, PreauthCancelled, PreauthExpired];
}

/// <summary>
/// Physical POS webhook event types (<c>physical_pos.*</c>); read them with <see cref="PhysicalPosClient.ParseWebhookEvent(LunixiWebhook)"/>.
/// Reversals (void, refund, pre-authorisation close / cancel) use the same payment events; tell them apart with
/// <c>data.operation</c> and <c>data.rootPaymentId</c>, and the original payment's source with
/// <see cref="PhysicalPosWebhookEvent.Original"/>. <see cref="PaymentDeviceSale"/> reports a sale made directly on the device
/// (<c>origin: DEVICE_DIRECT</c>): it arrives approved with <c>deviceOccurredAt</c> and no <c>approved</c> event is sent
/// for it. End-of-day events (<see cref="Settlement"/>) have their own payloads and no <c>historySeq</c>; deduplicate them by
/// the event id. The set is open: a new <c>physical_pos.*</c> type may appear later.
/// </summary>
public static class PhysicalPosEvents
{
    /// <summary>The payment (or reversal) was approved: money moved.</summary>
    public const string PaymentApproved = "physical_pos.payment.approved";
    /// <summary>Approved for less than requested.</summary>
    public const string PaymentPartiallyApproved = "physical_pos.payment.partially_approved";
    /// <summary>Declined, or resolved as not charged; no money moved.</summary>
    public const string PaymentDeclined = "physical_pos.payment.declined";
    /// <summary>Started and cancelled; no money moved.</summary>
    public const string PaymentCancelled = "physical_pos.payment.cancelled";
    /// <summary>Never started: the start window passed or it was cancelled before dispatch.</summary>
    public const string PaymentExpired = "physical_pos.payment.expired";
    /// <summary>Never started because of an error.</summary>
    public const string PaymentFailed = "physical_pos.payment.failed";
    /// <summary>The outcome is unknown and money may have moved: do not charge again.</summary>
    public const string PaymentUnknown = "physical_pos.payment.unknown";
    /// <summary>Automatic verification ended without a result; resolve it in the panel.</summary>
    public const string PaymentManualReview = "physical_pos.payment.manual_review";
    /// <summary>A sale made directly on the device, recorded already approved.</summary>
    public const string PaymentDeviceSale = "physical_pos.payment.device_sale";
    /// <summary>A fiscal document was issued.</summary>
    public const string FiscalDocumentIssued = "physical_pos.fiscal_document.issued";
    /// <summary>A fiscal document failed.</summary>
    public const string FiscalDocumentFailed = "physical_pos.fiscal_document.failed";
    /// <summary>A fiscal document was cancelled.</summary>
    public const string FiscalDocumentCancelled = "physical_pos.fiscal_document.cancelled";
    /// <summary>A fiscal document needs an action in the panel.</summary>
    public const string FiscalDocumentActionRequired = "physical_pos.fiscal_document.action_required";
    /// <summary>The device's end-of-day result was recorded (any source).</summary>
    public const string SettlementCompleted = "physical_pos.settlement.completed";
    /// <summary>A scheduled end-of-day could not finish within its window.</summary>
    public const string SettlementFailed = "physical_pos.settlement.failed";
    /// <summary>No end-of-day result within the policy's threshold.</summary>
    public const string SettlementOverdue = "physical_pos.settlement.overdue";
    /// <summary>A scheduled end-of-day was skipped for a reason you can fix.</summary>
    public const string SettlementSkipped = "physical_pos.settlement.skipped";
    /// <summary>Subscription pattern covering every physical POS event.</summary>
    public const string Pattern = "physical_pos.*";
    /// <summary>Every event type known to this release.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        PaymentApproved, PaymentPartiallyApproved, PaymentDeclined, PaymentCancelled, PaymentExpired, PaymentFailed,
        PaymentUnknown, PaymentManualReview, PaymentDeviceSale, FiscalDocumentIssued, FiscalDocumentFailed,
        FiscalDocumentCancelled, FiscalDocumentActionRequired, SettlementCompleted, SettlementFailed, SettlementOverdue,
        SettlementSkipped,
    ];
    /// <summary>The end-of-day event types.</summary>
    public static IReadOnlyList<string> Settlement { get; } = [SettlementCompleted, SettlementFailed, SettlementOverdue, SettlementSkipped];
}

/// <summary>Physical POS limits and the gateway codes after which a payment write certainly started nothing.</summary>
public static class PhysicalPosValues
{
    /// <summary>Shortest accepted <c>Idempotency-Key</c>.</summary>
    public const int IdempotencyKeyMinLength = 8;
    /// <summary>Longest accepted <c>Idempotency-Key</c> (printable ASCII without spaces, sent exactly as given).</summary>
    public const int IdempotencyKeyMaxLength = 200;
    /// <summary>Largest <c>limit</c> of a list call.</summary>
    public const int MaxLimit = 100;
    /// <summary>Largest <c>rowVersion</c> / <c>expectedRowVersion</c>.</summary>
    public const long RowVersionMax = 2_147_483_647;
    /// <summary>
    /// 5xx codes after which the gateway guarantees the payment write wrote nothing. Any other 5xx, and a request without an
    /// HTTP answer, leaves the outcome of a payment write unknown.
    /// </summary>
    public static IReadOnlyList<string> NotStartedCodes { get; } =
    [
        "physical_pos.presence.unavailable", "physical_pos.signing.unavailable", "physical_pos.service.busy",
        "physical_pos.service.clock_skew", "physical_pos.service.not_configured",
    ];
}

/// <summary>
/// The closed field lists of physical POS request bodies and the query filters of each list route. The gateway rejects an
/// unknown body field or query parameter with 400, so the SDK refuses one before sending.
/// </summary>
public static class PhysicalPosFields
{
    /// <summary><see cref="PhysicalPosClient.CreatePaymentAsync"/> body.</summary>
    public static IReadOnlyList<string> CreatePayment { get; } = PhysicalPosShapes.Names(PhysicalPosShapes.CreatePayment);
    /// <summary><c>target</c> of a payment: exactly one of <c>deviceId</c>, <c>vendorCode</c> + <c>serialNumber</c>, or <c>externalRef</c>.</summary>
    public static IReadOnlyList<string> PaymentTarget { get; } = PhysicalPosShapes.Names(PhysicalPosShapes.Target);
    /// <summary>One sale item (<c>items[]</c> of a payment).</summary>
    public static IReadOnlyList<string> SaleItem { get; } = PhysicalPosShapes.Names(PhysicalPosShapes.SaleItem);
    /// <summary><see cref="PhysicalPosClient.CreateReversalAsync"/> body.</summary>
    public static IReadOnlyList<string> CreateReversal { get; } = PhysicalPosShapes.Names(PhysicalPosShapes.CreateReversal);
    /// <summary>One refund item (<c>items[]</c> of a reversal).</summary>
    public static IReadOnlyList<string> RefundItem { get; } = PhysicalPosShapes.Names(PhysicalPosShapes.RefundItem);
    /// <summary>Request bodies of the management routes, by operation (<c>createStore</c>, <c>statusChange</c>, <c>setSettlementPolicy</c> …).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Management { get; } = PhysicalPosShapes.Management
        .ToDictionary(entry => entry.Key, entry => PhysicalPosShapes.Names(entry.Value), StringComparer.Ordinal);
    /// <summary>Query filters per route (<c>listPayments</c>, <c>listDevices</c>, <c>history</c> for audit / link / key lists …).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Filters { get; } = PhysicalPosShapes.Filters;
}
