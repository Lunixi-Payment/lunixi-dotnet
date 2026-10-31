using System.Text.Json;

namespace Lunixi;

/// <summary>A validated split-payment DTO. It accepts tokenized cards and the gateway's currently supported method blocks.</summary>
public sealed class SplitPaymentRequest
{
    internal static readonly HashSet<string> InProgress = ["COMPOSING", "AUTHORIZING", "AWAITING_3DS", "AWAITING_ACTION", "AWAITING_CREDIT_APPROVAL", "AWAITING_REPLACEMENT_CARD", "AUTHORIZED", "CAPTURING"];
    private static readonly string[] MethodBlocks = ["mobileBilling", "wallet", "promissoryNote"];
    private static readonly string[] LegFields = ["paymentMethod", "cardToken", "amount", "force3D", "installment", "installmentQuoteToken", .. MethodBlocks];
    /// <summary>The validated immutable JSON request body.</summary>
    public JsonElement Payload { get; }
    /// <summary>Validates the public split DTO, including exact leg totals, known fields and distinct card/provider references.</summary>
    public SplitPaymentRequest(object payload)
    {
        var body = CanonicalJson.Element(payload);
        RequestValidation.Fields(body, ["totalAmount", "currency", "ownerRef", "legs", "orderId", "ownerScope", "callbackUrl"]);
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
                var blockName = method switch { "MOBILE_BILLING" => "mobileBilling", "WALLET" => "wallet", "PROMISSORY_NOTE" => "promissoryNote", _ => throw new LunixiConfigurationException("Unsupported split payment method block.") };
                if (new[] { "cardToken", "force3D", "installmentQuoteToken" }.Any(b => leg.TryGetProperty(b, out _)))
                    throw new LunixiConfigurationException("Non-card legs cannot contain card-only fields.");
                if (MethodBlocks.Any(b => b != blockName && leg.TryGetProperty(b, out _))) throw new LunixiConfigurationException("A method leg must contain exactly its own block.");
                if (!leg.TryGetProperty(blockName, out var block) || block.ValueKind != JsonValueKind.Object)
                    throw new LunixiConfigurationException("The payment method's provider block is required.");
                RequestValidation.MethodBlock(block, true);
                var provider = RequestValidation.RequiredText(block, "providerCode", 64);
                if (provider.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-')) || !providers.Add(provider))
                    throw new LunixiConfigurationException("Each split leg needs one distinct provider code.");
                if (block.EnumerateObject().Count() > 33) throw new LunixiConfigurationException("A method block can contain at most 32 input fields and providerCode.");
                if (method == "PROMISSORY_NOTE")
                {
                    if (block.TryGetProperty("schedule", out _) || !block.TryGetProperty("noteCount", out var count) || count.ValueKind != JsonValueKind.Number || !count.TryGetInt32(out var notes) || notes is < 1 or > 24)
                        throw new LunixiConfigurationException("Promissory-note legs require noteCount from one to 24 and cannot contain schedule.");
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
