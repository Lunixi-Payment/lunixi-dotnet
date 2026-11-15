namespace Lunixi;

/// <summary>
/// Public payment method codes (<c>paymentMethod</c>). <c>QR</c> is accepted as an alias of <see cref="QrCode"/>.
/// The provider codes of each method are always read from <see cref="PaymentsClient.ListMethodsAsync"/>.
/// </summary>
public static class PaymentMethods
{
    /// <summary><c>CARD</c></summary>
    public const string Card = "CARD";
    /// <summary><c>BNPL</c></summary>
    public const string Bnpl = "BNPL";
    /// <summary><c>SHOPPING_LOAN</c>: shopping loan; the basket (1–100 items) is required.</summary>
    public const string ShoppingLoan = "SHOPPING_LOAN";
    /// <summary><c>PROMISSORY_NOTE</c>: payment by promissory notes; the plan is a schedule or a note count.</summary>
    public const string PromissoryNote = "PROMISSORY_NOTE";
    /// <summary><c>MEAL_CARD</c></summary>
    public const string MealCard = "MEAL_CARD";
    /// <summary><c>GIFT_CARD</c></summary>
    public const string GiftCard = "GIFT_CARD";
    /// <summary><c>CASH</c></summary>
    public const string Cash = "CASH";
    /// <summary><c>CASH_ON_DELIVERY</c></summary>
    public const string CashOnDelivery = "CASH_ON_DELIVERY";
    /// <summary><c>WALLET</c></summary>
    public const string Wallet = "WALLET";
    /// <summary><c>BANK_TRANSFER</c>: the buyer sends the money from their own bank to the merchant's bank account.</summary>
    public const string BankTransfer = "BANK_TRANSFER";
    /// <summary><c>OPEN_BANKING</c></summary>
    public const string OpenBanking = "OPEN_BANKING";
    /// <summary><c>MOBILE_BILLING</c>: charge to the mobile phone bill.</summary>
    public const string MobileBilling = "MOBILE_BILLING";
    /// <summary><c>QR_CODE</c></summary>
    public const string QrCode = "QR_CODE";
    /// <summary><c>CRYPTO</c></summary>
    public const string Crypto = "CRYPTO";
    /// <summary><c>OTHER</c></summary>
    public const string Other = "OTHER";
    /// <summary><c>QR</c>: accepted alias of <see cref="QrCode"/>; the SDK sends <c>QR_CODE</c>.</summary>
    public const string QrAlias = "QR";

    /// <summary>Every method code known to this SDK version, in the documented order.</summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        Card, Bnpl, ShoppingLoan, PromissoryNote, MealCard, GiftCard, Cash, CashOnDelivery,
        Wallet, BankTransfer, OpenBanking, MobileBilling, QrCode, Crypto, Other,
    });

    /// <summary>Most basket items a <see cref="ShoppingLoan"/> payment can carry; the basket is required.</summary>
    public const int ShoppingLoanMaxBasketItems = 100;
}

/// <summary>What the buyer must do when a payment response carries <c>action</c> (<c>action.type</c>).</summary>
public static class PaymentActionTypes
{
    /// <summary><c>OTP</c>: send the one-time code with <see cref="PaymentsClient.ConfirmActionAsync"/>.</summary>
    public const string Otp = "OTP";
    /// <summary><c>REDIRECT</c>: send the buyer to <c>action.redirect.url</c>.</summary>
    public const string Redirect = "REDIRECT";
    /// <summary><c>DISPLAY</c>: show the buyer what to act on outside the page (transfer details, a code).</summary>
    public const string Display = "DISPLAY";
    /// <summary><c>APP_APPROVAL</c>: the buyer approves in the provider's app; the result arrives by webhook.</summary>
    public const string AppApproval = "APP_APPROVAL";
    /// <summary><c>INPUT</c>: send the fields listed in <c>action.input.fields</c> with <see cref="PaymentsClient.ConfirmActionAsync"/>.</summary>
    public const string Input = "INPUT";
}

/// <summary><c>providerSelection</c> of a method returned by <see cref="PaymentsClient.ListMethodsAsync"/>.</summary>
public static class ProviderSelections
{
    /// <summary><c>ROUTED</c>: card routing picks the provider; <c>providerCode</c> is not sent.</summary>
    public const string Routed = "ROUTED";
    /// <summary><c>OPTIONAL</c>: <c>providerCode</c> may be omitted.</summary>
    public const string Optional = "OPTIONAL";
    /// <summary><c>REQUIRED</c>: more than one provider is connected and the buyer picks one; send <c>providerCode</c>.</summary>
    public const string Required = "REQUIRED";
}

/// <summary><c>providerSelectionMode</c> of a method returned by <see cref="PaymentsClient.ListMethodsAsync"/>: who picks the provider.</summary>
public static class ProviderSelectionModes
{
    /// <summary><c>BUYER</c>: the buyer picks the brand; with more than one provider connected <c>providerCode</c> is required.</summary>
    public const string Buyer = "BUYER";
    /// <summary><c>PLATFORM</c>: without <c>providerCode</c> the platform picks one of the connected providers before any money moves.</summary>
    public const string Platform = "PLATFORM";
}

/// <summary>Field lists of the payment-method endpoints (gateway DTOs; an unknown field is rejected with HTTP 400).</summary>
public static class PaymentMethodFields
{
    /// <summary>
    /// Method blocks accepted by <c>POST /api/v1/payments/direct/payment</c> besides <c>card</c>. The block of the chosen
    /// method is required and a block of another method is rejected. Its name is the camelCase form of the method code.
    /// </summary>
    public static IReadOnlyList<string> MethodBlocks { get; } = Array.AsReadOnly(new[]
    {
        "mobileBilling", "wallet", "promissoryNote", "mealCard", "openBanking", "shoppingLoan", "bankTransfer",
    });

    /// <summary>Body fields of <c>POST /api/v1/payments/direct/payment</c>: order, card and option fields plus <see cref="MethodBlocks"/>.</summary>
    public static IReadOnlyList<string> DirectMethodPayment { get; } = Array.AsReadOnly(new[]
    {
        "paymentMethod", "amount", "currency", "orderId", "paymentChannel", "paymentGroup", "callbackUrl", "locale",
        "description", "customerId", "preauth", "buyer", "shippingAddress", "billingAddress", "basketItems", "card",
        "installment", "installmentQuoteToken", "force3D", "metadata",
        "mobileBilling", "wallet", "promissoryNote", "mealCard", "openBanking", "shoppingLoan", "bankTransfer",
    });

    /// <summary>Fields that belong to a card payment only (<c>paymentMethod: CARD</c>).</summary>
    public static IReadOnlyList<string> CardOnly { get; } = Array.AsReadOnly(new[] { "card", "installment", "installmentQuoteToken", "force3D" });

    /// <summary>
    /// Body fields of <c>POST /api/v1/payments/{paymentId}/actions/{actionId}/confirm</c> sent by a merchant server. The
    /// hosted form's checkout-token fields are never sent by the SDK.
    /// </summary>
    public static IReadOnlyList<string> ActionConfirm { get; } = Array.AsReadOnly(new[] { "otp", "consentAccepted", "fields" });
}

internal static class PaymentMethodValidation
{
    /// <summary>Canonical method code (<c>^[A-Z][A-Z0-9_]{0,31}$</c>); <c>QR</c> becomes <c>QR_CODE</c>.</summary>
    internal static string Normalize(string? value)
    {
        if (value is null || value.Length is < 1 or > 32 || !char.IsAsciiLetterUpper(value[0]) ||
            value.Any(c => !(char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_')))
            throw new LunixiConfigurationException("paymentMethod must be an uppercase payment method code such as MOBILE_BILLING.");
        return value == PaymentMethods.QrAlias ? PaymentMethods.QrCode : value;
    }

    /// <summary>Block name of a method: the camelCase form of its code (<c>BANK_TRANSFER</c> → <c>bankTransfer</c>).</summary>
    internal static string BlockName(string code)
    {
        var parts = Normalize(code).ToLowerInvariant().Split('_', StringSplitOptions.RemoveEmptyEntries);
        return parts[0] + string.Concat(parts.Skip(1).Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }
}
