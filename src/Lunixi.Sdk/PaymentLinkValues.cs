namespace Lunixi;

/// <summary>How often a payment link can be paid (<c>usage</c>). Fixed at creation.</summary>
public static class PaymentLinkUsages
{
    /// <summary><c>SINGLE_USE</c>: paid once, the typical invoice or personal payment request.</summary>
    public const string SingleUse = "SINGLE_USE";
    /// <summary><c>MULTI_USE</c>: paid many times (donations, tickets, a product page).</summary>
    public const string MultiUse = "MULTI_USE";
    /// <summary>Every usage, in the documented order.</summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { SingleUse, MultiUse });
}

/// <summary>Who sets the amount of a payment link (<c>amountMode</c>). Fixed at creation.</summary>
public static class PaymentLinkAmountModes
{
    /// <summary><c>FIXED</c>: you set <c>amountMinor</c>.</summary>
    public const string Fixed = "FIXED";
    /// <summary><c>OPEN</c>: the buyer enters the amount within <c>minAmountMinor</c> / <c>maxAmountMinor</c>.</summary>
    public const string Open = "OPEN";
    /// <summary><c>ITEMIZED</c>: the buyer picks quantities from <c>items</c>; the amount is the sum of the lines.</summary>
    public const string Itemized = "ITEMIZED";
    /// <summary>Every amount mode, in the documented order.</summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { Fixed, Open, Itemized });
}

/// <summary>State of a payment link (<c>state</c> and the <c>state</c> list filter).</summary>
public static class PaymentLinkStates
{
    /// <summary><c>ACTIVE</c></summary>
    public const string Active = "ACTIVE";
    /// <summary><c>PAUSED</c></summary>
    public const string Paused = "PAUSED";
    /// <summary><c>COMPLETED</c></summary>
    public const string Completed = "COMPLETED";
    /// <summary><c>EXPIRED</c></summary>
    public const string Expired = "EXPIRED";
    /// <summary><c>DEACTIVATED</c></summary>
    public const string Deactivated = "DEACTIVATED";
    /// <summary><c>TAKEN_DOWN</c>: removed by Lunixi; every write answers 409 <c>payment_link.link.taken_down</c>.</summary>
    public const string TakenDown = "TAKEN_DOWN";
    /// <summary>Every state, in the documented order.</summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { Active, Paused, Completed, Expired, Deactivated, TakenDown });
}

/// <summary>State of a payment attempt on a link (<see cref="PaymentLinksClient.ListAttemptsAsync"/>).</summary>
public static class PaymentLinkAttemptStates
{
    /// <summary><c>OPEN</c></summary>
    public const string Open = "OPEN";
    /// <summary><c>BOUND</c></summary>
    public const string Bound = "BOUND";
    /// <summary><c>SUCCEEDED</c></summary>
    public const string Succeeded = "SUCCEEDED";
    /// <summary><c>FAILED</c></summary>
    public const string Failed = "FAILED";
    /// <summary><c>EXPIRED</c></summary>
    public const string Expired = "EXPIRED";
    /// <summary><c>ABANDONED</c></summary>
    public const string Abandoned = "ABANDONED";
    /// <summary><c>NEEDS_REVIEW</c></summary>
    public const string NeedsReview = "NEEDS_REVIEW";
    /// <summary>Every attempt state, in the documented order.</summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { Open, Bound, Succeeded, Failed, Expired, Abandoned, NeedsReview });
}

/// <summary>Environment of a payment link. The signing key already decides it; a different value answers 403.</summary>
public static class PaymentLinkEnvironments
{
    /// <summary><c>TEST</c></summary>
    public const string Test = "TEST";
    /// <summary><c>LIVE</c></summary>
    public const string Live = "LIVE";
}

/// <summary>Whether the payment page asks the buyer for a detail (<c>buyerFields</c> values).</summary>
public static class BuyerFieldRequirements
{
    /// <summary><c>HIDDEN</c></summary>
    public const string Hidden = "HIDDEN";
    /// <summary><c>OPTIONAL</c></summary>
    public const string Optional = "OPTIONAL";
    /// <summary><c>REQUIRED</c></summary>
    public const string Required = "REQUIRED";
}

/// <summary>Types of a custom question on the payment page (<see cref="PaymentLinkCustomField"/>). File uploads are not offered.</summary>
public static class PaymentLinkCustomFieldTypes
{
    /// <summary><c>TEXT</c></summary>
    public const string Text = "TEXT";
    /// <summary><c>NUMBER</c></summary>
    public const string Number = "NUMBER";
    /// <summary><c>EMAIL</c></summary>
    public const string Email = "EMAIL";
    /// <summary><c>PHONE</c></summary>
    public const string Phone = "PHONE";
    /// <summary><c>DATE</c></summary>
    public const string Date = "DATE";
    /// <summary><c>SELECT</c>: give the choices with <see cref="PaymentLinkCustomField.WithOptions"/>.</summary>
    public const string Select = "SELECT";
    /// <summary><c>CHECKBOX</c></summary>
    public const string Checkbox = "CHECKBOX";
    /// <summary><c>TCKN</c>: Turkish national identity number.</summary>
    public const string Tckn = "TCKN";
    /// <summary><c>VKN</c>: Turkish tax number.</summary>
    public const string Vkn = "VKN";
    /// <summary>Every type, in the documented order.</summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { Text, Number, Email, Phone, Date, Select, Checkbox, Tckn, Vkn });
}

/// <summary>Label of the pay button (<c>buttonLabelKey</c>).</summary>
public static class PaymentLinkButtonLabels
{
    /// <summary><c>PAY</c></summary>
    public const string Pay = "PAY";
    /// <summary><c>DONATE</c></summary>
    public const string Donate = "DONATE";
    /// <summary><c>BUY</c></summary>
    public const string Buy = "BUY";
    /// <summary><c>BOOK</c></summary>
    public const string Book = "BOOK";
    /// <summary>Every label, in the documented order.</summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { Pay, Donate, Buy, Book });
}

/// <summary>Channel of <see cref="PaymentLinksClient.SendAsync"/> and of the buyer verification code.</summary>
public static class PaymentLinkDeliveryChannels
{
    /// <summary><c>EMAIL</c></summary>
    public const string Email = "EMAIL";
    /// <summary><c>SMS</c></summary>
    public const string Sms = "SMS";
}

/// <summary>Image format of <see cref="PaymentLinksClient.QrAsync"/>.</summary>
public static class PaymentLinkQrFormats
{
    /// <summary><c>svg</c> (<c>image/svg+xml</c>)</summary>
    public const string Svg = "svg";
    /// <summary><c>png</c> (<c>image/png</c>)</summary>
    public const string Png = "png";
}

/// <summary>Language of the payment page and of a sent link.</summary>
public static class PaymentLinkLocales
{
    /// <summary><c>tr</c></summary>
    public const string Turkish = "tr";
    /// <summary><c>en</c></summary>
    public const string English = "en";
}

/// <summary>Types a payment-link image can be declared as. The stored bytes are checked against it on confirmation; SVG is never accepted.</summary>
public static class PaymentLinkImageContentTypes
{
    /// <summary><c>image/png</c></summary>
    public const string Png = "image/png";
    /// <summary><c>image/jpeg</c></summary>
    public const string Jpeg = "image/jpeg";
    /// <summary><c>image/webp</c></summary>
    public const string Webp = "image/webp";
    /// <summary>Every accepted type.</summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { Png, Jpeg, Webp });
}

/// <summary>Field lists of the payment-link bodies, in the gateway's spelling.</summary>
public static class PaymentLinkFields
{
    /// <summary>Shape of a media asset id (a uuid, any letter case); ownership and state are checked by the gateway.</summary>
    public const string AssetIdPattern = "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$";
    /// <summary><c>Idempotency-Key</c> format of the payment-link routes: 8–200 printable ASCII characters, no spaces.</summary>
    public const string IdempotencyKeyPattern = @"^[\x21-\x7E]{8,200}$";
    /// <summary>
    /// Every field of a payment-link definition. Create bodies carry only these (plus <c>environment</c>) and update bodies
    /// only these (plus <c>expectedRowVersion</c>); the gateway rejects any other key with 400.
    /// </summary>
    public static IReadOnlyList<string> Spec { get; } = Array.AsReadOnly(new[]
    {
        "usage", "amountMode", "currency", "amountMinor", "minAmountMinor", "maxAmountMinor", "suggestedAmountsMinor",
        "quantityEnabled", "minQuantity", "maxQuantity", "items", "capacity", "title", "description", "imageAssetId",
        "successMessage", "successRedirectUrl", "taxNote", "locale", "startsAt", "expiresAt", "eventAt", "buttonLabelKey",
        "requireTermsAcceptance", "buyerFields", "customFields", "recipient", "prefill", "verification", "reminders",
        "checkout", "reference", "tags", "branchCode", "salesChannel", "campaign", "agentCode", "merchantNotifyEmails", "metadata",
    });
    /// <summary>Fixed at creation; an update that names one answers 422 <c>payment_link.link.immutable_field</c>.</summary>
    public static IReadOnlyList<string> Immutable { get; } = Array.AsReadOnly(new[] { "usage", "amountMode", "currency" });
    /// <summary>Time fields, sent as ISO-8601 text.</summary>
    public static IReadOnlyList<string> Times { get; } = Array.AsReadOnly(new[] { "startsAt", "expiresAt", "eventAt" });
    /// <summary>Keys of <c>buyerFields</c>.</summary>
    public static IReadOnlyList<string> BuyerFields { get; } = Array.AsReadOnly(new[] { "name", "email", "phone", "address", "identityNumber" });
    /// <summary>Keys of <c>recipient</c>.</summary>
    public static IReadOnlyList<string> Recipient { get; } = Array.AsReadOnly(new[] { "name", "email", "phone" });
    /// <summary>Keys of <c>prefill</c>.</summary>
    public static IReadOnlyList<string> Prefill { get; } = Array.AsReadOnly(new[] { "name", "surname", "email", "phone", "identityNumber", "address", "city", "country", "zipCode" });
    /// <summary>Filters of <see cref="PaymentLinksClient.ListAsync"/>.</summary>
    public static IReadOnlyList<string> ListFilters { get; } = Array.AsReadOnly(new[]
    {
        "limit", "cursor", "includeTotal", "state", "usage", "amountMode", "environment", "tag", "branchCode", "createdById", "query", "createdFrom", "createdTo",
    });
    /// <summary>Filters of <see cref="PaymentLinksClient.ListAttemptsAsync"/>.</summary>
    public static IReadOnlyList<string> AttemptListFilters { get; } = Array.AsReadOnly(new[] { "limit", "cursor", "includeTotal", "state" });
    /// <summary>Filters of <see cref="PaymentLinksClient.ListPaymentsAsync"/>.</summary>
    public static IReadOnlyList<string> PaymentListFilters { get; } = Array.AsReadOnly(new[] { "limit", "cursor", "includeTotal" });
}
