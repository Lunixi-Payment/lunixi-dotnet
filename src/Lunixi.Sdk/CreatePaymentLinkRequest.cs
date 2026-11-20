using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Lunixi;

/// <summary>
/// Body of <c>POST /api/v1/payments/links</c>. Amounts are canonical minor-unit strings (<c>"150000"</c> is 1,500.00 TRY).
/// <c>usage</c>, <c>amountMode</c> and <c>currency</c> are fixed at creation. The builder checks types and value sets; the
/// business rules (title length, amount ranges, which fields a mode allows) are the gateway's, which answers 422
/// <c>payment_link.validation.invalid</c> with <c>details.errors</c>. Fields are sent in the order they are set.
/// </summary>
[JsonConverter(typeof(PaymentLinkJsonConverter<CreatePaymentLinkRequest>))]
public sealed class CreatePaymentLinkRequest : IPaymentLinkJson
{
    private readonly JsonObject payload = new();

    /// <summary>Starts a link with its fixed usage, amount mode and currency and its title.</summary>
    public CreatePaymentLinkRequest(string usage, string amountMode, string currency, string title)
    {
        payload["usage"] = PaymentLinkValidation.OneOf(usage, PaymentLinkUsages.All, "usage");
        payload["amountMode"] = PaymentLinkValidation.OneOf(amountMode, PaymentLinkAmountModes.All, "amountMode");
        var normalizedCurrency = currency?.ToUpperInvariant() ?? "";
        RequestValidation.Currency(normalizedCurrency); payload["currency"] = normalizedCurrency;
        payload["title"] = PaymentLinkValidation.RequiredText(title, "title");
    }

    /// <summary>A link for a set amount.</summary>
    public static CreatePaymentLinkRequest Fixed(string usage, string amountMinor, string currency, string title)
        => new CreatePaymentLinkRequest(usage, PaymentLinkAmountModes.Fixed, currency, title).WithAmountMinor(amountMinor);

    /// <summary>
    /// A link where the buyer enters the amount (donations, deposits). <paramref name="minAmountMinor"/> is required;
    /// a maximum and up to six ascending suggested amounts are optional.
    /// </summary>
    public static CreatePaymentLinkRequest Open(string usage, string currency, string title, string minAmountMinor,
        string? maxAmountMinor = null, IEnumerable<string>? suggestedAmountsMinor = null)
    {
        var request = new CreatePaymentLinkRequest(usage, PaymentLinkAmountModes.Open, currency, title).WithMinAmountMinor(minAmountMinor);
        if (maxAmountMinor is not null) request.WithMaxAmountMinor(maxAmountMinor);
        if (suggestedAmountsMinor is not null) request.WithSuggestedAmountsMinor(suggestedAmountsMinor);
        return request;
    }

    /// <summary>A link where the buyer picks quantities of the given lines.</summary>
    public static CreatePaymentLinkRequest Itemized(string usage, string currency, string title, IEnumerable<PaymentLinkItem> items)
        => new CreatePaymentLinkRequest(usage, PaymentLinkAmountModes.Itemized, currency, title).WithItems(items);

    /// <summary>Optional: must equal the signing key's environment (<see cref="PaymentLinkEnvironments"/>), otherwise 403 <c>payment_link.environment.mismatch</c>.</summary>
    public CreatePaymentLinkRequest WithEnvironment(string environment)
        => Set("environment", PaymentLinkValidation.OneOf(environment, [PaymentLinkEnvironments.Test, PaymentLinkEnvironments.Live], "environment"));
    /// <summary>The amount of a <c>FIXED</c> link in minor units.</summary>
    public CreatePaymentLinkRequest WithAmountMinor(string amountMinor) => Set("amountMinor", PaymentLinkValidation.Minor(amountMinor, "amountMinor"));
    /// <summary>The lowest amount of an <c>OPEN</c> link in minor units.</summary>
    public CreatePaymentLinkRequest WithMinAmountMinor(string minAmountMinor) => Set("minAmountMinor", PaymentLinkValidation.Minor(minAmountMinor, "minAmountMinor"));
    /// <summary>The highest amount of an <c>OPEN</c> link in minor units.</summary>
    public CreatePaymentLinkRequest WithMaxAmountMinor(string maxAmountMinor) => Set("maxAmountMinor", PaymentLinkValidation.Minor(maxAmountMinor, "maxAmountMinor"));
    /// <summary>Up to six suggested amounts of an <c>OPEN</c> link in minor units, ascending.</summary>
    public CreatePaymentLinkRequest WithSuggestedAmountsMinor(IEnumerable<string> suggestedAmountsMinor)
    {
        ArgumentNullException.ThrowIfNull(suggestedAmountsMinor);
        return Set("suggestedAmountsMinor", new JsonArray(suggestedAmountsMinor.Select(a => (JsonNode?)JsonValue.Create(PaymentLinkValidation.Minor(a, "suggestedAmountsMinor[]"))).ToArray()));
    }
    /// <summary>Lets the buyer choose a quantity (<c>FIXED</c> + <c>MULTI_USE</c> only), 1–999.</summary>
    public CreatePaymentLinkRequest WithQuantity(int minQuantity, int maxQuantity)
    {
        payload["quantityEnabled"] = true; payload["minQuantity"] = minQuantity; payload["maxQuantity"] = maxQuantity;
        return this;
    }
    /// <summary>The lines of an <c>ITEMIZED</c> link (at least one). Replaces lines added before.</summary>
    public CreatePaymentLinkRequest WithItems(IEnumerable<PaymentLinkItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var lines = items.ToArray();
        if (lines.Length == 0) throw new LunixiConfigurationException("An ITEMIZED payment link needs at least one item.");
        return Set("items", PaymentLinkValidation.Array(lines, "items"));
    }
    /// <summary>Adds one line of an <c>ITEMIZED</c> link.</summary>
    public CreatePaymentLinkRequest AddItem(PaymentLinkItem item) => Append("items", item);
    /// <summary>Total units the link can sell across all payments (<c>MULTI_USE</c> only); null means unlimited.</summary>
    public CreatePaymentLinkRequest WithCapacity(int? capacity) => Set("capacity", capacity is null ? null : JsonValue.Create(capacity.Value));
    /// <summary>Plain text, shown as text (no HTML or Markdown).</summary>
    public CreatePaymentLinkRequest WithDescription(string description) => Set("description", PaymentLinkValidation.NotNull(description, "description"));
    /// <summary>A published link image (<see cref="PaymentLinksClient.UploadImageAsync"/>); null means none.</summary>
    public CreatePaymentLinkRequest WithImageAssetId(string? imageAssetId) => Set("imageAssetId", PaymentLinkValidation.AssetIdOrNull(imageAssetId, "imageAssetId"));
    /// <summary>The message shown after a successful payment.</summary>
    public CreatePaymentLinkRequest WithSuccessMessage(string successMessage) => Set("successMessage", PaymentLinkValidation.NotNull(successMessage, "successMessage"));
    /// <summary>The HTTPS page the buyer goes to after paying; null clears it.</summary>
    public CreatePaymentLinkRequest WithSuccessRedirectUrl(Uri? successRedirectUrl)
    {
        if (successRedirectUrl is not null) RequestValidation.Https(successRedirectUrl);
        return Set("successRedirectUrl", successRedirectUrl?.AbsoluteUri);
    }
    /// <summary>A tax note shown on the payment page.</summary>
    public CreatePaymentLinkRequest WithTaxNote(string taxNote) => Set("taxNote", PaymentLinkValidation.NotNull(taxNote, "taxNote"));
    /// <summary>Language of the payment page (<see cref="PaymentLinkLocales"/>).</summary>
    public CreatePaymentLinkRequest WithLocale(string locale)
        => Set("locale", PaymentLinkValidation.OneOf(locale, [PaymentLinkLocales.Turkish, PaymentLinkLocales.English], "locale"));
    /// <summary>When the link starts accepting payments; null clears it.</summary>
    public CreatePaymentLinkRequest WithStartsAt(DateTimeOffset? startsAt) => Set("startsAt", PaymentLinkValidation.Time(startsAt));
    /// <summary>When the link stops accepting payments; null clears it.</summary>
    public CreatePaymentLinkRequest WithExpiresAt(DateTimeOffset? expiresAt) => Set("expiresAt", PaymentLinkValidation.Time(expiresAt));
    /// <summary>Display only, for example the event date of a ticket; null clears it.</summary>
    public CreatePaymentLinkRequest WithEventAt(DateTimeOffset? eventAt) => Set("eventAt", PaymentLinkValidation.Time(eventAt));
    /// <summary>The pay button label (<see cref="PaymentLinkButtonLabels"/>); null uses your organisation's default.</summary>
    public CreatePaymentLinkRequest WithButtonLabelKey(string? buttonLabelKey)
        => Set("buttonLabelKey", buttonLabelKey is null ? null : PaymentLinkValidation.OneOf(buttonLabelKey, PaymentLinkButtonLabels.All, "buttonLabelKey"));
    /// <summary>Requires the buyer to accept your terms before paying.</summary>
    public CreatePaymentLinkRequest WithTermsAcceptanceRequired(bool required = true) => Set("requireTermsAcceptance", required);
    /// <summary>
    /// Buyer details the page asks for: keys <c>name</c>, <c>email</c>, <c>phone</c>, <c>address</c>, <c>identityNumber</c>,
    /// each <see cref="BuyerFieldRequirements.Required"/> (the page always asks all five; a field left out is also required);
    /// null clears it.
    /// </summary>
    public CreatePaymentLinkRequest WithBuyerFields(IReadOnlyDictionary<string, string>? buyerFields)
    {
        if (buyerFields is not null)
            foreach (var pair in buyerFields)
                PaymentLinkValidation.OneOf(pair.Value, BuyerFieldRequirements.All, "buyerFields." + pair.Key);
        return Set("buyerFields", PaymentLinkValidation.StringObject(buyerFields, PaymentLinkFields.BuyerFields, "buyerFields"));
    }
    /// <summary>Questions the buyer answers on the payment page. Replaces questions added before.</summary>
    public CreatePaymentLinkRequest WithCustomFields(IEnumerable<PaymentLinkCustomField> customFields)
    {
        ArgumentNullException.ThrowIfNull(customFields);
        return Set("customFields", PaymentLinkValidation.Array(customFields.ToArray(), "customFields"));
    }
    /// <summary>Adds one question the buyer answers on the payment page.</summary>
    public CreatePaymentLinkRequest AddCustomField(PaymentLinkCustomField customField) => Append("customFields", customField);
    /// <summary>Who the link is for: keys <c>name</c>, <c>email</c>, <c>phone</c> (E.164). Stored encrypted; null clears it.</summary>
    public CreatePaymentLinkRequest WithRecipient(IReadOnlyDictionary<string, string>? recipient)
        => Set("recipient", PaymentLinkValidation.StringObject(recipient, PaymentLinkFields.Recipient, "recipient"));
    /// <summary>
    /// Buyer details filled in on the server side and never sent to the browser: keys <c>name</c>, <c>surname</c>,
    /// <c>email</c>, <c>phone</c>, <c>identityNumber</c>, <c>address</c>, <c>city</c>, <c>country</c> (ISO 3166-1 alpha-2),
    /// <c>zipCode</c>; null clears it.
    /// </summary>
    public CreatePaymentLinkRequest WithPrefill(IReadOnlyDictionary<string, string>? prefill)
        => Set("prefill", PaymentLinkValidation.StringObject(prefill, PaymentLinkFields.Prefill, "prefill"));
    /// <summary>Requires the buyer to confirm a one-time code sent to the recipient's e-mail before paying. Only <c>EMAIL</c> is available.</summary>
    public CreatePaymentLinkRequest WithVerification(bool required = true, string channel = PaymentLinkDeliveryChannels.Email)
        => Set("verification", new JsonObject
        {
            ["required"] = required,
            ["channel"] = PaymentLinkValidation.OneOf(channel, [PaymentLinkDeliveryChannels.Email, PaymentLinkDeliveryChannels.Sms], "verification channel"),
        });
    /// <summary>Reminder e-mails (<c>SINGLE_USE</c> with a recipient): every 24–168 hours, at most 1–3 times.</summary>
    public CreatePaymentLinkRequest WithReminders(int intervalHours, int maxCount)
        => Set("reminders", new JsonObject { ["enabled"] = true, ["intervalHours"] = intervalHours, ["maxCount"] = maxCount });
    /// <summary>
    /// Narrows the checkout: <paramref name="requireThreeDs"/> can only tighten, <paramref name="paymentMethods"/> is a subset
    /// of your form settings (empty means your form settings) and <paramref name="enabledInstallments"/> lists the
    /// installment counts the form may offer (empty means no restriction; otherwise the rules of
    /// <see cref="CreateIntentRequest.EnabledInstallments"/>).
    /// </summary>
    public CreatePaymentLinkRequest WithCheckout(bool requireThreeDs = false, IEnumerable<string>? paymentMethods = null, IEnumerable<int>? enabledInstallments = null)
    {
        var checkout = new JsonObject
        {
            ["requireThreeDs"] = requireThreeDs,
            ["paymentMethods"] = PaymentLinkValidation.Strings(paymentMethods ?? [], "paymentMethods"),
        };
        var counts = (enabledInstallments ?? []).ToArray();
        if (counts.Length > 0)
        {
            RequestValidation.EnabledInstallments(counts);
            checkout["enabledInstallments"] = new JsonArray(counts.Order().Select(c => (JsonNode?)JsonValue.Create(c)).ToArray());
        }
        return Set("checkout", checkout);
    }
    /// <summary>Your reference for the link.</summary>
    public CreatePaymentLinkRequest WithReference(string reference) => Set("reference", PaymentLinkValidation.NotNull(reference, "reference"));
    /// <summary>Tags to filter links by.</summary>
    public CreatePaymentLinkRequest WithTags(IEnumerable<string> tags) => Set("tags", PaymentLinkValidation.Strings(tags, "tags"));
    /// <summary>Your branch code.</summary>
    public CreatePaymentLinkRequest WithBranchCode(string branchCode) => Set("branchCode", PaymentLinkValidation.NotNull(branchCode, "branchCode"));
    /// <summary>Your sales channel.</summary>
    public CreatePaymentLinkRequest WithSalesChannel(string salesChannel) => Set("salesChannel", PaymentLinkValidation.NotNull(salesChannel, "salesChannel"));
    /// <summary>Your campaign.</summary>
    public CreatePaymentLinkRequest WithCampaign(string campaign) => Set("campaign", PaymentLinkValidation.NotNull(campaign, "campaign"));
    /// <summary>Your agent code.</summary>
    public CreatePaymentLinkRequest WithAgentCode(string agentCode) => Set("agentCode", PaymentLinkValidation.NotNull(agentCode, "agentCode"));
    /// <summary>Addresses notified about payments on this link.</summary>
    public CreatePaymentLinkRequest WithMerchantNotifyEmails(IEnumerable<string> merchantNotifyEmails)
        => Set("merchantNotifyEmails", PaymentLinkValidation.Strings(merchantNotifyEmails, "merchantNotifyEmails"));
    /// <summary>Up to 20 string key/value pairs returned to you in webhooks; null clears it.</summary>
    public CreatePaymentLinkRequest WithMetadata(IReadOnlyDictionary<string, string>? metadata)
        => Set("metadata", PaymentLinkValidation.StringObject(metadata, null, "metadata"));

    /// <summary>Returns the JSON body exactly as it is sent.</summary>
    public JsonElement ToPayload() => CanonicalJson.Element(payload);
    JsonObject IPaymentLinkJson.ToJsonObject() => payload.DeepClone().AsObject();

    /// <summary>Returns no recipient, prefill or merchant data.</summary>
    public override string ToString() => "CreatePaymentLinkRequest { RequestData = [REDACTED] }";

    private CreatePaymentLinkRequest Set(string name, JsonNode? value) { payload[name] = value; return this; }
    private CreatePaymentLinkRequest Append(string name, IPaymentLinkJson value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (payload[name] is not JsonArray list) payload[name] = list = new JsonArray();
        list.Add(value.ToJsonObject());
        return this;
    }
}

/// <summary>
/// One line of an <c>ITEMIZED</c> payment link. <c>itemKey</c> is the line's permanent identity (stock counters hang off
/// it) and cannot change once the link exists; leave it out and the gateway assigns one.
/// </summary>
[JsonConverter(typeof(PaymentLinkJsonConverter<PaymentLinkItem>))]
public sealed class PaymentLinkItem : IPaymentLinkJson
{
    private readonly JsonObject payload = new();

    /// <summary>Creates a line with its name and unit price in minor units.</summary>
    public PaymentLinkItem(string name, string unitPriceMinor)
    {
        payload["name"] = PaymentLinkValidation.RequiredText(name, "PaymentLinkItem name");
        payload["unitPriceMinor"] = PaymentLinkValidation.Minor(unitPriceMinor, "unitPriceMinor");
    }

    /// <summary>The line's permanent key.</summary>
    public PaymentLinkItem WithItemKey(string itemKey) => Set("itemKey", PaymentLinkValidation.RequiredText(itemKey, "itemKey"));
    /// <summary>Plain-text description.</summary>
    public PaymentLinkItem WithDescription(string description) => Set("description", PaymentLinkValidation.NotNull(description, "description"));
    /// <summary>Buyer-selectable quantity range for this line; a minimum of 0 makes the line optional.</summary>
    public PaymentLinkItem WithQuantityRange(int minQuantity, int maxQuantity)
    {
        payload["minQuantity"] = minQuantity; payload["maxQuantity"] = maxQuantity;
        return this;
    }
    /// <summary>Units available for this line across all payments; null means unlimited.</summary>
    public PaymentLinkItem WithCapacity(int? capacity) => Set("capacity", capacity is null ? null : JsonValue.Create(capacity.Value));
    /// <summary>A published link image; null means none.</summary>
    public PaymentLinkItem WithImageAssetId(string? imageAssetId) => Set("imageAssetId", PaymentLinkValidation.AssetIdOrNull(imageAssetId, "imageAssetId"));
    /// <summary>Lunixi product category (<see cref="ProductCategories"/>); an unknown code is refused by the gateway.</summary>
    public PaymentLinkItem WithProductCategory(string productCategory) => Set("productCategory", PaymentLinkValidation.RequiredText(productCategory, "productCategory"));

    JsonObject IPaymentLinkJson.ToJsonObject() => payload.DeepClone().AsObject();
    private PaymentLinkItem Set(string name, JsonNode? value) { payload[name] = value; return this; }
}

/// <summary>A typed question the buyer answers on the payment page (no file uploads).</summary>
[JsonConverter(typeof(PaymentLinkJsonConverter<PaymentLinkCustomField>))]
public sealed class PaymentLinkCustomField : IPaymentLinkJson
{
    private readonly JsonObject payload = new();

    /// <summary>Creates a question with its key, label and <see cref="PaymentLinkCustomFieldTypes"/> type.</summary>
    public PaymentLinkCustomField(string key, string label, string fieldType)
    {
        payload["key"] = PaymentLinkValidation.RequiredText(key, "custom field key");
        payload["label"] = PaymentLinkValidation.RequiredText(label, "custom field label");
        payload["fieldType"] = PaymentLinkValidation.OneOf(fieldType, PaymentLinkCustomFieldTypes.All, "fieldType");
    }

    /// <summary>Whether the buyer must answer.</summary>
    public PaymentLinkCustomField WithRequired(bool required = true) => Set("required", required);
    /// <summary>The choices of a <c>SELECT</c> question.</summary>
    public PaymentLinkCustomField WithOptions(IEnumerable<string> options) => Set("options", PaymentLinkValidation.Strings(options, "options"));
    /// <summary>The longest accepted answer.</summary>
    public PaymentLinkCustomField WithMaxLength(int maxLength) => Set("maxLength", maxLength);
    /// <summary>Help text under the question.</summary>
    public PaymentLinkCustomField WithHelpText(string helpText) => Set("helpText", PaymentLinkValidation.NotNull(helpText, "helpText"));

    JsonObject IPaymentLinkJson.ToJsonObject() => payload.DeepClone().AsObject();
    private PaymentLinkCustomField Set(string name, JsonNode? value) { payload[name] = value; return this; }
}

/// <summary>A payment-link builder that writes its own JSON object.</summary>
internal interface IPaymentLinkJson
{
    JsonObject ToJsonObject();
}

/// <summary>Serializes a payment-link builder as the JSON object it holds, so it can be nested in any request body.</summary>
internal sealed class PaymentLinkJsonConverter<T> : JsonConverter<T> where T : class, IPaymentLinkJson
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new NotSupportedException("Payment-link builders are write-only.");
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => value.ToJsonObject().WriteTo(writer, options);
}

/// <summary>The local checks of the payment-link bodies. The gateway stays the authority for business rules.</summary>
internal static partial class PaymentLinkValidation
{
    private static readonly HashSet<string> CreateFields = new(PaymentLinkFields.Spec.Prepend("environment"), StringComparer.Ordinal);
    private static readonly HashSet<string> UpdateFields = new(PaymentLinkFields.Spec, StringComparer.Ordinal);

    [GeneratedRegex(PaymentLinkFields.AssetIdPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AssetIdPattern();

    internal static string OneOf(string value, IReadOnlyCollection<string> allowed, string field)
    {
        if (value is null || !allowed.Contains(value))
            throw new LunixiConfigurationException($"{field} must be one of {string.Join(", ", allowed)}.");
        return value;
    }
    internal static string RequiredText(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new LunixiConfigurationException($"{field} must be a non-empty string.");
        return value;
    }
    internal static string NotNull(string value, string field)
        => value ?? throw new LunixiConfigurationException($"{field} must be a string.");
    internal static string Minor(string value, string field)
    {
        try { Money.Parse(value); }
        catch (LunixiConfigurationException) { throw new LunixiConfigurationException($"{field} must be a canonical minor-unit string."); }
        return value;
    }
    internal static string? AssetIdOrNull(string? value, string field)
    {
        if (value is not null && !IsAssetId(value))
            throw new LunixiConfigurationException($"{field} must be an asset id (uuid) or null.");
        return value;
    }
    internal static string AssetId(string value)
    {
        var assetId = value?.Trim() ?? "";
        if (!IsAssetId(assetId)) throw new LunixiConfigurationException("assetId must be the image's asset id (uuid).");
        return assetId;
    }
    /// <summary>A uuid of exactly 36 characters (the pattern's <c>$</c> would also accept a trailing newline).</summary>
    internal static bool IsAssetId(string? value) => value is { Length: 36 } && AssetIdPattern().IsMatch(value);
    internal static string? Time(DateTimeOffset? value)
        => value?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    internal static JsonArray Strings(IEnumerable<string> values, string field)
    {
        ArgumentNullException.ThrowIfNull(values);
        var items = values.ToArray();
        if (items.Any(v => v is null)) throw new LunixiConfigurationException($"{field} must be a list of strings.");
        return new JsonArray(items.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
    }
    internal static JsonArray Array(IReadOnlyCollection<IPaymentLinkJson> values, string field)
    {
        if (values.Any(v => v is null)) throw new LunixiConfigurationException($"{field} cannot contain null.");
        return new JsonArray(values.Select(v => (JsonNode?)v.ToJsonObject()).ToArray());
    }
    /// <summary>An object of string values with a closed key set (or any keys when <paramref name="allowed"/> is null); null clears it.</summary>
    internal static JsonObject? StringObject(IReadOnlyDictionary<string, string>? values, IReadOnlyCollection<string>? allowed, string field)
    {
        if (values is null) return null;
        var result = new JsonObject();
        foreach (var pair in values)
        {
            if (allowed is not null && !allowed.Contains(pair.Key))
                throw new LunixiConfigurationException($"Unknown {field} field '{pair.Key}'. Allowed: {string.Join(", ", allowed)}.");
            result[pair.Key] = pair.Value ?? throw new LunixiConfigurationException($"{field}.{pair.Key} must be a string.");
        }
        return result;
    }

    /// <summary>A create body: an object with known fields only, time fields as non-empty text or null.</summary>
    internal static JsonElement CreateBody(object request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = CanonicalJson.Element(request);
        if (body.ValueKind != JsonValueKind.Object)
            throw new LunixiConfigurationException("A payment link request must be a CreatePaymentLinkRequest or a JSON object.");
        KnownFields(body, CreateFields, "create");
        Times(body);
        return body;
    }

    /// <summary><c>{ expectedRowVersion, ...changes }</c>: at least one change, no fixed field, known fields only.</summary>
    internal static JsonObject UpdateBody(long expectedRowVersion, object changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var body = CanonicalJson.Element(changes);
        if (body.ValueKind != JsonValueKind.Object) throw new LunixiConfigurationException("Payment link changes must be a JSON object.");
        if (!body.EnumerateObject().Any()) throw new LunixiConfigurationException("A payment link update needs at least one changed field.");
        foreach (var property in body.EnumerateObject())
            if (PaymentLinkFields.Immutable.Contains(property.Name))
                throw new LunixiConfigurationException($"'{property.Name}' is fixed when a payment link is created and cannot be updated. Create a new link instead.");
        KnownFields(body, UpdateFields, "update");
        Times(body);
        var result = new JsonObject { ["expectedRowVersion"] = RowVersion(expectedRowVersion) };
        foreach (var property in body.EnumerateObject()) result[property.Name] = JsonNode.Parse(property.Value.GetRawText());
        return result;
    }

    internal static long RowVersion(long value)
    {
        if (value < 0 || value > Validation.MaxSafeInteger)
            throw new LunixiConfigurationException("expectedRowVersion must be the link's current rowVersion (a non-negative integer).");
        return value;
    }

    private static void KnownFields(JsonElement body, HashSet<string> allowed, string operation)
    {
        var unknown = body.EnumerateObject().Select(p => p.Name).Where(name => !allowed.Contains(name)).ToArray();
        if (unknown.Length > 0)
            throw new LunixiConfigurationException($"Unknown payment link {operation} field(s): {string.Join(", ", unknown)}. The gateway rejects unknown fields with 400.");
    }

    private static void Times(JsonElement body)
    {
        foreach (var name in PaymentLinkFields.Times)
            if (body.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null &&
                (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())))
                throw new LunixiConfigurationException($"{name} must be an ISO-8601 time or null.");
    }
}
