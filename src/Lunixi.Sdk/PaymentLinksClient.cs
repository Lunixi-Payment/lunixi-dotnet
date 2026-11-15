using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lunixi;

/// <summary>
/// Payment links (<c>/api/v1/payments/links</c>). Every gateway call here is signed, reads included; the merchant and the
/// environment come from the signing key, so a TEST key sees and changes only TEST links. The key needs the matching scope
/// (<c>payment-link:create</c>, <c>:view</c>, <c>:manage</c>, <c>:send</c>); a missing scope is 403
/// <c>API_INSUFFICIENT_SCOPE</c>. List answers carry <c>data.items</c> and the envelope's top-level <c>pageInfo</c>.
/// The image PUT of <see cref="UploadImageAsync"/> is the one request that is not signed: it goes to the storage host
/// behind a presigned URL and carries no credentials.
/// </summary>
public sealed class PaymentLinksClient
{
    private const string BasePath = "/api/v1/payments/links";
    private const string ImagesPath = BasePath + "/images";
    private const int QrMinSize = 128;
    private const int QrMaxSize = 2048;
    private static readonly string[] Actions = ["pause", "resume", "deactivate"];
    private readonly LunixiClient client;
    internal PaymentLinksClient(LunixiClient client) => this.client = client;

    /// <summary>
    /// Creates a payment link. The idempotency key is required (8–200 printable characters): the same key with the same
    /// body returns the stored link with <c>replayed: true</c> (HTTP 200); the same key with a different body is 409
    /// <c>payment_link.idempotency.conflict</c>.
    /// </summary>
    public Task<LunixiResponse> CreateAsync(CreatePaymentLinkRequest request, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CreateAsync((object)request, idempotencyKey, cancellationToken);
    }
    /// <summary>Creates a payment link from a raw JSON body after the same field checks as <see cref="CreatePaymentLinkRequest"/>.</summary>
    public Task<LunixiResponse> CreateAsync(object request, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = PaymentLinkValidation.CreateBody(request); LinkKey(idempotencyKey);
        return Send(HttpMethod.Post, BasePath, body, idempotencyKey, cancellationToken);
    }
    /// <summary>
    /// One page of links, newest first. Filters: <c>limit</c> (1–100), <c>cursor</c>, <c>includeTotal</c>, <c>state</c>,
    /// <c>usage</c>, <c>amountMode</c> (comma lists), <c>environment</c>, <c>tag</c>, <c>branchCode</c>, <c>createdById</c>,
    /// <c>query</c> (a short code, or a title/reference prefix), <c>createdFrom</c> / <c>createdTo</c> (half-open window).
    /// </summary>
    public Task<LunixiResponse> ListAsync(IReadOnlyDictionary<string, string>? filters = null, CancellationToken cancellationToken = default)
        => Send(HttpMethod.Get, BasePath + RequestValidation.Query(filters, PaymentLinkFields.ListFilters), null, null, cancellationToken);
    /// <summary>Reads one link.</summary>
    public Task<LunixiResponse> GetAsync(string linkId, CancellationToken cancellationToken = default)
        => Send(HttpMethod.Get, LinkPath(linkId), null, null, cancellationToken);
    /// <summary>
    /// Changes a link and creates a new version of it. Only the fields in <paramref name="changes"/> are updated; a JSON
    /// null clears a field (pass a <see cref="JsonObject"/> or a dictionary to send null) and a nested object is replaced
    /// as a whole. <paramref name="expectedRowVersion"/> is the link's current <c>rowVersion</c>; a change in between is 409
    /// <c>payment_link.link.version_conflict</c>. <c>usage</c>, <c>amountMode</c> and <c>currency</c> cannot change.
    /// </summary>
    public Task<LunixiResponse> UpdateAsync(string linkId, long expectedRowVersion, object changes, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        var path = LinkPath(linkId); var body = PaymentLinkValidation.UpdateBody(expectedRowVersion, changes); OptionalLinkKey(idempotencyKey);
        return Send(HttpMethod.Patch, path, body, idempotencyKey, cancellationToken);
    }
    /// <summary>Stops accepting payments until <see cref="ResumeAsync"/>.</summary>
    public Task<LunixiResponse> PauseAsync(string linkId, long? expectedRowVersion = null, string? reason = null, string? idempotencyKey = null, CancellationToken cancellationToken = default)
        => ActionAsync(linkId, "pause", expectedRowVersion, reason, idempotencyKey, cancellationToken);
    /// <summary>Accepts payments again after <see cref="PauseAsync"/>.</summary>
    public Task<LunixiResponse> ResumeAsync(string linkId, long? expectedRowVersion = null, string? reason = null, string? idempotencyKey = null, CancellationToken cancellationToken = default)
        => ActionAsync(linkId, "resume", expectedRowVersion, reason, idempotencyKey, cancellationToken);
    /// <summary>Permanently closes the link. It cannot be reopened.</summary>
    public Task<LunixiResponse> DeactivateAsync(string linkId, long? expectedRowVersion = null, string? reason = null, string? idempotencyKey = null, CancellationToken cancellationToken = default)
        => ActionAsync(linkId, "deactivate", expectedRowVersion, reason, idempotencyKey, cancellationToken);
    /// <summary>
    /// <c>pause</c>, <c>resume</c> or <c>deactivate</c>. <paramref name="expectedRowVersion"/> is optional here (null means no
    /// version check); an invalid transition is 409 <c>payment_link.link.invalid_transition</c>.
    /// </summary>
    public Task<LunixiResponse> ActionAsync(string linkId, string action, long? expectedRowVersion = null, string? reason = null, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        if (action is null || !Actions.Contains(action))
            throw new LunixiConfigurationException($"Payment link action must be one of {string.Join(", ", Actions)}.");
        var path = LinkPath(linkId) + "/" + action;
        var body = new JsonObject();
        if (expectedRowVersion is { } version) body["expectedRowVersion"] = PaymentLinkValidation.RowVersion(version);
        if (reason is not null) { RequestValidation.Text(reason, "reason", 4096); body["reason"] = reason; }
        OptionalLinkKey(idempotencyKey);
        return Send(HttpMethod.Post, path, body, idempotencyKey, cancellationToken);
    }
    /// <summary>
    /// Sends the link by e-mail or SMS (<see cref="PaymentLinkDeliveryChannels"/>). Without a recipient in
    /// <paramref name="options"/> the link's own recipient is used. The idempotency key is required so a retried call cannot
    /// send twice. A delivery can be accepted and still fail: check <c>data.delivery.state</c> and <c>failureReason</c>.
    /// </summary>
    public Task<LunixiResponse> SendAsync(string linkId, string channel, string idempotencyKey, PaymentLinkSendOptions? options = null, CancellationToken cancellationToken = default)
    {
        var path = LinkPath(linkId) + "/send";
        var body = new JsonObject
        {
            ["channel"] = PaymentLinkValidation.OneOf(channel, [PaymentLinkDeliveryChannels.Email, PaymentLinkDeliveryChannels.Sms], "Send channel"),
        };
        if (options?.RecipientEmail is { } email) { RequestValidation.Text(email, "recipientEmail", 320); body["recipientEmail"] = email; }
        if (options?.RecipientPhone is { } phone) { RequestValidation.Text(phone, "recipientPhone", 32); body["recipientPhone"] = phone; }
        if (options?.Locale is { } locale) body["locale"] = PaymentLinkValidation.OneOf(locale, [PaymentLinkLocales.Turkish, PaymentLinkLocales.English], "Send locale");
        LinkKey(idempotencyKey);
        return Send(HttpMethod.Post, path, body, idempotencyKey, cancellationToken);
    }
    /// <summary>
    /// The link's QR code as an image (<see cref="PaymentLinkQrFormats"/>; <paramref name="size"/> in pixels, 128–2048, gateway
    /// default 512). The QR encodes the link URL. Errors are read as the JSON envelope and thrown as <see cref="LunixiApiException"/>.
    /// </summary>
    public Task<LunixiBinaryResponse> QrAsync(string linkId, string format = PaymentLinkQrFormats.Svg, int? size = null, CancellationToken cancellationToken = default)
    {
        var path = LinkPath(linkId) + "/qr";
        PaymentLinkValidation.OneOf(format, [PaymentLinkQrFormats.Svg, PaymentLinkQrFormats.Png], "QR format");
        if (size is < QrMinSize or > QrMaxSize)
            throw new LunixiConfigurationException($"QR size must be {QrMinSize}-{QrMaxSize} pixels.");
        var query = "?format=" + format + (size is { } pixels ? "&size=" + pixels.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
        var accept = format == PaymentLinkQrFormats.Svg ? "image/svg+xml, application/json" : "image/png, application/json";
        return client.SendBinaryAsync(path + query, accept, cancellationToken);
    }
    /// <summary>Payment attempts on the link, newest first. Filters: <c>limit</c>, <c>cursor</c>, <c>includeTotal</c>, <c>state</c> (<see cref="PaymentLinkAttemptStates"/>, comma list).</summary>
    public Task<LunixiResponse> ListAttemptsAsync(string linkId, IReadOnlyDictionary<string, string>? filters = null, CancellationToken cancellationToken = default)
        => Send(HttpMethod.Get, LinkPath(linkId) + "/attempts" + RequestValidation.Query(filters, PaymentLinkFields.AttemptListFilters), null, null, cancellationToken);
    /// <summary>Successful payments on the link, with refund totals. Filters: <c>limit</c>, <c>cursor</c>, <c>includeTotal</c>.</summary>
    public Task<LunixiResponse> ListPaymentsAsync(string linkId, IReadOnlyDictionary<string, string>? filters = null, CancellationToken cancellationToken = default)
        => Send(HttpMethod.Get, LinkPath(linkId) + "/payments" + RequestValidation.Query(filters, PaymentLinkFields.PaymentListFilters), null, null, cancellationToken);

    /// <summary>
    /// Link image, step 1 of 3: an upload target for a PNG, JPEG or WebP image (<c>POST /api/v1/payments/links/images</c>).
    /// Step 2 is a PUT of the bytes to <see cref="PaymentLinkImageUpload.UploadUrl"/> with exactly
    /// <see cref="PaymentLinkImageUpload.UploadHeaders"/> before it expires, never with the access token or the signature;
    /// step 3 is <see cref="ConfirmImageUploadAsync"/>. <see cref="UploadImageAsync"/> runs all three. A declared size over
    /// the image limit (2 MiB) is refused here by the gateway, before anything is uploaded.
    /// </summary>
    public async Task<PaymentLinkImageUpload> CreateImageUploadAsync(string fileName, string contentType, long sizeBytes, CancellationToken cancellationToken = default)
    {
        if (fileName is not { Length: >= 1 and <= 255 } || string.IsNullOrWhiteSpace(fileName) || fileName.Any(char.IsControl))
            throw new LunixiConfigurationException("fileName must be 1–255 characters without control characters.");
        PaymentLinkValidation.OneOf(contentType, PaymentLinkImageContentTypes.All, "Image contentType");
        if (sizeBytes < 1 || sizeBytes > Validation.MaxSafeInteger)
            throw new LunixiConfigurationException("Image sizeBytes must be the file size in bytes (a positive integer).");
        var body = new JsonObject { ["fileName"] = fileName, ["contentType"] = contentType, ["sizeBytes"] = sizeBytes };
        var response = await Send(HttpMethod.Post, ImagesPath, body, null, cancellationToken).ConfigureAwait(false);
        return PaymentLinkImageUpload.FromResponse(response);
    }
    /// <summary>
    /// Link image, step 3 of 3: publishes an uploaded image (<c>POST /api/v1/payments/links/images/{assetId}/confirm</c>).
    /// The stored bytes must be the declared type, at most 2 MiB and 4096×4096 px; a file that fails is deleted (400) and
    /// has to be uploaded again from step 1. Confirming before the PUT has landed is 400 <c>media.confirm.object_missing</c>
    /// and can be retried. Use <see cref="PaymentLinkImage.AssetId"/> as the <c>imageAssetId</c> of a link or an item.
    /// </summary>
    public async Task<PaymentLinkImage> ConfirmImageUploadAsync(string assetId, CancellationToken cancellationToken = default)
    {
        var path = ImagesPath + "/" + Uri.EscapeDataString(PaymentLinkValidation.AssetId(assetId)) + "/confirm";
        var response = await Send(HttpMethod.Post, path, new JsonObject(), null, cancellationToken).ConfigureAwait(false);
        return PaymentLinkImage.FromResponse(response);
    }
    /// <summary>
    /// Uploads a link image in one call: <see cref="CreateImageUploadAsync"/>, the PUT of <paramref name="content"/> to the
    /// returned URL, then <see cref="ConfirmImageUploadAsync"/>. The PUT goes straight to the storage host with only the
    /// upload headers; the access token and the request signature are never sent there. A failed PUT is not retried and
    /// throws <see cref="LunixiStorageUploadException"/> (for example <c>AccessDenied</c> once the URL has expired); call
    /// this method again for a new upload URL.
    /// </summary>
    public async Task<PaymentLinkImage> UploadImageAsync(string fileName, string contentType, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default)
    {
        if (content.IsEmpty) throw new LunixiConfigurationException("Image content must not be empty.");
        var target = await CreateImageUploadAsync(fileName, contentType, content.Length, cancellationToken).ConfigureAwait(false);
        await client.PutToStorageAsync(target.UploadUrl, target.UploadHeaders, content, cancellationToken).ConfigureAwait(false);
        return await ConfirmImageUploadAsync(target.AssetId, cancellationToken).ConfigureAwait(false);
    }

    private Task<LunixiResponse> Send(HttpMethod method, string path, object? body, string? idempotencyKey, CancellationToken cancellationToken)
        => client.SendCoreAsync(method, path, body, idempotencyKey, true, Validation.MaxPaymentLinkIdempotencyKeyLength, cancellationToken);
    private static string LinkPath(string linkId) => BasePath + "/" + Validation.Id(linkId);
    private static void LinkKey(string idempotencyKey) => Validation.IdempotencyKey(idempotencyKey, Validation.MaxPaymentLinkIdempotencyKeyLength);
    private static void OptionalLinkKey(string? idempotencyKey) { if (idempotencyKey is not null) LinkKey(idempotencyKey); }
}

/// <summary>Optional recipient and language of <see cref="PaymentLinksClient.SendAsync"/>.</summary>
public sealed class PaymentLinkSendOptions
{
    /// <summary>Sends to this address instead of the link's recipient.</summary>
    public string? RecipientEmail { get; init; }
    /// <summary>Sends to this phone number (E.164) instead of the link's recipient.</summary>
    public string? RecipientPhone { get; init; }
    /// <summary>The message language (<see cref="PaymentLinkLocales"/>).</summary>
    public string? Locale { get; init; }
    /// <summary>Returns no recipient data.</summary>
    public override string ToString() => "PaymentLinkSendOptions { RequestData = [REDACTED] }";
}

/// <summary>
/// Where to PUT a link image (step 1 of <see cref="PaymentLinksClient.UploadImageAsync"/>). <see cref="UploadUrl"/> is a
/// presigned storage URL and works as a credential until it expires: do not log it.
/// </summary>
public sealed class PaymentLinkImageUpload
{
    /// <summary>The image's asset id (uuid), confirmed with <see cref="PaymentLinksClient.ConfirmImageUploadAsync"/>.</summary>
    public string AssetId { get; }
    /// <summary>The presigned storage URL (not the gateway).</summary>
    public Uri UploadUrl { get; }
    /// <summary>Always <c>PUT</c>.</summary>
    public string UploadMethod { get; }
    /// <summary>The only headers to send with the PUT, for example <c>Content-Type</c>.</summary>
    public IReadOnlyDictionary<string, string> UploadHeaders { get; }
    /// <summary>When the upload URL expires (ISO-8601), if the gateway sent it.</summary>
    public string? ExpiresAt { get; }
    /// <summary>The server request identifier, if present.</summary>
    public string? RequestId { get; }

    private PaymentLinkImageUpload(string assetId, Uri uploadUrl, IReadOnlyDictionary<string, string> uploadHeaders, string? expiresAt, string? requestId)
    { AssetId = assetId; UploadUrl = uploadUrl; UploadMethod = "PUT"; UploadHeaders = uploadHeaders; ExpiresAt = expiresAt; RequestId = requestId; }

    /// <summary>Used only if the SDK can PUT to it as documented: an asset id, an http(s) URL, the PUT method and string headers.</summary>
    internal static PaymentLinkImageUpload FromResponse(LunixiResponse response)
    {
        var data = response.Data;
        var assetId = LunixiClient.Property(data, "assetId");
        if (!PaymentLinkValidation.IsAssetId(assetId) || LunixiClient.Property(data, "uploadMethod") != "PUT" ||
            !Uri.TryCreate(LunixiClient.Property(data, "uploadUrl"), UriKind.Absolute, out var uploadUrl) ||
            uploadUrl.Scheme is not ("https" or "http") || uploadUrl.UserInfo != "" || uploadUrl.Fragment != "" ||
            !data.TryGetProperty("uploadHeaders", out var given) || given.ValueKind != JsonValueKind.Object)
            throw Unusable();
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in given.EnumerateObject())
        {
            var value = header.Value.ValueKind == JsonValueKind.String ? header.Value.GetString()! : null;
            if (value is null || value.Any(char.IsControl) || header.Name.Length == 0 ||
                header.Name.Any(c => c <= 0x20 || c >= 0x7f) || !headers.TryAdd(header.Name, value))
                throw Unusable();
        }
        return new PaymentLinkImageUpload(assetId!, uploadUrl, headers, LunixiClient.Property(data, "expiresAt"), response.RequestId);
    }
    private static LunixiTransportException Unusable() => new("Image upload creation did not return a usable upload target.");

    /// <summary>Returns no upload URL.</summary>
    public override string ToString() => $"PaymentLinkImageUpload {{ AssetId = {AssetId}, UploadUrl = [REDACTED], ExpiresAt = {ExpiresAt} }}";
}

/// <summary>A published link image (step 3 of <see cref="PaymentLinksClient.UploadImageAsync"/>).</summary>
public sealed class PaymentLinkImage
{
    /// <summary>Use as the <c>imageAssetId</c> of a link or an item.</summary>
    public string AssetId { get; }
    /// <summary>The public image URL; null when the public image host is not configured.</summary>
    public string? Url { get; }
    /// <summary>Always <c>PUBLISHED</c>.</summary>
    public string State { get; }
    /// <summary>The server request identifier, if present.</summary>
    public string? RequestId { get; }

    private PaymentLinkImage(string assetId, string? url, string state, string? requestId)
    { AssetId = assetId; Url = url; State = state; RequestId = requestId; }

    internal static PaymentLinkImage FromResponse(LunixiResponse response)
    {
        var assetId = LunixiClient.Property(response.Data, "assetId");
        var state = LunixiClient.Property(response.Data, "state");
        if (assetId is null || state != "PUBLISHED")
            throw new LunixiTransportException("Image confirmation did not return a published image.");
        return new PaymentLinkImage(assetId, LunixiClient.Property(response.Data, "url"), state, response.RequestId);
    }
}
