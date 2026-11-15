namespace Lunixi;

/// <summary>Who collects the premium (<c>premiumCollectedBy</c>). <c>PROVIDER</c> orders cannot be completed through this API yet.</summary>
public static class InsurancePremiumCollectedBy
{
    /// <summary><c>MERCHANT</c>: you collect the premium (in a Lunixi payment or outside Lunixi).</summary>
    public const string Merchant = "MERCHANT";
    /// <summary><c>PROVIDER</c>: the insurance provider collects it.</summary>
    public const string Provider = "PROVIDER";
    /// <summary>Both values.</summary>
    public static IReadOnlyList<string> All { get; } = [Merchant, Provider];
}

/// <summary>Status of a quote option. Only a <c>QUOTED</c> option that has not expired becomes an order.</summary>
public static class InsuranceQuoteStatuses
{
    /// <summary><c>REQUESTED</c></summary>
    public const string Requested = "REQUESTED";
    /// <summary><c>QUOTED</c></summary>
    public const string Quoted = "QUOTED";
    /// <summary><c>EXPIRED</c></summary>
    public const string Expired = "EXPIRED";
    /// <summary><c>REJECTED</c></summary>
    public const string Rejected = "REJECTED";
    /// <summary><c>ERROR</c></summary>
    public const string Error = "ERROR";
    /// <summary>Every quote status.</summary>
    public static IReadOnlyList<string> All { get; } = [Requested, Quoted, Expired, Rejected, Error];
}

/// <summary>
/// The order's overall status. It is not stored: every read derives it from the four axes, in this order — REFUNDED,
/// CANCELLED, FAILED, ACTION_REQUIRED, PROCESSING, PARTIALLY_ACTIVE, ACTIVE, CREATED.
/// </summary>
public static class InsuranceOrderStatuses
{
    /// <summary><c>CREATED</c></summary>
    public const string Created = "CREATED";
    /// <summary><c>PROCESSING</c></summary>
    public const string Processing = "PROCESSING";
    /// <summary><c>ACTION_REQUIRED</c></summary>
    public const string ActionRequired = "ACTION_REQUIRED";
    /// <summary><c>PARTIALLY_ACTIVE</c></summary>
    public const string PartiallyActive = "PARTIALLY_ACTIVE";
    /// <summary><c>ACTIVE</c></summary>
    public const string Active = "ACTIVE";
    /// <summary><c>FAILED</c></summary>
    public const string Failed = "FAILED";
    /// <summary><c>CANCELLED</c></summary>
    public const string Cancelled = "CANCELLED";
    /// <summary><c>REFUNDED</c></summary>
    public const string Refunded = "REFUNDED";
    /// <summary>Every order status.</summary>
    public static IReadOnlyList<string> All { get; } = [Created, Processing, ActionRequired, PartiallyActive, Active, Failed, Cancelled, Refunded];
}

/// <summary>Premium collection axis of an order (<c>collectionStatus</c>).</summary>
public static class InsuranceCollectionStatuses
{
    /// <summary><c>NOT_STARTED</c></summary>
    public const string NotStarted = "NOT_STARTED";
    /// <summary><c>PENDING</c></summary>
    public const string Pending = "PENDING";
    /// <summary><c>ACTION_REQUIRED</c></summary>
    public const string ActionRequired = "ACTION_REQUIRED";
    /// <summary><c>COLLECTED</c></summary>
    public const string Collected = "COLLECTED";
    /// <summary><c>FAILED</c></summary>
    public const string Failed = "FAILED";
    /// <summary><c>UNKNOWN</c></summary>
    public const string Unknown = "UNKNOWN";
    /// <summary>Every collection status.</summary>
    public static IReadOnlyList<string> All { get; } = [NotStarted, Pending, ActionRequired, Collected, Failed, Unknown];
}

/// <summary>Policy axis (<c>policyStatus</c>) of an order and of each of its policies.</summary>
public static class InsurancePolicyStatuses
{
    /// <summary><c>NOT_REQUESTED</c></summary>
    public const string NotRequested = "NOT_REQUESTED";
    /// <summary><c>PENDING</c></summary>
    public const string Pending = "PENDING";
    /// <summary><c>PARTIALLY_ISSUED</c></summary>
    public const string PartiallyIssued = "PARTIALLY_ISSUED";
    /// <summary><c>ISSUED</c></summary>
    public const string Issued = "ISSUED";
    /// <summary><c>FAILED_RETRYABLE</c></summary>
    public const string FailedRetryable = "FAILED_RETRYABLE";
    /// <summary><c>FAILED_FINAL</c></summary>
    public const string FailedFinal = "FAILED_FINAL";
    /// <summary><c>UNKNOWN</c></summary>
    public const string Unknown = "UNKNOWN";
    /// <summary><c>CANCEL_PENDING</c></summary>
    public const string CancelPending = "CANCEL_PENDING";
    /// <summary><c>CANCELLED</c></summary>
    public const string Cancelled = "CANCELLED";
    /// <summary>Every policy status.</summary>
    public static IReadOnlyList<string> All { get; } =
        [NotRequested, Pending, PartiallyIssued, Issued, FailedRetryable, FailedFinal, Unknown, CancelPending, Cancelled];
}

/// <summary>Premium refund axis of an order (<c>refundStatus</c>).</summary>
public static class InsuranceRefundStatuses
{
    /// <summary><c>NONE</c></summary>
    public const string None = "NONE";
    /// <summary><c>REQUIRED</c></summary>
    public const string Required = "REQUIRED";
    /// <summary><c>PENDING</c></summary>
    public const string Pending = "PENDING";
    /// <summary><c>REFUNDED</c></summary>
    public const string Refunded = "REFUNDED";
    /// <summary><c>FAILED</c></summary>
    public const string Failed = "FAILED";
    /// <summary>Every refund status.</summary>
    public static IReadOnlyList<string> All { get; } = [None, Required, Pending, Refunded, Failed];
}

/// <summary>Policy document axis (<c>documentStatus</c>); it does not change the overall status.</summary>
public static class InsuranceDocumentStatuses
{
    /// <summary><c>NOT_AVAILABLE</c></summary>
    public const string NotAvailable = "NOT_AVAILABLE";
    /// <summary><c>PENDING</c></summary>
    public const string Pending = "PENDING";
    /// <summary><c>AVAILABLE</c></summary>
    public const string Available = "AVAILABLE";
    /// <summary><c>FAILED</c></summary>
    public const string Failed = "FAILED";
    /// <summary>Every document status.</summary>
    public static IReadOnlyList<string> All { get; } = [NotAvailable, Pending, Available, Failed];
}

/// <summary>How the premium of an order is paid (<c>paymentMode</c>).</summary>
public static class InsurancePaymentModes
{
    /// <summary><c>LUNIXI_PAYMENT</c></summary>
    public const string LunixiPayment = "LUNIXI_PAYMENT";
    /// <summary><c>EXTERNAL_PAYMENT</c></summary>
    public const string ExternalPayment = "EXTERNAL_PAYMENT";
    /// <summary><c>NO_PAYMENT</c></summary>
    public const string NoPayment = "NO_PAYMENT";
    /// <summary>Every payment mode.</summary>
    public static IReadOnlyList<string> All { get; } = [LunixiPayment, ExternalPayment, NoPayment];
}

/// <summary>Where an order was created (<c>surface</c>).</summary>
public static class InsuranceOrderSurfaces
{
    /// <summary><c>MERCHANT_API</c></summary>
    public const string MerchantApi = "MERCHANT_API";
    /// <summary><c>CHECKOUT_FORM</c></summary>
    public const string CheckoutForm = "CHECKOUT_FORM";
    /// <summary><c>PANEL</c></summary>
    public const string Panel = "PANEL";
    /// <summary>Every surface.</summary>
    public static IReadOnlyList<string> All { get; } = [MerchantApi, CheckoutForm, Panel];
}

/// <summary>
/// Status of a lead: what Lunixi knows about sending it to the insurance provider (the provider does not report back).
/// <c>PENDING</c>: the provider call is still running. <c>SUBMITTED</c>: the provider received it. <c>UNKNOWN</c>: the outcome
/// is not known and the lead is never sent again (open a new lead with a new <c>Idempotency-Key</c> if needed).
/// <c>FAILED</c>: refused, or it never reached the provider (<c>errorCode</c>).
/// </summary>
public static class InsuranceLeadStatuses
{
    /// <summary><c>PENDING</c></summary>
    public const string Pending = "PENDING";
    /// <summary><c>SUBMITTED</c></summary>
    public const string Submitted = "SUBMITTED";
    /// <summary><c>UNKNOWN</c></summary>
    public const string Unknown = "UNKNOWN";
    /// <summary><c>FAILED</c></summary>
    public const string Failed = "FAILED";
    /// <summary>Every lead status.</summary>
    public static IReadOnlyList<string> All { get; } = [Pending, Submitted, Unknown, Failed];
}

/// <summary>
/// Lunixi reference code lists (<see cref="InsuranceClient.ListReferenceValuesAsync"/>). <c>DISTRICT</c>, <c>DEVICE_MODEL</c>,
/// <c>VEHICLE_MODEL</c>, <c>BANK_BRANCH</c> and <c>PET_BREED</c> need <c>parentCode</c> (province, brand, bank, <c>CAT</c> |
/// <c>DOG</c>); <c>COUNTRY</c>, <c>CITY</c> and <c>BANK</c> take none. National standards are the codes as they are (ISO 3166-1
/// alpha-2 country, licence-plate province code <c>01</c>–<c>81</c>); the others are Lunixi codes.
/// </summary>
public static class InsuranceReferenceKinds
{
    /// <summary><c>COUNTRY</c></summary>
    public const string Country = "COUNTRY";
    /// <summary><c>CITY</c></summary>
    public const string City = "CITY";
    /// <summary><c>DISTRICT</c></summary>
    public const string District = "DISTRICT";
    /// <summary><c>DEVICE_BRAND</c></summary>
    public const string DeviceBrand = "DEVICE_BRAND";
    /// <summary><c>DEVICE_MODEL</c></summary>
    public const string DeviceModel = "DEVICE_MODEL";
    /// <summary><c>VEHICLE_BRAND</c></summary>
    public const string VehicleBrand = "VEHICLE_BRAND";
    /// <summary><c>VEHICLE_MODEL</c></summary>
    public const string VehicleModel = "VEHICLE_MODEL";
    /// <summary><c>VEHICLE_TYPE</c></summary>
    public const string VehicleType = "VEHICLE_TYPE";
    /// <summary><c>PRODUCT_CATEGORY</c></summary>
    public const string ProductCategory = "PRODUCT_CATEGORY";
    /// <summary><c>VEHICLE_USAGE</c></summary>
    public const string VehicleUsage = "VEHICLE_USAGE";
    /// <summary><c>LIABILITY_LIMIT</c></summary>
    public const string LiabilityLimit = "LIABILITY_LIMIT";
    /// <summary><c>OCCUPATION</c></summary>
    public const string Occupation = "OCCUPATION";
    /// <summary><c>PET_BREED</c></summary>
    public const string PetBreed = "PET_BREED";
    /// <summary><c>DWELLING_TYPE</c></summary>
    public const string DwellingType = "DWELLING_TYPE";
    /// <summary><c>DWELLING_FLOOR</c></summary>
    public const string DwellingFloor = "DWELLING_FLOOR";
    /// <summary><c>BANK</c></summary>
    public const string Bank = "BANK";
    /// <summary><c>BANK_BRANCH</c></summary>
    public const string BankBranch = "BANK_BRANCH";
    /// <summary><c>FINANCE_INSTITUTION</c></summary>
    public const string FinanceInstitution = "FINANCE_INSTITUTION";
    /// <summary><c>MEDICAL_PROCEDURE_COVER</c></summary>
    public const string MedicalProcedureCover = "MEDICAL_PROCEDURE_COVER";
    /// <summary>Every reference kind.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Country, City, District, DeviceBrand, DeviceModel, VehicleBrand, VehicleModel, VehicleType, ProductCategory, VehicleUsage,
        LiabilityLimit, Occupation, PetBreed, DwellingType, DwellingFloor, Bank, BankBranch, FinanceInstitution, MedicalProcedureCover,
    ];
}

/// <summary>
/// Insurance webhook event types (subscription <c>insurance.*</c>), delivered in the same signed envelope as every Lunixi
/// webhook. Read order events with <see cref="InsuranceWebhookData.FromWebhook"/> and the lead event with
/// <see cref="InsuranceLeadWebhookData.FromWebhook"/>; deduplicate on the event id (<c>data.eventId</c> is the same value).
/// </summary>
public static class InsuranceEvents
{
    /// <summary><c>insurance.policy.issued</c>: the order's policies were issued.</summary>
    public const string PolicyIssued = "insurance.policy.issued";
    /// <summary><c>insurance.policy.failed</c>: the policy could not be issued or the premium was not collected; <c>errorCode</c> says why.</summary>
    public const string PolicyFailed = "insurance.policy.failed";
    /// <summary><c>insurance.refund.required</c>: the collected premium has to be refunded; <c>errorCode</c> says why.</summary>
    public const string RefundRequired = "insurance.refund.required";
    /// <summary><c>insurance.policy.document_ready</c>: a policy document can be downloaded (<c>policies[].documentUrl</c>).</summary>
    public const string PolicyDocumentReady = "insurance.policy.document_ready";
    /// <summary><c>insurance.policy.cancelled</c>: the provider confirmed the cancellation.</summary>
    public const string PolicyCancelled = "insurance.policy.cancelled";
    /// <summary><c>insurance.refund.completed</c>: the premium refund completed.</summary>
    public const string RefundCompleted = "insurance.refund.completed";
    /// <summary><c>insurance.lead.submitted</c>: the insurance provider received a lead; the payload is a lead summary, not an order.</summary>
    public const string LeadSubmitted = "insurance.lead.submitted";
    /// <summary>Subscription pattern covering every insurance event.</summary>
    public const string AllEvents = "insurance.*";
    /// <summary>The events about an order; their payload is <see cref="InsuranceWebhookData"/>.</summary>
    public static IReadOnlyList<string> OrderEvents { get; } = [PolicyIssued, PolicyFailed, RefundRequired, PolicyDocumentReady, PolicyCancelled, RefundCompleted];
    /// <summary>The events about a lead; their payload is <see cref="InsuranceLeadWebhookData"/>.</summary>
    public static IReadOnlyList<string> LeadEvents { get; } = [LeadSubmitted];
    /// <summary>Every insurance event type.</summary>
    public static IReadOnlyList<string> All { get; } = [.. OrderEvents, .. LeadEvents];
}

/// <summary>
/// The top-level request fields and query filters of the insurance routes (the gateway answers any other with 400), and their
/// limits. The lists are the Node SDK's <c>INSURANCE_*_FIELDS</c> and <c>INSURANCE_FILTERS</c>. The field rules of an insurance
/// type's own object (<c>insured</c>, <c>tickets</c> …) are not repeated here: a violation comes back as 422
/// <c>insurance.validation.invalid</c> with every <c>details.errors[{ field, reason }]</c> at once.
/// </summary>
public static class InsuranceFields
{
    /// <summary><c>Idempotency-Key</c> accepted by the insurance routes: 8–200 printable ASCII characters, no spaces.</summary>
    public const string IdempotencyKeyPattern = @"^[\x21-\x7E]{8,200}$";
    /// <summary>Largest page (<c>limit</c> 1–100).</summary>
    public const int MaxLimit = 100;
    /// <summary>Most basket items one <c>context.items</c> may carry.</summary>
    public const int MaxContextItems = 100;
    /// <summary>Orders in one reconciliation CSV file; a longer export ends with a continuation line.</summary>
    public const int ExportOrderCap = 20000;
    /// <summary>Longest lead <c>note</c>, in characters.</summary>
    public const int LeadNoteMaxLength = 1000;

    /// <summary>The type objects of the type body, one per insurance type (<c>tickets</c> is a list, the others single objects).</summary>
    public static IReadOnlyList<string> TypeObjects { get; } =
        ["tickets", "stay", "device", "product", "vehicle", "trip", "rental", "instrument", "event", "accident", "procedure", "pet", "residence", "abroadStay"];
    /// <summary>Fields of <see cref="InsuranceClient.CreateQuoteAsync"/>: the person fields, then the type objects.</summary>
    public static IReadOnlyList<string> Quote { get; } =
        ["insuranceType", "schemaVersion", "currency", "paymentIntentId", "context", "merchantOrderId", "policyholder", "insured", .. TypeObjects];
    /// <summary>Fields of <see cref="InsuranceClient.CreateLeadAsync"/>: the type body of a quote without basket, currency and order fields, plus <c>shipment</c>, <c>company</c> and <c>note</c>.</summary>
    public static IReadOnlyList<string> Lead { get; } = ["insuranceType", "schemaVersion", "policyholder", "insured", .. TypeObjects, "shipment", "company", "note"];
    /// <summary>Fields of <see cref="InsuranceClient.SearchProductsAsync"/>.</summary>
    public static IReadOnlyList<string> ProductSearch { get; } = ["currency", "insuranceType", "context"];
    /// <summary>Fields of <see cref="InsuranceClient.CreateOrderAsync"/>.</summary>
    public static IReadOnlyList<string> Order { get; } = ["quoteId", "merchantOrderId", "consent"];
    /// <summary>Fields of <c>consent</c> (<see cref="InsuranceClient.CreateOrderAsync"/>).</summary>
    public static IReadOnlyList<string> Consent { get; } = ["termsVersion", "termsSha256", "acceptedAt"];
    /// <summary>Fields of <see cref="InsuranceClient.ConfirmPaymentAsync"/> (a premium collected outside Lunixi).</summary>
    public static IReadOnlyList<string> ExternalPayment { get; } = ["externalPaymentId", "paidAmount", "paidCurrency", "paidAt", "paymentProvider"];
    /// <summary>Fields of <c>externalRefund</c> (<see cref="InsuranceClient.RefundPremiumAsync"/> for a premium collected outside Lunixi).</summary>
    public static IReadOnlyList<string> ExternalRefund { get; } = ["externalRefundId", "refundedAt"];

    /// <summary>Query filters by read route name.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Filters { get; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        ["listProducts"] = ["currency", "paymentIntentId", "insuranceType"],
        ["listReferenceValues"] = ["parentCode", "limit", "cursor"],
        ["listOrders"] = ["limit", "cursor", "paymentIntentId", "merchantOrderId"],
        ["getReconciliationSummary"] = ["from", "to", "connectionId", "insuranceType", "currency"],
        ["listReconciliationOrders"] = ["from", "to", "connectionId", "insuranceType", "currency", "limit", "cursor"],
        ["exportReconciliation"] = ["from", "to", "connectionId", "insuranceType", "currency", "cursor"],
        ["listLeads"] = ["limit", "cursor", "insuranceType", "status"],
    };
}
