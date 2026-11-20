using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Lunixi;

/// <summary>
/// Body of <c>POST /api/v1/bin/installments</c>: the installment table for an amount and, with a BIN, the binding
/// <c>quoteToken</c> for an installment count above one. Card digits are never part of <see cref="ToString"/> or of an
/// error message.
/// </summary>
public sealed class InstallmentOptionsRequest
{
    /// <summary>Positive amount in minor units, the basket total without the installment fee.</summary>
    public string Amount { get; }
    /// <summary>The gateway currency code.</summary>
    public string Currency { get; }
    /// <summary>The card's first 6 or 8 digits (or a longer PAN prefix), 6–19 digits.</summary>
    public string? BinOrPan { get; init; }
    /// <summary>The installment count to quote, 1–36. A count above one with a BIN returns a <c>quoteToken</c>.</summary>
    public int? Installment { get; init; }
    /// <summary>The card brand from <see cref="PaymentsClient.CardTaxonomyAsync"/>, when no BIN is known.</summary>
    public string? CardBrand { get; init; }
    /// <summary>
    /// Limits the table to these installment counts, for example <c>[1, 2, 3]</c> for a product page that allows at most
    /// three. Same rules as <see cref="CreateIntentRequest.EnabledInstallments"/>: 1–36, distinct, 1 included; sent in
    /// ascending order. Null means no restriction.
    /// </summary>
    [JsonConverter(typeof(AscendingInstallmentsConverter))]
    public IReadOnlyList<int>? EnabledInstallments { get; init; }

    /// <summary>Starts a request with a positive minor-unit amount and a three-letter currency.</summary>
    public InstallmentOptionsRequest(string amount, string currency)
    {
        var normalizedCurrency = currency?.ToUpperInvariant() ?? "";
        Validation.Amount(amount); RequestValidation.Currency(normalizedCurrency);
        Amount = amount; Currency = normalizedCurrency;
    }

    internal void Validate()
    {
        if (BinOrPan is not null) CardValidation.BinOrPan(BinOrPan);
        if (Installment is < 1 or > 36) throw new LunixiConfigurationException("Installment must be between one and 36.");
        if (CardBrand is not null) RequestValidation.Text(CardBrand, "cardBrand", 64);
        if (EnabledInstallments is not null) RequestValidation.EnabledInstallments(EnabledInstallments);
    }

    /// <summary>Returns no card digits.</summary>
    public override string ToString() => "InstallmentOptionsRequest { RequestData = [REDACTED] }";
}

/// <summary>
/// Body of <c>POST /api/v1/payments/cards</c>: verifies a card and stores it for <c>cardUserKey</c>. The answer carries the
/// <c>storedCardToken</c> to charge later; the card number and security code are never returned, logged or placed in an
/// error message by the SDK. <see cref="Buyer"/> and <see cref="BillingAddress"/> are required (no basket; see
/// <see cref="OrderDetails"/>).
/// </summary>
public sealed class StoreCardRequest
{
    private readonly JsonElement card;
    /// <summary>Your stable reference for the card owner; stored cards are listed per key.</summary>
    public string CardUserKey { get; }
    /// <summary>The HTTPS URL the buyer's browser returns to after card verification.</summary>
    public Uri CallbackUrl { get; }
    /// <summary>Optional verification currency, a three-letter code.</summary>
    public string? Currency { get; init; }
    /// <summary>
    /// The card holder as the buyer (required, a JSON object with <see cref="OrderDetails.BuyerRequiredFields"/>; do not log it).
    /// </summary>
    public JsonElement? Buyer { get; init; }
    /// <summary>The billing address (required, a JSON object with <see cref="OrderDetails.AddressRequiredFields"/>).</summary>
    public JsonElement? BillingAddress { get; init; }

    /// <summary>
    /// Creates a request from the card fields of the public gateway card DTO (a JSON object), the owner's key and the
    /// HTTPS return URL.
    /// </summary>
    public StoreCardRequest(object card, string cardUserKey, Uri callbackUrl)
    {
        ArgumentNullException.ThrowIfNull(card); ArgumentNullException.ThrowIfNull(callbackUrl);
        var element = CanonicalJson.Element(card);
        if (element.ValueKind != JsonValueKind.Object || !element.EnumerateObject().Any())
            throw new LunixiConfigurationException("StoreCardRequest requires a non-empty card object.");
        RequestValidation.Text(cardUserKey, "cardUserKey", 256); RequestValidation.Https(callbackUrl);
        this.card = element; CardUserKey = cardUserKey; CallbackUrl = callbackUrl;
    }

    internal JsonObject ToPayload()
    {
        var body = new JsonObject
        {
            ["card"] = JsonNode.Parse(card.GetRawText()),
            ["cardUserKey"] = CardUserKey,
            ["callbackUrl"] = CallbackUrl.AbsoluteUri,
        };
        if (Currency is not null)
        {
            var normalized = Currency.ToUpperInvariant();
            RequestValidation.Currency(normalized); body["currency"] = normalized;
        }
        if (Buyer is { } buyer) body["buyer"] = JsonNode.Parse(buyer.GetRawText());
        if (BillingAddress is { } billingAddress) body["billingAddress"] = JsonNode.Parse(billingAddress.GetRawText());
        return body;
    }

    /// <summary>Returns no card data.</summary>
    public override string ToString() => "StoreCardRequest { RequestData = [REDACTED] }";
}

/// <summary>How a marketplace intent or split charge treats sellers that cannot take part (<c>multiSellerPolicy</c>).</summary>
public static class MarketplaceSellerPolicies
{
    /// <summary><c>ALL_OR_NOTHING</c>: the intent is refused when any seller cannot take part.</summary>
    public const string AllOrNothing = "ALL_OR_NOTHING";
    /// <summary><c>PARTIAL_ALLOWED</c>: eligible sellers proceed.</summary>
    public const string PartialAllowed = "PARTIAL_ALLOWED";
    /// <summary>Every multi-seller policy.</summary>
    public static IReadOnlyList<string> All { get; } = [AllOrNothing, PartialAllowed];
}

/// <summary>One seller's share of a marketplace intent: the sub-dealer and the total attributed to it.</summary>
public sealed class MarketplaceSeller
{
    /// <summary>The sub-dealer identifier.</summary>
    public string SubDealerId { get; }
    /// <summary>The seller's share in positive minor units, as a digit string.</summary>
    public string AmountMinor { get; }
    /// <summary>Optional per-request commission scenario (a JSON object).</summary>
    public JsonElement? Scenario { get; init; }

    /// <summary>Creates one seller share.</summary>
    public MarketplaceSeller(string subDealerId, string amountMinor)
    {
        RequestValidation.Text(subDealerId, "subDealerId", 256); Validation.Amount(amountMinor);
        SubDealerId = subDealerId; AmountMinor = amountMinor;
    }
}

internal static class CardValidation
{
    /// <summary>6–19 digits. The value is never echoed in the message.</summary>
    internal static void BinOrPan(string value)
    {
        if (value is null || value.Length is < 6 or > 19 || !value.All(char.IsAsciiDigit))
            throw new LunixiConfigurationException("binOrPan must be the card's first 6 to 19 digits, digits only.");
    }

    /// <summary>The intent body plus <c>sellers</c> and <c>multiSellerPolicy</c>; the sellers' shares add up to the amount.</summary>
    internal static JsonObject MarketplaceIntent(CreateIntentRequest request, IReadOnlyList<MarketplaceSeller> sellers, string? multiSellerPolicy)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(sellers);
        request.Validate();
        var legs = SellerLegs(sellers, request.Amount);
        if (multiSellerPolicy is not null and not (MarketplaceSellerPolicies.AllOrNothing or MarketplaceSellerPolicies.PartialAllowed))
            throw new LunixiConfigurationException("multiSellerPolicy must be ALL_OR_NOTHING or PARTIAL_ALLOWED.");
        var body = JsonNode.Parse(CanonicalJson.Serialize(request))!.AsObject();
        body["sellers"] = legs;
        if (multiSellerPolicy is not null) body["multiSellerPolicy"] = multiSellerPolicy;
        return body;
    }

    /// <summary>
    /// The <c>sellers</c> array of a marketplace split: at least one leg, each a scenario object when given, and the legs'
    /// <c>amountMinor</c> adding up exactly to <paramref name="amount"/> (a positive minor-unit string).
    /// </summary>
    internal static JsonNode SellerLegs(IReadOnlyList<MarketplaceSeller>? sellers, string amount)
    {
        if (sellers is null || sellers.Count == 0) throw new LunixiConfigurationException("A marketplace split requires at least one seller.");
        if (sellers.Any(seller => seller is null)) throw new LunixiConfigurationException("Marketplace sellers cannot be null.");
        long total = 0; var expected = Money.Parse(amount);
        foreach (var seller in sellers)
        {
            var share = Money.Parse(seller.AmountMinor);
            if (total > expected - share) throw new LunixiConfigurationException("The sellers' amountMinor values must add up exactly to the amount.");
            total += share;
            if (seller.Scenario is { ValueKind: not JsonValueKind.Object })
                throw new LunixiConfigurationException("A seller scenario must be a JSON object.");
        }
        if (total != expected) throw new LunixiConfigurationException("The sellers' amountMinor values must add up exactly to the amount.");
        return JsonNode.Parse(CanonicalJson.Serialize(sellers))!;
    }
}
