using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lunixi;

/// <summary>
/// The reconciliation CSV of <see cref="InsuranceClient.ExportReconciliationAsync"/> with its trailer read: a cut file ends with
/// <c># truncated=true orderCap=20000 nextCursor=…</c>, an interrupted one with <c># export-aborted=true</c>.
/// </summary>
public sealed class InsuranceReconciliationExport
{
    private static readonly Regex TruncatedLine = new(@"^# truncated=true orderCap=(\d+) nextCursor=(\S+)\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex AbortedLine = new(@"^# export-aborted=true\s*$", RegexOptions.CultureInvariant);

    /// <summary>The CSV file (UTF-8 with BOM, one line per policy, amounts in minor units).</summary>
    public LunixiBinaryResponse File { get; }
    /// <summary>True when the file was cut at the order cap; continue with <see cref="NextCursor"/>.</summary>
    public bool Truncated { get; }
    /// <summary>The cursor of the next file when <see cref="Truncated"/>, otherwise null.</summary>
    public string? NextCursor { get; }
    /// <summary>True when the file ends with the gateway's abort line: it is incomplete, request it again.</summary>
    public bool Aborted { get; }

    internal InsuranceReconciliationExport(LunixiBinaryResponse file)
    {
        File = file;
        var last = LastLine(file.Content.Span);
        var truncated = TruncatedLine.Match(last);
        Truncated = truncated.Success;
        NextCursor = truncated.Success ? truncated.Groups[2].Value : null;
        Aborted = AbortedLine.IsMatch(last);
    }

    /// <summary>The last non-empty line, read from the end of the file (the trailer is ASCII).</summary>
    private static string LastLine(ReadOnlySpan<byte> content)
    {
        var tail = content.Length > 8192 ? content[^8192..] : content;
        var lines = Encoding.UTF8.GetString(tail).Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0).ToArray();
        return lines.Length == 0 ? "" : lines[^1];
    }

    /// <summary>Returns no file content.</summary>
    public override string ToString() => $"InsuranceReconciliationExport {{ Length = {File.Content.Length}, Truncated = {Truncated}, Aborted = {Aborted} }}";
}

/// <summary>The insurance provider of an order webhook (<c>provider</c>).</summary>
/// <param name="Code">The provider code; read provider codes from the API, never hard-code them.</param>
/// <param name="Name">The provider's display name.</param>
/// <param name="LogoUrl">The provider's logo.</param>
/// <param name="ConnectionId">Your provider connection.</param>
public sealed record InsuranceWebhookProvider(string? Code, string? Name, string? LogoUrl, string? ConnectionId);

/// <summary>One policy of an order webhook (<c>policies[]</c>). <see cref="DocumentUrl"/> is empty unless the policy is in force; downloading it is a signed API call.</summary>
/// <param name="PolicyId">The policy id (<c>insp_…</c>).</param>
/// <param name="BasketItemId">The insured basket item, or null for insurance sold without one.</param>
/// <param name="PolicyNumber">The provider's policy number.</param>
/// <param name="PolicyStatus">The <see cref="InsurancePolicyStatuses"/> value.</param>
/// <param name="DocumentStatus">The <see cref="InsuranceDocumentStatuses"/> value.</param>
/// <param name="DocumentUrl">The policy document address (<see cref="InsuranceClient.GetPolicyDocumentAsync"/>), or empty.</param>
public sealed record InsuranceWebhookPolicy(string? PolicyId, string? BasketItemId, string? PolicyNumber, string? PolicyStatus, string? DocumentStatus, string? DocumentUrl);

/// <summary>
/// The <c>data</c> of a verified order <c>insurance.*</c> webhook (<see cref="InsuranceEvents.OrderEvents"/>). No other key is
/// delivered and no personal data. Decide on the order from the webhook; deduplicate on <see cref="EventId"/>.
/// </summary>
public sealed class InsuranceWebhookData
{
    /// <summary>The event id (the same as the envelope's id).</summary>
    public string EventId { get; }
    /// <summary>The event type, for example <see cref="InsuranceEvents.PolicyIssued"/>.</summary>
    public string EventType { get; }
    /// <summary>Your merchant id.</summary>
    public string? MerchantId { get; }
    /// <summary>The order id (<c>inso_…</c>).</summary>
    public string InsuranceOrderId { get; }
    /// <summary>The Lunixi payment bound to the order, or null.</summary>
    public string? PaymentIntentId { get; }
    /// <summary>Your order id, or null.</summary>
    public string? MerchantOrderId { get; }
    /// <summary>The insurance type.</summary>
    public string? InsuranceType { get; }
    /// <summary>The <see cref="InsurancePremiumCollectedBy"/> value.</summary>
    public string? PremiumCollectedBy { get; }
    /// <summary>The insurance provider, or null when the payload has none.</summary>
    public InsuranceWebhookProvider? Provider { get; }
    /// <summary>The <see cref="InsuranceOrderStatuses"/> value.</summary>
    public string? Status { get; }
    /// <summary>The <see cref="InsuranceCollectionStatuses"/> value.</summary>
    public string? CollectionStatus { get; }
    /// <summary>The <see cref="InsurancePolicyStatuses"/> value.</summary>
    public string? PolicyStatus { get; }
    /// <summary>The <see cref="InsuranceRefundStatuses"/> value.</summary>
    public string? RefundStatus { get; }
    /// <summary>The <see cref="InsuranceDocumentStatuses"/> value.</summary>
    public string? DocumentStatus { get; }
    /// <summary>The premium in minor units, or null.</summary>
    public string? PremiumAmount { get; }
    /// <summary>The premium currency.</summary>
    public string? Currency { get; }
    /// <summary>The order's policies.</summary>
    public IReadOnlyList<InsuranceWebhookPolicy> Policies { get; }
    /// <summary>Why, on <see cref="InsuranceEvents.PolicyFailed"/> and <see cref="InsuranceEvents.RefundRequired"/>; otherwise null.</summary>
    public string? ErrorCode { get; }
    /// <summary>When the event was created.</summary>
    public string? CreatedAt { get; }
    /// <summary>The whole <c>data</c> object.</summary>
    public JsonElement Data { get; }

    private InsuranceWebhookData(LunixiWebhook verified, JsonElement data)
    {
        Data = data;
        EventId = WebhookData.Text(data, "eventId") ?? verified.Id; EventType = WebhookData.Text(data, "eventType") ?? verified.Type;
        MerchantId = WebhookData.Text(data, "merchantId"); InsuranceOrderId = WebhookData.Required(data, "insuranceOrderId");
        PaymentIntentId = WebhookData.Text(data, "paymentIntentId"); MerchantOrderId = WebhookData.Text(data, "merchantOrderId");
        InsuranceType = WebhookData.Text(data, "insuranceType"); PremiumCollectedBy = WebhookData.Text(data, "premiumCollectedBy");
        Provider = data.TryGetProperty("provider", out var provider) && provider.ValueKind == JsonValueKind.Object
            ? new InsuranceWebhookProvider(WebhookData.Text(provider, "code"), WebhookData.Text(provider, "name"), WebhookData.Text(provider, "logoUrl"),
                WebhookData.Text(provider, "connectionId"))
            : null;
        Status = WebhookData.Text(data, "status"); CollectionStatus = WebhookData.Text(data, "collectionStatus");
        PolicyStatus = WebhookData.Text(data, "policyStatus"); RefundStatus = WebhookData.Text(data, "refundStatus");
        DocumentStatus = WebhookData.Text(data, "documentStatus"); PremiumAmount = WebhookData.Money(data, "premiumAmount", false);
        Currency = WebhookData.Text(data, "currency"); ErrorCode = WebhookData.Text(data, "errorCode"); CreatedAt = WebhookData.Text(data, "createdAt");
        Policies = data.TryGetProperty("policies", out var policies) && policies.ValueKind == JsonValueKind.Array
            ? policies.EnumerateArray().Where(policy => policy.ValueKind == JsonValueKind.Object).Select(policy => new InsuranceWebhookPolicy(
                WebhookData.Text(policy, "policyId"), WebhookData.Text(policy, "basketItemId"), WebhookData.Text(policy, "policyNumber"),
                WebhookData.Text(policy, "policyStatus"), WebhookData.Text(policy, "documentStatus"), LunixiClient.Property(policy, "documentUrl"))).ToArray()
            : [];
    }

    /// <summary>
    /// Reads a webhook already verified by <see cref="WebhookVerifier"/>. Throws <see cref="LunixiWebhookException"/> for an
    /// event that is not an order <c>insurance.*</c> event (a lead event included), a missing payload or order id, or a
    /// non-canonical <c>premiumAmount</c>.
    /// </summary>
    public static InsuranceWebhookData FromWebhook(LunixiWebhook verified)
    {
        ArgumentNullException.ThrowIfNull(verified);
        if (!verified.Type.StartsWith("insurance.", StringComparison.Ordinal) || verified.Type.StartsWith("insurance.lead.", StringComparison.Ordinal))
            throw new LunixiWebhookException("Not an insurance order webhook event.");
        return new InsuranceWebhookData(verified, WebhookData.Payload(verified));
    }

    /// <summary>Returns only the event type.</summary>
    public override string ToString() => $"InsuranceWebhookData {{ EventType = {EventType}, Data = [REDACTED] }}";
}

/// <summary>The lead of an <c>insurance.lead.*</c> webhook; no personal data, no note, no provider reference.</summary>
/// <param name="LeadId">The lead id.</param>
/// <param name="InsuranceType">The insurance type.</param>
/// <param name="SchemaVersion">The lead schema version, or null.</param>
/// <param name="Status">The <see cref="InsuranceLeadStatuses"/> value.</param>
/// <param name="SubmittedAt">When the provider received it, or null.</param>
/// <param name="CreatedAt">When the lead was created.</param>
public sealed record InsuranceWebhookLead(string? LeadId, string? InsuranceType, int? SchemaVersion, string? Status, string? SubmittedAt, string? CreatedAt);

/// <summary>The <c>data</c> of a verified <c>insurance.lead.*</c> webhook (<see cref="InsuranceEvents.LeadEvents"/>); no other key is delivered.</summary>
public sealed class InsuranceLeadWebhookData
{
    /// <summary>The event id (the same as the envelope's id).</summary>
    public string EventId { get; }
    /// <summary>The event type, for example <see cref="InsuranceEvents.LeadSubmitted"/>.</summary>
    public string EventType { get; }
    /// <summary>Your merchant id.</summary>
    public string? MerchantId { get; }
    /// <summary>The lead summary.</summary>
    public InsuranceWebhookLead Lead { get; }
    /// <summary>When the event was created.</summary>
    public string? CreatedAt { get; }
    /// <summary>The whole <c>data</c> object.</summary>
    public JsonElement Data { get; }

    private InsuranceLeadWebhookData(LunixiWebhook verified, JsonElement data)
    {
        Data = data;
        EventId = WebhookData.Text(data, "eventId") ?? verified.Id; EventType = WebhookData.Text(data, "eventType") ?? verified.Type;
        MerchantId = WebhookData.Text(data, "merchantId"); CreatedAt = WebhookData.Text(data, "createdAt");
        if (!data.TryGetProperty("lead", out var lead) || lead.ValueKind != JsonValueKind.Object) throw new LunixiWebhookException("The webhook payload has no lead.");
        int? schemaVersion = lead.TryGetProperty("schemaVersion", out var version) && version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var number) ? number : null;
        Lead = new InsuranceWebhookLead(WebhookData.Required(lead, "leadId"), WebhookData.Text(lead, "insuranceType"), schemaVersion,
            WebhookData.Text(lead, "status"), WebhookData.Text(lead, "submittedAt"), WebhookData.Text(lead, "createdAt"));
    }

    /// <summary>Reads a webhook already verified by <see cref="WebhookVerifier"/>; throws <see cref="LunixiWebhookException"/> for an event that is not <c>insurance.lead.*</c> or carries no lead.</summary>
    public static InsuranceLeadWebhookData FromWebhook(LunixiWebhook verified)
    {
        ArgumentNullException.ThrowIfNull(verified);
        if (!verified.Type.StartsWith("insurance.lead.", StringComparison.Ordinal)) throw new LunixiWebhookException("Not an insurance lead webhook event.");
        return new InsuranceLeadWebhookData(verified, WebhookData.Payload(verified));
    }

    /// <summary>Returns only the event type.</summary>
    public override string ToString() => $"InsuranceLeadWebhookData {{ EventType = {EventType}, Data = [REDACTED] }}";
}
