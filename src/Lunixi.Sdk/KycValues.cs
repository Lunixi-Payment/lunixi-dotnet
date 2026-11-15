namespace Lunixi;

/// <summary>KYC session types (<c>sessionType</c>). <c>EVIDENCE</c> is proof-of-X (address, funds, card ownership…), not only onboarding.</summary>
public static class KycSessionTypes
{
    /// <summary><c>KYC</c>: a person's identity.</summary>
    public const string Kyc = "KYC";
    /// <summary><c>KYB</c>: a company and its beneficial owners.</summary>
    public const string Kyb = "KYB";
    /// <summary><c>EVIDENCE</c></summary>
    public const string Evidence = "EVIDENCE";
    /// <summary>Every session type.</summary>
    public static IReadOnlyList<string> All { get; } = [Kyc, Kyb, Evidence];
}

/// <summary>KYC session status.</summary>
public static class KycSessionStatuses
{
    /// <summary><c>CREATED</c></summary>
    public const string Created = "CREATED";
    /// <summary><c>INITIATED</c></summary>
    public const string Initiated = "INITIATED";
    /// <summary><c>DOCUMENT_SUBMITTED</c></summary>
    public const string DocumentSubmitted = "DOCUMENT_SUBMITTED";
    /// <summary><c>PROCESSING</c></summary>
    public const string Processing = "PROCESSING";
    /// <summary><c>AWAITING_PROVIDER_CALLBACK</c></summary>
    public const string AwaitingProviderCallback = "AWAITING_PROVIDER_CALLBACK";
    /// <summary><c>AWAITING_ADDITIONAL_DOCUMENT</c></summary>
    public const string AwaitingAdditionalDocument = "AWAITING_ADDITIONAL_DOCUMENT";
    /// <summary><c>AWAITING_USER_ACTION</c></summary>
    public const string AwaitingUserAction = "AWAITING_USER_ACTION";
    /// <summary><c>UNDER_REVIEW</c></summary>
    public const string UnderReview = "UNDER_REVIEW";
    /// <summary><c>PENDING_ADDITIONAL_INFO</c></summary>
    public const string PendingAdditionalInfo = "PENDING_ADDITIONAL_INFO";
    /// <summary><c>RESCREEN_REQUIRED</c></summary>
    public const string RescreenRequired = "RESCREEN_REQUIRED";
    /// <summary><c>REOPENED</c></summary>
    public const string Reopened = "REOPENED";
    /// <summary><c>APPROVED</c></summary>
    public const string Approved = "APPROVED";
    /// <summary><c>REJECTED</c></summary>
    public const string Rejected = "REJECTED";
    /// <summary><c>MANUAL_REVIEW</c></summary>
    public const string ManualReview = "MANUAL_REVIEW";
    /// <summary><c>EXPIRED</c></summary>
    public const string Expired = "EXPIRED";
    /// <summary><c>ABANDONED</c></summary>
    public const string Abandoned = "ABANDONED";
    /// <summary><c>CANCELLED</c></summary>
    public const string Cancelled = "CANCELLED";
    /// <summary>Every session status.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Created, Initiated, DocumentSubmitted, Processing, AwaitingProviderCallback, AwaitingAdditionalDocument, AwaitingUserAction,
        UnderReview, PendingAdditionalInfo, RescreenRequired, Reopened, Approved, Rejected, ManualReview, Expired, Abandoned, Cancelled,
    ];
    /// <summary>Terminal states: the session will not change again.</summary>
    public static IReadOnlyList<string> TerminalStates { get; } = [Approved, Rejected, ManualReview, Expired, Abandoned, Cancelled];

    /// <summary>True for <c>APPROVED</c>, <c>REJECTED</c>, <c>MANUAL_REVIEW</c>, <c>EXPIRED</c>, <c>ABANDONED</c>, <c>CANCELLED</c> (case-insensitive).</summary>
    public static bool IsTerminal(string? status) => status is not null && TerminalStates.Contains(status.ToUpperInvariant());
}

/// <summary><c>evidencePurpose</c> of an <c>EVIDENCE</c> session.</summary>
public static class KycEvidencePurposes
{
    /// <summary><c>IDENTITY_VERIFICATION</c></summary>
    public const string IdentityVerification = "IDENTITY_VERIFICATION";
    /// <summary><c>BUSINESS_VERIFICATION</c></summary>
    public const string BusinessVerification = "BUSINESS_VERIFICATION";
    /// <summary><c>PROOF_OF_FUNDS</c></summary>
    public const string ProofOfFunds = "PROOF_OF_FUNDS";
    /// <summary><c>PROOF_OF_ADDRESS</c></summary>
    public const string ProofOfAddress = "PROOF_OF_ADDRESS";
    /// <summary><c>SOURCE_OF_FUNDS</c></summary>
    public const string SourceOfFunds = "SOURCE_OF_FUNDS";
    /// <summary><c>SOURCE_OF_WEALTH</c></summary>
    public const string SourceOfWealth = "SOURCE_OF_WEALTH";
    /// <summary><c>TRANSACTION_EVIDENCE</c></summary>
    public const string TransactionEvidence = "TRANSACTION_EVIDENCE";
    /// <summary><c>CARD_OWNERSHIP</c></summary>
    public const string CardOwnership = "CARD_OWNERSHIP";
    /// <summary><c>PAYMENT_METHOD_OWNERSHIP</c></summary>
    public const string PaymentMethodOwnership = "PAYMENT_METHOD_OWNERSHIP";
    /// <summary><c>PAYOUT_EVIDENCE</c></summary>
    public const string PayoutEvidence = "PAYOUT_EVIDENCE";
    /// <summary><c>MERCHANT_ONBOARDING</c></summary>
    public const string MerchantOnboarding = "MERCHANT_ONBOARDING";
    /// <summary>Every evidence purpose.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        IdentityVerification, BusinessVerification, ProofOfFunds, ProofOfAddress, SourceOfFunds, SourceOfWealth, TransactionEvidence,
        CardOwnership, PaymentMethodOwnership, PayoutEvidence, MerchantOnboarding,
    ];
}

/// <summary>The assurance ladder (rank-aligned with NIST 800-63 IAL / eIDAS LoA). A gate passes when achieved ≥ required.</summary>
public static class KycAssuranceLevels
{
    /// <summary><c>NONE</c></summary>
    public const string None = "NONE";
    /// <summary><c>NOT_PERFORMED</c></summary>
    public const string NotPerformed = "NOT_PERFORMED";
    /// <summary><c>LOW</c></summary>
    public const string Low = "LOW";
    /// <summary><c>SUBSTANTIAL</c></summary>
    public const string Substantial = "SUBSTANTIAL";
    /// <summary><c>HIGH</c></summary>
    public const string High = "HIGH";
    /// <summary>Every assurance level.</summary>
    public static IReadOnlyList<string> All { get; } = [None, NotPerformed, Low, Substantial, High];

    /// <summary>The rank on the ladder: <c>HIGH</c> 3, <c>SUBSTANTIAL</c> 2, <c>LOW</c> 1, anything else 0 (case-insensitive).</summary>
    public static int Rank(string? level) => level?.ToUpperInvariant() switch
    {
        High => 3,
        Substantial => 2,
        Low => 1,
        _ => 0,
    };

    /// <summary>True when <paramref name="achieved"/> meets or exceeds <paramref name="required"/>.</summary>
    public static bool Meets(string? achieved, string? required) => Rank(achieved) >= Rank(required);
}

/// <summary>Domains of the achieved assurance snapshot (<c>session.decision.assuranceSnapshot.achieved</c>), each an assurance level.</summary>
public static class KycAssuranceDomains
{
    /// <summary><c>identity_evidence_strength</c></summary>
    public const string Identity = "identity_evidence_strength";
    /// <summary><c>document_authenticity_level</c></summary>
    public const string Document = "document_authenticity_level";
    /// <summary><c>possession_biometric_binding</c></summary>
    public const string Biometric = "possession_biometric_binding";
    /// <summary><c>authoritative_source_level</c></summary>
    public const string Authoritative = "authoritative_source_level";
    /// <summary><c>address_assurance</c></summary>
    public const string Address = "address_assurance";
    /// <summary><c>aml_freshness</c></summary>
    public const string Aml = "aml_freshness";
    /// <summary><c>human_review_level</c></summary>
    public const string HumanReview = "human_review_level";
    /// <summary>Every assurance domain.</summary>
    public static IReadOnlyList<string> All { get; } = [Identity, Document, Biometric, Authoritative, Address, Aml, HumanReview];
}

/// <summary>
/// KYC webhook event types (same signed envelope as payment events). <c>data</c> always carries <c>sessionId</c> and
/// <c>externalCustomerId</c>; terminal events carry the decision. A rescreen or screening hit on an approved session must revoke access.
/// </summary>
public static class KycEvents
{
    /// <summary><c>kyc.session.created</c></summary>
    public const string SessionCreated = "kyc.session.created";
    /// <summary><c>kyc.session.initiated</c></summary>
    public const string SessionInitiated = "kyc.session.initiated";
    /// <summary><c>kyc.session.expired</c></summary>
    public const string SessionExpired = "kyc.session.expired";
    /// <summary><c>kyc.session.abandoned</c></summary>
    public const string SessionAbandoned = "kyc.session.abandoned";
    /// <summary><c>kyc.session.cancelled</c></summary>
    public const string SessionCancelled = "kyc.session.cancelled";
    /// <summary><c>kyc.document.received</c></summary>
    public const string DocumentReceived = "kyc.document.received";
    /// <summary><c>kyc.document.rejected</c></summary>
    public const string DocumentRejected = "kyc.document.rejected";
    /// <summary><c>kyc.liveness.completed</c></summary>
    public const string LivenessCompleted = "kyc.liveness.completed";
    /// <summary><c>kyc.processing.started</c></summary>
    public const string ProcessingStarted = "kyc.processing.started";
    /// <summary><c>kyc.manual_review.required</c></summary>
    public const string ManualReviewRequired = "kyc.manual_review.required";
    /// <summary><c>kyc.approved</c></summary>
    public const string Approved = "kyc.approved";
    /// <summary><c>kyc.rejected</c></summary>
    public const string Rejected = "kyc.rejected";
    /// <summary><c>kyc.rescreen.required</c></summary>
    public const string RescreenRequired = "kyc.rescreen.required";
    /// <summary><c>kyb.session.created</c></summary>
    public const string KybSessionCreated = "kyb.session.created";
    /// <summary><c>kyb.company.verified</c></summary>
    public const string KybCompanyVerified = "kyb.company.verified";
    /// <summary><c>kyb.company.not_found</c></summary>
    public const string KybCompanyNotFound = "kyb.company.not_found";
    /// <summary><c>kyb.ubo.pending</c></summary>
    public const string KybUboPending = "kyb.ubo.pending";
    /// <summary><c>kyb.related_kyc.required</c></summary>
    public const string KybRelatedKycRequired = "kyb.related_kyc.required";
    /// <summary><c>kyb.related_kyc.completed</c></summary>
    public const string KybRelatedKycCompleted = "kyb.related_kyc.completed";
    /// <summary><c>kyb.manual_review.required</c></summary>
    public const string KybManualReviewRequired = "kyb.manual_review.required";
    /// <summary><c>kyb.approved</c></summary>
    public const string KybApproved = "kyb.approved";
    /// <summary><c>kyb.rejected</c></summary>
    public const string KybRejected = "kyb.rejected";
    /// <summary><c>screening.match.detected</c></summary>
    public const string ScreeningMatchDetected = "screening.match.detected";
    /// <summary><c>screening.false_positive.cleared</c></summary>
    public const string ScreeningFalsePositiveCleared = "screening.false_positive.cleared";
    /// <summary><c>ongoing_monitoring.alert.created</c></summary>
    public const string MonitoringAlertCreated = "ongoing_monitoring.alert.created";
    /// <summary>Every KYC, KYB, screening and monitoring event type.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        SessionCreated, SessionInitiated, SessionExpired, SessionAbandoned, SessionCancelled, DocumentReceived, DocumentRejected,
        LivenessCompleted, ProcessingStarted, ManualReviewRequired, Approved, Rejected, RescreenRequired, KybSessionCreated,
        KybCompanyVerified, KybCompanyNotFound, KybUboPending, KybRelatedKycRequired, KybRelatedKycCompleted, KybManualReviewRequired,
        KybApproved, KybRejected, ScreeningMatchDetected, ScreeningFalsePositiveCleared, MonitoringAlertCreated,
    ];
}

/// <summary>
/// The single description of every KYC route the SDK calls (<c>/api/v1/kyc/*</c>): method, path template, the
/// <c>Idempotency-Key</c> rule and the body and query fields. The surface is bearer-only. The browser bootstraps its own SDK
/// token with the publishable key (<c>/kyc/sdk/init</c>); that route is not reachable from a server key and is not here. The
/// table is the Node SDK's <c>KYC_ROUTES</c>, row for row, and the shared catalog's group of the same name.
/// </summary>
public static class KycRoutes
{
    private const string Base = "/api/v1/kyc";

    /// <summary>The longest session lifetime <c>ttlMinutes</c> accepts (one day).</summary>
    public const int SessionTtlMaxMinutes = 1440;

    /// <summary>Optional fields of a session body (<c>createSession</c>, <c>evidenceRequest</c>) besides <c>subjectId</c> and <c>sessionType</c>.</summary>
    public static IReadOnlyList<string> SessionFields { get; } =
    [
        "externalCustomerId", "customerMode", "customer", "jurisdiction", "requiredDocumentTypes",
        "primaryDocumentType", "evidencePurpose", "ttlMinutes", "idempotencyKey", "evidenceContextJson",
    ];

    /// <summary>Every route by name, in the Node table's order.</summary>
    public static IReadOnlyDictionary<string, LunixiRoute> All { get; } = Build();

    private static IReadOnlyDictionary<string, LunixiRoute> Build()
    {
        var session = SessionFields.ToArray();
        var routes = new OrderedRoutes
        {
            ["createSession"] = LunixiRoute.Post(Base + "/sessions").KeyAccepted().WithRequired("subjectId", "sessionType").WithOptional(session),
            ["getSession"] = LunixiRoute.Get(Base + "/sessions/:sessionId"),
            // The merchant list takes only these two; an unknown filter is a 400 on the gateway.
            ["listSessions"] = LunixiRoute.Get(Base + "/sessions").WithQuery("limit", "cursor"),
            ["setJurisdiction"] = LunixiRoute.Post(Base + "/sessions/:sessionId/jurisdiction").WithRequired("jurisdiction"),
            ["setPrimaryDocument"] = LunixiRoute.Post(Base + "/sessions/:sessionId/primary-document").WithRequired("documentType"),
            ["uploadDocument"] = LunixiRoute.Post(Base + "/sessions/:sessionId/documents")
                .WithRequired("documentType", "fileBase64", "mimeType").WithOptional("side", "jurisdiction", "metadataJson", "idempotencyKey"),
            ["timeline"] = LunixiRoute.Get(Base + "/sessions/:sessionId/timeline"),
            ["submit"] = LunixiRoute.Post(Base + "/sessions/:sessionId/submit"),
            ["manualReview"] = LunixiRoute.Post(Base + "/sessions/:sessionId/manual-review").WithOptional("reasonCode", "reasonDetail").WithOptionalBody(),
            ["evidenceRequest"] = LunixiRoute.Post(Base + "/evidence-requests").KeyAccepted().WithRequired("subjectId", "sessionType").WithOptional(session),
            ["merchantPolicy"] = LunixiRoute.Get(Base + "/admin/merchant-policy"),
            ["updateMerchantPolicy"] = LunixiRoute.Post(Base + "/admin/merchant-policy").AsFree(),
            ["rotateWebhookSecret"] = LunixiRoute.Post(Base + "/admin/merchant-policy/webhook-secret/rotate"),
            ["webhookOutbox"] = LunixiRoute.Get(Base + "/admin/webhook-outbox").WithQuery("limit", "status"),
            ["retryWebhookEvent"] = LunixiRoute.Post(Base + "/admin/webhook-outbox/:eventId/retry"),
            ["erasureRequests"] = LunixiRoute.Get(Base + "/admin/erasure-requests").WithQuery("limit", "status"),
        };
        return routes.Freeze();
    }
}
