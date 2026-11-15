using System.Text.Json;

namespace Lunixi;

/// <summary>Field lists of the split-payment endpoints (gateway DTOs; an unknown field is rejected with HTTP 400).</summary>
public static class SplitPaymentFields
{
    /// <summary>Body fields of <c>POST /api/v1/payments/split</c>.</summary>
    public static IReadOnlyList<string> Payment { get; } = Array.AsReadOnly(new[] { "totalAmount", "currency", "ownerRef", "legs", "orderId", "ownerScope", "callbackUrl" });
    /// <summary>
    /// Method blocks a non-card leg can carry: <c>mobileBilling</c>, <c>wallet</c>, <c>promissoryNote</c>. Meal card, open
    /// banking, shopping loan and bank transfer cannot be split payment legs.
    /// </summary>
    public static IReadOnlyList<string> LegBlocks { get; } = Array.AsReadOnly(new[] { "mobileBilling", "wallet", "promissoryNote" });
    /// <summary>Fields of one leg.</summary>
    public static IReadOnlyList<string> Leg { get; } = Array.AsReadOnly(new[] { "paymentMethod", "cardToken", "amount", "force3D", "installment", "installmentQuoteToken", "mobileBilling", "wallet", "promissoryNote" });
    /// <summary>Filters of <c>GET /api/v1/payments/split</c> (<see cref="PaymentsClient.ListSplitsAsync"/>).</summary>
    public static IReadOnlyList<string> ListFilters { get; } = Array.AsReadOnly(new[] { "status", "currency", "createdFrom", "createdTo", "orderId", "ownerRef", "limit", "after" });
}

/// <summary>
/// Webhook event types of a split payment. The main payment's result arrives as <c>payment.captured</c> /
/// <c>payment.failed</c> with <c>settlementMode: SPLIT</c> and <c>legs[]</c>; deliver the order on that event.
/// </summary>
public static class SplitPaymentEvents
{
    /// <summary><c>payment.split.completed</c>: every leg was charged.</summary>
    public const string Completed = "payment.split.completed";
    /// <summary><c>payment.split.voided</c>: failed without a charge left on any leg.</summary>
    public const string Voided = "payment.split.voided";
    /// <summary><c>payment.split.compensating</c>: a leg failed; holds are released and charged legs are refunded.</summary>
    public const string Compensating = "payment.split.compensating";
    /// <summary><c>payment.split.outcome_pending</c>: the non-reversible leg's result is being confirmed.</summary>
    public const string OutcomePending = "payment.split.outcome_pending";
    /// <summary><c>payment.split.reconciliation_required</c>: the order could not be fully collected.</summary>
    public const string ReconciliationRequired = "payment.split.reconciliation_required";
    /// <summary><c>payment.split.leg_refund_pending</c>: a charged leg is being refunded and retried until it succeeds.</summary>
    public const string LegRefundPending = "payment.split.leg_refund_pending";
    /// <summary>
    /// <c>payment.split.leg_events</c>: add this key to a webhook endpoint's event types to also receive the events of
    /// each leg. It is a subscription key, not an event type, and <c>payment.*</c> does not include it.
    /// </summary>
    public const string LegEventsSubscription = "payment.split.leg_events";
}

/// <summary>A validated split-payment DTO. It accepts tokenized cards and the gateway's currently supported method blocks.</summary>
public sealed class SplitPaymentRequest
{
    internal static readonly HashSet<string> InProgress = ["COMPOSING", "AUTHORIZING", "AWAITING_3DS", "AWAITING_ACTION", "AWAITING_CREDIT_APPROVAL", "AWAITING_REPLACEMENT_CARD", "AUTHORIZED", "CAPTURING"];
    private static IReadOnlyList<string> MethodBlocks => SplitPaymentFields.LegBlocks;
    private static IReadOnlyList<string> LegFields => SplitPaymentFields.Leg;
    /// <summary>The validated immutable JSON request body.</summary>
    public JsonElement Payload { get; }
    /// <summary>Validates the public split DTO, including exact leg totals, known fields and distinct card/provider references.</summary>
    public SplitPaymentRequest(object payload)
    {
        var body = CanonicalJson.Element(payload);
        RequestValidation.Fields(body, SplitPaymentFields.Payment);
        var amount = RequestValidation.PositiveAmount(body, "totalAmount");
        RequestValidation.Currency(RequestValidation.RequiredText(body, "currency", 3));
        RequestValidation.RequiredText(body, "ownerRef", 256);
        if (body.TryGetProperty("ownerScope", out var scope) && (scope.ValueKind != JsonValueKind.String || scope.GetString() != "MERCHANT_CUSTOMER"))
            throw new LunixiConfigurationException("ownerScope must be MERCHANT_CUSTOMER.");
        if (body.TryGetProperty("orderId", out _)) RequestValidation.RequiredText(body, "orderId", 256);
        var callback = LunixiClient.Property(body, "callbackUrl");
        if (body.TryGetProperty("callbackUrl", out _) && callback is null) throw new LunixiConfigurationException("callbackUrl must be an HTTPS URL string.");
        if (callback is not null)
        {
            if (!Uri.TryCreate(callback, UriKind.Absolute, out var callbackUri)) throw new LunixiConfigurationException("Invalid callback URL.");
            RequestValidation.Https(callbackUri);
        }
        if (!body.TryGetProperty("legs", out var legs) || legs.ValueKind != JsonValueKind.Array || legs.GetArrayLength() is < 1 or > 5)
            throw new LunixiConfigurationException("A split payment must contain one to five legs.");
        var cards = new HashSet<string>(StringComparer.Ordinal); var providers = new HashSet<string>(StringComparer.Ordinal);
        long sum = 0;
        foreach (var leg in legs.EnumerateArray())
        {
            RequestValidation.Fields(leg, LegFields);
            var legAmount = RequestValidation.PositiveAmount(leg, "amount");
            if (sum > amount - legAmount) throw new LunixiConfigurationException("Split leg amounts must sum exactly to totalAmount.");
            sum += legAmount;
            var method = leg.TryGetProperty("paymentMethod", out _) ? RequestValidation.RequiredText(leg, "paymentMethod", 32) : "CARD";
            if (method == "CARD")
            {
                var card = RequestValidation.RequiredText(leg, "cardToken", 256);
                if (!cards.Add(card)) throw new LunixiConfigurationException("A card token cannot appear in more than one leg.");
                if (MethodBlocks.Any(b => leg.TryGetProperty(b, out _))) throw new LunixiConfigurationException("Card legs cannot contain another payment-method block.");
                if (leg.TryGetProperty("force3D", out var force))
                {
                    if (force.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new LunixiConfigurationException("force3D must be a JSON boolean.");
                    if (force.GetBoolean() && callback is null) throw new LunixiConfigurationException("A 3DS card leg requires callbackUrl.");
                }
                if (leg.TryGetProperty("installment", out var installment))
                {
                    if (installment.ValueKind != JsonValueKind.Number || !installment.TryGetInt32(out var n) || n is < 1 or > 36) throw new LunixiConfigurationException("Installment must be between one and 36.");
                    if (n >= 2) RequestValidation.RequiredText(leg, "installmentQuoteToken", 4096);
                }
                if (leg.TryGetProperty("installmentQuoteToken", out _)) RequestValidation.RequiredText(leg, "installmentQuoteToken", 4096);
            }
            else
            {
                var code = PaymentMethodValidation.Normalize(method);
                var blockName = PaymentMethodValidation.BlockName(code);
                if (!MethodBlocks.Contains(blockName))
                    throw new LunixiConfigurationException($"paymentMethod {code} cannot be a split payment leg; a non-card leg carries one of the blocks {string.Join(", ", MethodBlocks)}.");
                if (new[] { "cardToken", "force3D", "installmentQuoteToken" }.Any(b => leg.TryGetProperty(b, out _)))
                    throw new LunixiConfigurationException("Non-card legs cannot contain card-only fields.");
                if (MethodBlocks.Any(b => b != blockName && leg.TryGetProperty(b, out _))) throw new LunixiConfigurationException("A method leg must contain exactly its own block.");
                if (!leg.TryGetProperty(blockName, out var block) || block.ValueKind != JsonValueKind.Object)
                    throw new LunixiConfigurationException("The payment method's provider block is required.");
                RequestValidation.MethodBlock(block, ProviderCodeRule.Required, blockName);
                var provider = RequestValidation.RequiredText(block, "providerCode", 64);
                if (!providers.Add(provider))
                    throw new LunixiConfigurationException("Each split leg needs one distinct provider code.");
                if (code == PaymentMethods.PromissoryNote)
                {
                    if (block.TryGetProperty("schedule", out _) || !block.TryGetProperty("noteCount", out var count))
                        throw new LunixiConfigurationException("Promissory-note legs require noteCount from one to 24 and cannot contain schedule.");
                    PromissoryNotePlan.NoteCount(count);
                }
                if (leg.TryGetProperty("installment", out var nonCardInstallment) && (nonCardInstallment.ValueKind != JsonValueKind.Number || !nonCardInstallment.TryGetInt32(out var n) || n != 1))
                    throw new LunixiConfigurationException("Installments above one require a card leg.");
            }
        }
        if (sum != amount) throw new LunixiConfigurationException("Split leg amounts must sum exactly to totalAmount.");
        Payload = body;
    }
    /// <summary>Returns no stored-card tokens or provider input values.</summary>
    public override string ToString() => "SplitPaymentRequest { RequestData = [REDACTED] }";
}
