using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lunixi;

/// <summary>
/// Order details every payment endpoint requires: the buyer, the billing address and at least one basket item; a basket
/// with a <c>PHYSICAL</c> item also requires the shipping address. Payment providers do not start a payment without them.
/// <para>
/// The gateway refuses a missing field with <see cref="OrderFieldErrorCodes.FieldRequired"/> and an invalid one with
/// <see cref="OrderFieldErrorCodes.FieldInvalid"/>; <c>details.field</c> is the dotted path of the first failing field,
/// <c>details.reason</c> the reason (<see cref="OrderFieldReasons"/>) and <c>details.fields</c> every field failing for the
/// same reason. The SDK checks the same rule before sending and names the same paths in
/// <see cref="LunixiConfigurationException"/>.
/// </para>
/// </summary>
public static class OrderDetails
{
    /// <summary>Order objects required on every payment endpoint: <c>buyer</c>, <c>billingAddress</c>, <c>basketItems</c>.</summary>
    public static IReadOnlyList<string> RequiredFields { get; } = Array.AsReadOnly(new[] { "buyer", "billingAddress", "basketItems" });

    /// <summary>
    /// <c>buyer</c> fields that must be non-empty strings. <c>identityNumber</c>: Turkish ID or passport number;
    /// <c>email</c>: ASCII; <c>ip</c>: the buyer's browser IP (IPv4 or IPv6).
    /// </summary>
    public static IReadOnlyList<string> BuyerRequiredFields { get; } = Array.AsReadOnly(new[]
    {
        "name", "surname", "identityNumber", "email", "gsmNumber", "city", "country", "zipCode", "ip",
    });

    /// <summary>
    /// <c>buyer</c> fields a checkout intent may leave out (<see cref="PaymentsClient.CreateIntentAsync"/>,
    /// <see cref="PaymentsClient.CreateMarketplaceIntentAsync"/>, <see cref="MarketplaceClient.CreateCheckoutIntentAsync"/>):
    /// the Lunixi form takes the IP from the buyer's browser. A given <c>ip</c> is still sent and wins.
    /// </summary>
    public static IReadOnlyList<string> CheckoutIntentBuyerOptionalFields { get; } = Array.AsReadOnly(new[] { "ip" });

    /// <summary>Fields of <c>billingAddress</c> and <c>shippingAddress</c> that must be non-empty strings.</summary>
    public static IReadOnlyList<string> AddressRequiredFields { get; } = Array.AsReadOnly(new[] { "contactName", "address", "city", "country", "zipCode" });

    /// <summary>Fields of each <c>basketItems[]</c> entry the gateway requires.</summary>
    public static IReadOnlyList<string> BasketItemRequiredFields { get; } = Array.AsReadOnly(new[] { "id", "name", "category1", "itemType", "price" });

    /// <summary>
    /// Paths of the missing order fields of a payment body, in the gateway's format (<c>buyer</c>, <c>buyer.email</c>,
    /// <c>billingAddress.city</c>, <c>basketItems.0.category1</c>, <c>shippingAddress</c>). An empty list means the order
    /// details are complete. <paramref name="body"/> is the JSON body (a <see cref="JsonElement"/>, a JSON node or an object
    /// serialized like a request). <paramref name="basket"/> false checks the buyer and the billing address only (card
    /// storage, wallet card top-up); <paramref name="checkoutIntent"/> true does not require
    /// <see cref="CheckoutIntentBuyerOptionalFields"/>.
    /// </summary>
    public static IReadOnlyList<string> MissingFields(object body, bool basket = true, bool checkoutIntent = false)
    {
        ArgumentNullException.ThrowIfNull(body);
        var element = body is JsonElement json ? json : CanonicalJson.Element(body);
        if (element.ValueKind != JsonValueKind.Object) return RequiredFields;
        return Missing(element, basket, checkoutIntent);
    }

    /// <summary>Throws <see cref="LunixiConfigurationException"/> naming the missing order fields of <paramref name="body"/>.</summary>
    internal static void Require(JsonElement body, string label, bool basket = true, bool checkoutIntent = false)
    {
        var missing = body.ValueKind == JsonValueKind.Object ? Missing(body, basket, checkoutIntent) : RequiredFields;
        if (missing.Count > 0)
            throw new LunixiConfigurationException($"{label} requires the order details: {string.Join(", ", missing)}.");
    }

    /// <inheritdoc cref="Require(JsonElement, string, bool, bool)"/>
    internal static void Require(JsonNode body, string label, bool basket = true, bool checkoutIntent = false)
        => Require(CanonicalJson.Element(body), label, basket, checkoutIntent);

    private static List<string> Missing(JsonElement body, bool basket, bool checkoutIntent)
    {
        var buyerFields = checkoutIntent
            ? BuyerRequiredFields.Where(field => !CheckoutIntentBuyerOptionalFields.Contains(field)).ToArray()
            : BuyerRequiredFields;
        var missing = new List<string>();
        RequiredObject(body, "buyer", buyerFields, missing);
        RequiredObject(body, "billingAddress", AddressRequiredFields, missing);
        if (!basket) return missing;
        if (!body.TryGetProperty("basketItems", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
        {
            missing.Add("basketItems");
            return missing;
        }
        var physical = false;
        var index = 0;
        foreach (var item in items.EnumerateArray())
        {
            var path = "basketItems." + index++;
            if (item.ValueKind != JsonValueKind.Object) { missing.Add(path); continue; }
            foreach (var field in BasketItemRequiredFields)
                if (!item.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null ||
                    value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()))
                    missing.Add(path + "." + field);
            physical |= item.TryGetProperty("itemType", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "PHYSICAL";
        }
        var hasShipping = body.TryGetProperty("shippingAddress", out var shipping) && shipping.ValueKind != JsonValueKind.Null;
        if (physical || hasShipping) RequiredObject(body, "shippingAddress", AddressRequiredFields, missing);
        return missing;
    }

    /// <summary>The object itself when absent or not an object; otherwise each field that is not a non-empty string.</summary>
    private static void RequiredObject(JsonElement body, string name, IReadOnlyList<string> fields, List<string> missing)
    {
        if (!body.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object) { missing.Add(name); return; }
        foreach (var field in fields)
            if (!value.TryGetProperty(field, out var text) || text.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(text.GetString()))
                missing.Add(name + "." + field);
    }
}

/// <summary>Error codes of a missing or invalid order field (<see cref="OrderDetails"/>).</summary>
public static class OrderFieldErrorCodes
{
    /// <summary><c>payment.order.field_required</c>: an order field is missing (HTTP 400).</summary>
    public const string FieldRequired = "payment.order.field_required";
    /// <summary><c>payment.order.field_invalid</c>: an order field has the wrong type, format or length (HTTP 400).</summary>
    public const string FieldInvalid = "payment.order.field_invalid";
}

/// <summary><c>details.reason</c> of an order field error.</summary>
public static class OrderFieldReasons
{
    /// <summary><c>required</c></summary>
    public const string Required = "required";
    /// <summary><c>required_when_physical</c>: the shipping address of a basket with a <c>PHYSICAL</c> item.</summary>
    public const string RequiredWhenPhysical = "required_when_physical";
    /// <summary><c>invalid_type</c></summary>
    public const string InvalidType = "invalid_type";
    /// <summary><c>invalid_format</c></summary>
    public const string InvalidFormat = "invalid_format";
    /// <summary><c>too_long</c></summary>
    public const string TooLong = "too_long";
    /// <summary><c>too_few_items</c>: <c>basketItems</c> is empty.</summary>
    public const string TooFewItems = "too_few_items";
}
