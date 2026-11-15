using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lunixi;

/// <summary>
/// Insurance (<c>/api/v1/insurance</c>, product <c>INSURANCE_SERVICE</c>): offer, sell and service insurance for your basket
/// items, and submit leads for the types sold by request only. Every call is signed, reads included; a key without the
/// product is 403 <c>PRODUCT_NOT_ENABLED</c>, and the key decides the environment (TEST or LIVE).
/// <para>
/// Request fields and query filters are checked before anything is sent (<see cref="InsuranceFields"/>): an unknown field or
/// filter, a missing required value, a basket sent both as <c>paymentIntentId</c> and <c>context.items</c>, a malformed id,
/// currency, amount or <c>Idempotency-Key</c> is a <see cref="LunixiConfigurationException"/>. The field rules of an insurance
/// type's own object are not repeated here: a violation is 422 <c>insurance.validation.invalid</c> with every
/// <c>details.errors[{ field, reason }]</c> at once. <see cref="CreateOrderAsync"/>, <see cref="ConfirmPaymentAsync"/>,
/// <see cref="IssuePolicyAsync"/>, <see cref="CancelOrderAsync"/>, <see cref="RefundPremiumAsync"/> and
/// <see cref="CreateLeadAsync"/> require an <c>Idempotency-Key</c> (8–200 printable ASCII characters);
/// <see cref="CreateQuoteAsync"/> and <see cref="BindPaymentAsync"/> take one optionally. Amounts are minor-unit strings.
/// Methods return the envelope's <c>data</c>; lists return <see cref="LunixiPage"/>; policy files are bytes.
/// </para>
/// </summary>
public sealed class InsuranceClient
{
    private const string BasePath = "/api/v1/insurance";
    private const string PdfAccept = "application/pdf, application/json";
    private const string CsvAccept = "text/csv, application/json";
    private const int TextMaxLength = 128;
    private const int CursorMaxLength = 512;
    private readonly LunixiClient client;
    internal InsuranceClient(LunixiClient client) => this.client = client;

    // Products and reference values

    /// <summary>
    /// Insurance types offered for a payment's basket, read from the items sent when the payment was created (<c>GET
    /// /products</c>); filters <c>currency</c> (required), <c>paymentIntentId</c>, <c>insuranceType</c>. Without
    /// <c>paymentIntentId</c> it lists the types sold without a basket item. An empty list means insurance is not offered.
    /// </summary>
    public Task<JsonElement> ListProductsAsync(IReadOnlyDictionary<string, object?> filters, CancellationToken cancellationToken = default)
    {
        var query = Query(filters, "listProducts", "currency");
        return ReadAsync("/products" + query, cancellationToken);
    }

    /// <summary>
    /// Insurance types offered for basket items before the payment exists (<c>POST /products</c>, read-only): <c>{ currency,
    /// insuranceType?, context: { items } }</c> with the 1–100 items you will send in the payment.
    /// </summary>
    public async Task<JsonElement> SearchProductsAsync(object request, CancellationToken cancellationToken = default)
    {
        var body = Fields(request, InsuranceFields.ProductSearch, "searchProducts request");
        var payload = new JsonObject { ["currency"] = RouteRequest.Currency(body["currency"], "currency"), ["context"] = Context(body["context"]) };
        if (RouteRequest.Present(body["insuranceType"])) payload["insuranceType"] = Text(body["insuranceType"], "insuranceType");
        return (await SendAsync(HttpMethod.Post, "/products", payload, null, cancellationToken).ConfigureAwait(false)).Data;
    }

    /// <summary>Lunixi reference codes of one <see cref="InsuranceReferenceKinds"/> kind; filters <c>parentCode</c>, <c>limit</c>, <c>cursor</c>.</summary>
    public async Task<LunixiPage> ListReferenceValuesAsync(string kind, IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
    {
        var path = "/reference/" + Uri.EscapeDataString(ReferenceKind(kind));
        return new(await SendAsync(HttpMethod.Get, path + Query(filters, "listReferenceValues"), null, null, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Every code of <see cref="ListReferenceValuesAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateReferenceValuesAsync(string kind, IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
    {
        ReferenceKind(kind);
        return RouteRequest.IterateAsync((page, token) => ListReferenceValuesAsync(kind, page, token), filters, cancellationToken);
    }

    // Quotes and orders

    /// <summary>
    /// Prices an insurance type (HTTP 201): <c>{ insuranceType, currency, schemaVersion?, paymentIntentId?, context?,
    /// merchantOrderId?, policyholder?, insured?, &lt;type object&gt;? }</c>. The basket is either a payment's recorded basket
    /// (<c>paymentIntentId</c>) or the items you will send in the payment (<c>context.items</c>), not both; send neither for a
    /// type sold without a basket item. Every option of the answer has its own <c>quoteId</c>, <c>terms</c> and
    /// <c>expiresAt</c>; a company the request could not be priced with is in <c>excludedOptions</c>. The optional
    /// <paramref name="idempotencyKey"/> only correlates traces: every call is a new, time-limited price.
    /// </summary>
    public async Task<JsonElement> CreateQuoteAsync(object request, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        var body = Fields(request, InsuranceFields.Quote, "createQuote request");
        var hasPayment = RouteRequest.Present(body["paymentIntentId"]);
        var hasItems = RouteRequest.Present(body["context"]);
        if (hasPayment && hasItems)
            throw new LunixiConfigurationException(
                "Send the basket either as 'paymentIntentId' (a payment that exists) or as 'context.items' (before the payment), not both. Send neither for an insurance type sold without a basket item.");
        body["insuranceType"] = Text(body["insuranceType"], "insuranceType");
        body["currency"] = RouteRequest.Currency(body["currency"], "currency");
        Set(body, "paymentIntentId", hasPayment ? Uuid(body["paymentIntentId"], "paymentIntentId") : null);
        Set(body, "context", hasItems ? Context(body["context"]) : null);
        Set(body, "merchantOrderId", RouteRequest.Present(body["merchantOrderId"]) ? Text(body["merchantOrderId"], "merchantOrderId", TextMaxLength) : null);
        return (await SendAsync(HttpMethod.Post, "/quotes", body, OptionalKey(idempotencyKey), cancellationToken).ConfigureAwait(false)).Data;
    }

    /// <summary>One quote option; 404 <c>insurance.quote.not_found</c> for an unknown or another merchant's quote.</summary>
    public Task<JsonElement> GetQuoteAsync(string quoteId, CancellationToken cancellationToken = default)
        => ReadAsync("/quotes/" + RouteRequest.PathId(quoteId, "quoteId"), cancellationToken);

    /// <summary>
    /// Turns a quote option into an order (HTTP 201) and records the buyer's consent: <c>{ quoteId, merchantOrderId?, consent:
    /// { termsVersion, termsSha256, acceptedAt } }</c>. No provider call and no money movement. The same key with the same body
    /// returns the recorded order; with another body it is 409 <c>insurance.idempotency.conflict</c>. <see cref="ConsentFromQuote(JsonElement, DateTimeOffset)"/>
    /// builds <c>consent</c>.
    /// </summary>
    public async Task<JsonElement> CreateOrderAsync(object request, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var key = RequiredKey(idempotencyKey, "insurance order create");
        var body = Fields(request, InsuranceFields.Order, "createOrder request");
        if (body["consent"] is not JsonObject consentSource) throw new LunixiConfigurationException("consent must be an object.");
        var consent = Fields(consentSource, InsuranceFields.Consent, "consent");
        var payload = new JsonObject { ["quoteId"] = Text(body["quoteId"], "quoteId") };
        if (RouteRequest.Present(body["merchantOrderId"])) payload["merchantOrderId"] = Text(body["merchantOrderId"], "merchantOrderId", TextMaxLength);
        payload["consent"] = new JsonObject
        {
            ["termsVersion"] = Text(consent["termsVersion"], "consent.termsVersion"),
            ["termsSha256"] = Text(consent["termsSha256"], "consent.termsSha256"),
            ["acceptedAt"] = RouteRequest.TimeField(consent["acceptedAt"], "consent.acceptedAt"),
        };
        return (await SendAsync(HttpMethod.Post, "/orders", payload, key, cancellationToken).ConfigureAwait(false)).Data;
    }

    /// <summary>Your insurance orders, newest first; filters <c>limit</c>, <c>cursor</c>, <c>paymentIntentId</c>, <c>merchantOrderId</c>.</summary>
    public async Task<LunixiPage> ListOrdersAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => new(await SendAsync(HttpMethod.Get, "/orders" + Query(filters, "listOrders"), null, null, cancellationToken).ConfigureAwait(false));

    /// <summary>Every order of <see cref="ListOrdersAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateOrdersAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => RouteRequest.IterateAsync(ListOrdersAsync, filters, cancellationToken);

    /// <summary>One order; 404 <c>insurance.order.not_found</c> for an unknown or another merchant's order.</summary>
    public Task<JsonElement> GetOrderAsync(string orderId, CancellationToken cancellationToken = default)
        => ReadAsync(OrderPath(orderId), cancellationToken);

    /// <summary>
    /// Binds an order to the Lunixi payment that collects its premium. When you collect it (<c>premiumCollectedBy:
    /// MERCHANT</c>) the payment basket must hold the premium item (<see cref="PremiumBasketItem"/>); when the provider
    /// collects it, it must not. Binding the same payment again returns the order.
    /// </summary>
    public async Task<JsonElement> BindPaymentAsync(string orderId, string paymentIntentId, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject { ["paymentIntentId"] = Uuid(JsonValue.Create(paymentIntentId), "paymentIntentId") };
        return (await SendAsync(HttpMethod.Post, OrderPath(orderId) + "/bind-payment", body, OptionalKey(idempotencyKey), cancellationToken).ConfigureAwait(false)).Data;
    }

    /// <summary>
    /// Records a premium you collected outside Lunixi: <c>{ externalPaymentId, paidAmount, paidCurrency, paidAt,
    /// paymentProvider? }</c>; <c>paidAmount</c> and <c>paidCurrency</c> must equal the order's premium. Repeating the same
    /// <c>externalPaymentId</c> returns the order.
    /// </summary>
    public async Task<JsonElement> ConfirmPaymentAsync(string orderId, object payment, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var key = RequiredKey(idempotencyKey, "insurance payment confirmation");
        var body = Fields(payment, InsuranceFields.ExternalPayment, "payment");
        var payload = new JsonObject
        {
            ["externalPaymentId"] = Text(body["externalPaymentId"], "payment.externalPaymentId", TextMaxLength),
            ["paidAmount"] = RouteRequest.PositiveMinor(body["paidAmount"], "payment.paidAmount"),
            ["paidCurrency"] = RouteRequest.Currency(body["paidCurrency"], "payment.paidCurrency"),
            ["paidAt"] = RouteRequest.TimeField(body["paidAt"], "payment.paidAt"),
        };
        if (RouteRequest.Present(body["paymentProvider"])) payload["paymentProvider"] = Text(body["paymentProvider"], "payment.paymentProvider", 64);
        var path = OrderPath(orderId) + "/confirm-payment";
        return (await SendAsync(HttpMethod.Post, path, new JsonObject { ["payment"] = payload }, key, cancellationToken).ConfigureAwait(false)).Data;
    }

    /// <summary>
    /// Requests the policy of an order whose premium you collected outside Lunixi (<c>policyStatus: PENDING</c>; the result
    /// arrives by webhook). With a Lunixi payment the policy is requested on its own once the premium is collected. 412
    /// <c>insurance.premium.not_captured</c> before the premium is collected.
    /// </summary>
    public async Task<JsonElement> IssuePolicyAsync(string orderId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var key = RequiredKey(idempotencyKey, "insurance policy issue");
        return (await SendAsync(HttpMethod.Post, OrderPath(orderId) + "/issue-policy", null, key, cancellationToken).ConfigureAwait(false)).Data;
    }

    /// <summary>
    /// Opens a cancellation at the provider for every issued policy of the order (<c>policyStatus: CANCEL_PENDING</c>;
    /// <c>insurance.policy.cancelled</c> confirms it). Cancelling does not refund the premium: call <see cref="RefundPremiumAsync"/> after it.
    /// </summary>
    public async Task<JsonElement> CancelOrderAsync(string orderId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var key = RequiredKey(idempotencyKey, "insurance order cancel");
        return (await SendAsync(HttpMethod.Post, OrderPath(orderId) + "/cancel", null, key, cancellationToken).ConfigureAwait(false)).Data;
    }

    /// <summary>
    /// Refunds the premium. With a Lunixi payment leave <paramref name="options"/> out: the premium is refunded from that payment
    /// (an empty JSON body; <c>refundStatus: PENDING</c>, then <c>insurance.refund.completed</c>). For a premium collected
    /// outside Lunixi send the refund you made: <c>{ externalRefund: { externalRefundId, refundedAt } }</c>. An in-force policy
    /// must be cancelled first (409, <c>policy_active</c>).
    /// </summary>
    public async Task<JsonElement> RefundPremiumAsync(string orderId, string idempotencyKey, object? options = null, CancellationToken cancellationToken = default)
    {
        var key = RequiredKey(idempotencyKey, "insurance premium refund");
        var body = Fields(options, ["externalRefund"], "refundPremium options");
        var payload = new JsonObject();
        if (RouteRequest.Present(body["externalRefund"]))
        {
            if (body["externalRefund"] is not JsonObject refundSource) throw new LunixiConfigurationException("externalRefund must be an object.");
            var refund = Fields(refundSource, InsuranceFields.ExternalRefund, "externalRefund");
            payload["externalRefund"] = new JsonObject
            {
                ["externalRefundId"] = Text(refund["externalRefundId"], "externalRefund.externalRefundId", TextMaxLength),
                ["refundedAt"] = RouteRequest.TimeField(refund["refundedAt"], "externalRefund.refundedAt"),
            };
        }
        return (await SendAsync(HttpMethod.Post, OrderPath(orderId) + "/refund-premium", payload, key, cancellationToken).ConfigureAwait(false)).Data;
    }

    // Leads

    /// <summary>
    /// Submits a lead (HTTP 201) for an insurance type sold by request only: the provider gives no quote, so there is no
    /// premium, order, policy or document. Body: <c>insuranceType</c>, <c>schemaVersion?</c>, <c>policyholder</c>, the type
    /// object, <c>shipment?</c>, <c>company?</c> and an optional free <c>note</c> (sent as given, at most 1000 characters). The
    /// answer's <c>status</c> is <c>SUBMITTED</c> or <c>UNKNOWN</c> (not sent again); a refusal is 422 <c>insurance.lead.rejected</c>.
    /// The same key with the same body returns the same lead.
    /// </summary>
    public async Task<JsonElement> CreateLeadAsync(object request, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var key = RequiredKey(idempotencyKey, "insurance lead submit");
        var body = Fields(request, InsuranceFields.Lead, "createLead request");
        body["insuranceType"] = Text(body["insuranceType"], "insuranceType", 64);
        if (RouteRequest.Present(body["note"]))
        {
            if (RouteRequest.Text(body["note"]) is not { } note) throw new LunixiConfigurationException("'note' must be a string.");
            if (RouteRequest.CodePoints(note) > InsuranceFields.LeadNoteMaxLength)
                throw new LunixiConfigurationException($"'note' must be at most {InsuranceFields.LeadNoteMaxLength} characters.");
        }
        else body.Remove("note");
        return (await SendAsync(HttpMethod.Post, "/leads", body, key, cancellationToken).ConfigureAwait(false)).Data;
    }

    /// <summary>Your leads, newest first (personal data masked in <c>requestMasked</c>); filters <c>limit</c>, <c>cursor</c>, <c>insuranceType</c>, <c>status</c>.</summary>
    public async Task<LunixiPage> ListLeadsAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => new(await SendAsync(HttpMethod.Get, "/leads" + Query(filters, "listLeads"), null, null, cancellationToken).ConfigureAwait(false));

    /// <summary>Every lead of <see cref="ListLeadsAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateLeadsAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => RouteRequest.IterateAsync(ListLeadsAsync, filters, cancellationToken);

    /// <summary>One lead; 404 <c>insurance.lead.not_found</c> for an unknown or another merchant's lead.</summary>
    public Task<JsonElement> GetLeadAsync(string leadId, CancellationToken cancellationToken = default)
        => ReadAsync("/leads/" + RouteRequest.PathId(leadId, "leadId"), cancellationToken);

    // Policy files

    /// <summary>
    /// The policy PDF. It is read from the provider on every call and is available while the policy is in force
    /// (<c>ISSUED</c>, <c>CANCEL_PENDING</c>); 503 <c>insurance.document.unavailable</c> is temporary, retry later.
    /// </summary>
    public Task<LunixiBinaryResponse> GetPolicyDocumentAsync(string policyId, CancellationToken cancellationToken = default)
        => PolicyFileAsync(policyId, "document", cancellationToken);

    /// <summary>The provider's premium receipt of the policy (PDF); only for products that issue one, otherwise 412 <c>insurance.document.not_supported</c>.</summary>
    public Task<LunixiBinaryResponse> GetPolicyReceiptAsync(string policyId, CancellationToken cancellationToken = default)
        => PolicyFileAsync(policyId, "receipt", cancellationToken);

    /// <summary>The provider's certificate of the policy (PDF, for example a motor Green Card); only for products that issue one, otherwise 412 <c>insurance.document.not_supported</c>.</summary>
    public Task<LunixiBinaryResponse> GetPolicyCertificateAsync(string policyId, CancellationToken cancellationToken = default)
        => PolicyFileAsync(policyId, "certificate", cancellationToken);

    // Reconciliation

    /// <summary>
    /// Reconciliation totals grouped by provider connection, currency and <c>premiumCollectedBy</c>. <c>from</c> (inclusive) and
    /// <c>to</c> (exclusive) are required, on the order creation time, as a <see cref="DateTimeOffset"/> or ISO-8601 text with a
    /// time zone, at most 366 days apart; other filters <c>connectionId</c>, <c>insuranceType</c>, <c>currency</c>.
    /// </summary>
    public Task<JsonElement> GetReconciliationSummaryAsync(IReadOnlyDictionary<string, object?> filters, CancellationToken cancellationToken = default)
        => ReadAsync("/reconciliation/summary" + Query(filters, "getReconciliationSummary", "from", "to"), cancellationToken);

    /// <summary>The orders and policies behind the summary for the same filters (plus <c>limit</c>, <c>cursor</c>), newest first.</summary>
    public async Task<LunixiPage> ListReconciliationOrdersAsync(IReadOnlyDictionary<string, object?> filters, CancellationToken cancellationToken = default)
    {
        var query = Query(filters, "listReconciliationOrders", "from", "to");
        return new(await SendAsync(HttpMethod.Get, "/reconciliation/orders" + query, null, null, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Every order of <see cref="ListReconciliationOrdersAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateReconciliationOrdersAsync(IReadOnlyDictionary<string, object?> filters, CancellationToken cancellationToken = default)
        => RouteRequest.IterateAsync(ListReconciliationOrdersAsync, filters, cancellationToken);

    /// <summary>
    /// The reconciliation report as CSV (UTF-8 with BOM, one line per policy, amounts in minor units). A file holds at most
    /// 20000 orders: when it was cut, <see cref="InsuranceReconciliationExport.Truncated"/> is true and
    /// <see cref="InsuranceReconciliationExport.NextCursor"/> continues it (pass it as <c>cursor</c>). When the file ends with the
    /// gateway's abort line, <see cref="InsuranceReconciliationExport.Aborted"/> is true: it is incomplete, request it again.
    /// </summary>
    public async Task<InsuranceReconciliationExport> ExportReconciliationAsync(IReadOnlyDictionary<string, object?> filters, CancellationToken cancellationToken = default)
    {
        var query = Query(filters, "exportReconciliation", "from", "to");
        var file = await client.SendBinaryAsync(BasePath + "/reconciliation/export" + query, CsvAccept, cancellationToken).ConfigureAwait(false);
        return new InsuranceReconciliationExport(file);
    }

    // Helpers

    /// <summary>
    /// The <c>consent</c> of <see cref="CreateOrderAsync"/>: the quote option's <c>terms</c> (<c>version</c>, <c>sha256</c>)
    /// unchanged and the moment the buyer accepted them (not in the future).
    /// </summary>
    public static JsonObject ConsentFromQuote(JsonElement option, DateTimeOffset acceptedAt) => Consent(option, RouteRequest.Instant(acceptedAt));

    /// <summary>The <c>consent</c> of <see cref="CreateOrderAsync"/>, with <paramref name="acceptedAt"/> as ISO-8601 text with a time zone.</summary>
    public static JsonObject ConsentFromQuote(JsonElement option, string acceptedAt) => Consent(option, RouteRequest.Time("acceptedAt", acceptedAt));

    /// <summary>
    /// The premium basket item for a payment that collects the premium of an order (<c>premiumCollectedBy: MERCHANT</c>):
    /// <c>id</c> = <c>orderId</c>, <c>itemType</c> <c>VIRTUAL</c>, <c>price</c> = <c>premiumAmount</c>. Add it to the payment's
    /// basket next to the insured items and add the premium to the payment amount; for insurance sold without a basket item it
    /// can be the only item. A <c>PROVIDER</c> order must not have one.
    /// </summary>
    public static BasketItem PremiumBasketItem(JsonElement order, string name, string category1, string? category2 = null)
    {
        if (order.ValueKind != JsonValueKind.Object) throw new LunixiConfigurationException("order must be an object.");
        if (LunixiClient.Property(order, "premiumCollectedBy") != InsurancePremiumCollectedBy.Merchant)
            throw new LunixiConfigurationException("Only an order whose premium you collect (premiumCollectedBy MERCHANT) has a premium basket item.");
        var orderId = Text(JsonValue.Create(LunixiClient.Property(order, "orderId")), "order.orderId");
        var premium = RouteRequest.PositiveMinor(JsonValue.Create(LunixiClient.Property(order, "premiumAmount")), "order.premiumAmount");
        return new BasketItem(orderId, Text(JsonValue.Create(name), "name"), premium, "VIRTUAL")
        {
            Category1 = Text(JsonValue.Create(category1), "category1"),
            Category2 = string.IsNullOrEmpty(category2) ? null : Text(JsonValue.Create(category2), "category2"),
        };
    }

    private static JsonObject Consent(JsonElement option, string acceptedAt)
    {
        if (option.ValueKind != JsonValueKind.Object || !option.TryGetProperty("terms", out var terms) || terms.ValueKind != JsonValueKind.Object)
            throw new LunixiConfigurationException("The quote option has no 'terms': pass an option returned by CreateQuoteAsync or GetQuoteAsync.");
        return new JsonObject
        {
            ["termsVersion"] = Text(JsonValue.Create(LunixiClient.Property(terms, "version")), "terms.version"),
            ["termsSha256"] = Text(JsonValue.Create(LunixiClient.Property(terms, "sha256")), "terms.sha256"),
            ["acceptedAt"] = acceptedAt,
        };
    }

    private static string OrderPath(string orderId) => "/orders/" + RouteRequest.PathId(orderId, "orderId");

    private Task<LunixiBinaryResponse> PolicyFileAsync(string policyId, string file, CancellationToken cancellationToken)
        => client.SendBinaryAsync(BasePath + "/policies/" + RouteRequest.PathId(policyId, "policyId") + "/" + file, PdfAccept, cancellationToken);

    private async Task<JsonElement> ReadAsync(string pathAndQuery, CancellationToken cancellationToken)
        => (await SendAsync(HttpMethod.Get, pathAndQuery, null, null, cancellationToken).ConfigureAwait(false)).Data;

    private Task<LunixiResponse> SendAsync(HttpMethod method, string pathAndQuery, JsonObject? body, string? idempotencyKey, CancellationToken cancellationToken)
        => client.SendCoreAsync(method, BasePath + pathAndQuery, body, idempotencyKey, true, 200, cancellationToken);

    /// <summary>The request object with only the allowed top-level fields (null members left out).</summary>
    private static JsonObject Fields(object? request, IReadOnlyList<string> allowed, string label)
    {
        var body = RouteRequest.ObjectOf(request, label);
        var unknown = body.Select(property => property.Key).Where(name => !allowed.Contains(name)).ToArray();
        if (unknown.Length > 0)
            throw new LunixiConfigurationException(
                $"Unknown {label} field(s): {string.Join(", ", unknown)}. Allowed: {string.Join(", ", allowed)}. The gateway rejects unknown fields with 400.");
        return body;
    }

    private static void Set(JsonObject body, string name, JsonNode? value)
    {
        if (value is null) body.Remove(name);
        else body[name] = value;
    }

    /// <summary><c>context: { items }</c>: the 1–100 basket items the payment carries (<see cref="BasketItem"/> or plain objects).</summary>
    private static JsonObject Context(JsonNode? value)
    {
        if (!RouteRequest.Present(value)) throw new LunixiConfigurationException("'context.items' is required.");
        if (value is not JsonObject context) throw new LunixiConfigurationException("context must be an object.");
        var plain = Fields(context, ["items"], "context");
        if (plain["items"] is not JsonArray items || items.Count is < 1 or > InsuranceFields.MaxContextItems)
            throw new LunixiConfigurationException($"'context.items' must hold 1 to {InsuranceFields.MaxContextItems} basket items.");
        return new JsonObject { ["items"] = items.DeepClone() };
    }

    private static string Text(JsonNode? value, string field, int? maxLength = null)
    {
        var text = RouteRequest.Text(value)?.Trim();
        if (string.IsNullOrEmpty(text)) throw new LunixiConfigurationException($"{field} is required.");
        if (maxLength is { } max && text.Length > max) throw new LunixiConfigurationException($"{field} must be at most {max} characters.");
        return text;
    }

    private static string Uuid(JsonNode? value, string field)
    {
        var id = RouteRequest.Text(value)?.Trim();
        if (id is null || id.Length != 36 || !Guid.TryParseExact(id, "D", out _)) throw new LunixiConfigurationException($"'{field}' must be a Lunixi id (UUID).");
        return id;
    }

    private static string ReferenceKind(string kind)
    {
        if (kind is null || !InsuranceReferenceKinds.All.Contains(kind))
            throw new LunixiConfigurationException($"Reference kind must be one of {string.Join(", ", InsuranceReferenceKinds.All)}.");
        return kind;
    }

    private static string? OptionalKey(string? idempotencyKey)
        => string.IsNullOrEmpty(idempotencyKey) ? null : RouteRequest.PrintableKey(idempotencyKey.Trim());

    private static string RequiredKey(string? idempotencyKey, string operation)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new LunixiConfigurationException($"A stable Idempotency-Key is required for {operation}. Reuse the same key when retrying the same operation.");
        return RouteRequest.PrintableKey(idempotencyKey.Trim());
    }

    /// <summary>The query of a read route; <paramref name="required"/> filters must be present.</summary>
    private static string Query(IReadOnlyDictionary<string, object?>? filters, string route, params string[] required)
    {
        if (required.Length > 0 && filters is null) throw new LunixiConfigurationException($"{route} requires {string.Join(" and ", required.Select(key => "'" + key + "'"))}.");
        var missing = required.Where(key => filters is null || !filters.TryGetValue(key, out var value) || value is null || value is "").ToArray();
        if (missing.Length > 0) throw new LunixiConfigurationException($"{route} requires {string.Join(" and ", missing.Select(key => "'" + key + "'"))}.");
        return RouteRequest.Query(filters, InsuranceFields.Filters[route], "Insurance " + route, FilterValue);
    }

    private static string FilterValue(string key, object value) => key switch
    {
        "limit" => RouteRequest.Integer(value) is { } limit && limit is >= 1 and <= InsuranceFields.MaxLimit
            ? limit.ToString(CultureInfo.InvariantCulture)
            : throw new LunixiConfigurationException($"'limit' must be an integer from 1 to {InsuranceFields.MaxLimit}."),
        "cursor" => value is string cursor && cursor.Length <= CursorMaxLength
            ? cursor
            : throw new LunixiConfigurationException("'cursor' must be the nextCursor of the previous page, passed back unchanged."),
        "currency" => RouteRequest.Currency(value, key),
        "paymentIntentId" or "connectionId" => Uuid(JsonValue.Create(value as string), key),
        "from" or "to" => RouteRequest.Time(key, value),
        "insuranceType" or "parentCode" => Text(JsonValue.Create(value as string), key, 64),
        "merchantOrderId" => Text(JsonValue.Create(value as string), key, TextMaxLength),
        "status" => value is string status && InsuranceLeadStatuses.All.Contains(status)
            ? status
            : throw new LunixiConfigurationException($"'status' must be one of {string.Join(", ", InsuranceLeadStatuses.All)}."),
        _ => throw new LunixiConfigurationException($"Unknown insurance filter '{key}'."),
    };
}
