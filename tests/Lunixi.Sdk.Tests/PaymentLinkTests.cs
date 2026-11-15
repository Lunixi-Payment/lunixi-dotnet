using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Lunixi.Sdk.Tests;

public class PaymentLinkTests
{
    private const string AssetId = "0f8fad5b-d9cb-469f-a165-70867728950e";
    private const string UploadUrl = "https://storage.example/bucket/link-images/0f8fad5b?X-Amz-Signature=synthetic";
    private static readonly string[] CredentialHeaders = ["Authorization", "X-Key-Id", "X-Date", "X-Nonce", "X-Signature", "Digest", "Idempotency-Key"];

    [Fact]
    public void ConstantsMatchTheReferenceValues()
    {
        Assert.Equal(new[] { "SINGLE_USE", "MULTI_USE" }, PaymentLinkUsages.All);
        Assert.Equal(new[] { "FIXED", "OPEN", "ITEMIZED" }, PaymentLinkAmountModes.All);
        Assert.Equal(new[] { "ACTIVE", "PAUSED", "COMPLETED", "EXPIRED", "DEACTIVATED", "TAKEN_DOWN" }, PaymentLinkStates.All);
        Assert.Equal(new[] { "OPEN", "BOUND", "SUCCEEDED", "FAILED", "EXPIRED", "ABANDONED", "NEEDS_REVIEW" }, PaymentLinkAttemptStates.All);
        Assert.Equal(new[] { "TEXT", "NUMBER", "EMAIL", "PHONE", "DATE", "SELECT", "CHECKBOX", "TCKN", "VKN" }, PaymentLinkCustomFieldTypes.All);
        Assert.Equal(new[] { "PAY", "DONATE", "BUY", "BOOK" }, PaymentLinkButtonLabels.All);
        Assert.Equal(new[] { "image/png", "image/jpeg", "image/webp" }, PaymentLinkImageContentTypes.All);
        Assert.Equal(("svg", "png", "tr", "en", "EMAIL", "SMS", "TEST", "LIVE"),
            (PaymentLinkQrFormats.Svg, PaymentLinkQrFormats.Png, PaymentLinkLocales.Turkish, PaymentLinkLocales.English,
             PaymentLinkDeliveryChannels.Email, PaymentLinkDeliveryChannels.Sms, PaymentLinkEnvironments.Test, PaymentLinkEnvironments.Live));
        Assert.Equal(new[] { "HIDDEN", "OPTIONAL", "REQUIRED" }, new[] { BuyerFieldRequirements.Hidden, BuyerFieldRequirements.Optional, BuyerFieldRequirements.Required });
        Assert.Equal(new[]
        {
            "usage", "amountMode", "currency", "amountMinor", "minAmountMinor", "maxAmountMinor", "suggestedAmountsMinor",
            "quantityEnabled", "minQuantity", "maxQuantity", "items", "capacity", "title", "description", "imageAssetId",
            "successMessage", "successRedirectUrl", "taxNote", "locale", "startsAt", "expiresAt", "eventAt", "buttonLabelKey",
            "requireTermsAcceptance", "buyerFields", "customFields", "recipient", "prefill", "verification", "reminders",
            "checkout", "reference", "tags", "branchCode", "salesChannel", "campaign", "agentCode", "merchantNotifyEmails", "metadata",
        }, PaymentLinkFields.Spec);
        Assert.Equal(new[] { "usage", "amountMode", "currency" }, PaymentLinkFields.Immutable);
        Assert.Equal(new[] { "limit", "cursor", "includeTotal", "state", "usage", "amountMode", "environment", "tag", "branchCode", "createdById", "query", "createdFrom", "createdTo" }, PaymentLinkFields.ListFilters);
        Assert.Equal(new[] { "limit", "cursor", "includeTotal", "state" }, PaymentLinkFields.AttemptListFilters);
        Assert.Equal(new[] { "limit", "cursor", "includeTotal" }, PaymentLinkFields.PaymentListFilters);
    }

    [Fact]
    public async Task CreateSendsTheBuilderBodyInOrderSignedWithTheKey()
    {
        var (client, requests) = RecordingClient.Create();
        using (client)
        {
            var request = CreatePaymentLinkRequest.Itemized(PaymentLinkUsages.MultiUse, "try", "Concert",
                    [new PaymentLinkItem("Ticket", "15000").WithItemKey("ticket").WithQuantityRange(0, 4).WithProductCategory(ProductCategories.EventTicket)])
                .WithExpiresAt(new DateTimeOffset(2027, 1, 31, 23, 0, 0, TimeSpan.FromHours(3)))
                .AddCustomField(new PaymentLinkCustomField("seat", "Seat", PaymentLinkCustomFieldTypes.Select).WithOptions(["A", "B"]).WithRequired())
                .WithCheckout(requireThreeDs: true, enabledInstallments: [3, 1])
                .WithMetadata(new Dictionary<string, string> { ["campaign"] = "winter" });
            await client.PaymentLinks.CreateAsync(request, "plink-create:0001");

            var sent = Assert.Single(requests);
            Assert.Equal(("POST", "/api/v1/payments/links"), (sent.Method, sent.PathAndQuery));
            Assert.True(sent.Signed);
            Assert.Equal("plink-create:0001", sent.IdempotencyKey);
            Assert.Equal("{\"usage\":\"MULTI_USE\",\"amountMode\":\"ITEMIZED\",\"currency\":\"TRY\",\"title\":\"Concert\"," +
                "\"items\":[{\"name\":\"Ticket\",\"unitPriceMinor\":\"15000\",\"itemKey\":\"ticket\",\"minQuantity\":0,\"maxQuantity\":4,\"productCategory\":\"EVENT_TICKET\"}]," +
                "\"expiresAt\":\"2027-01-31T20:00:00.000Z\"," +
                "\"customFields\":[{\"key\":\"seat\",\"label\":\"Seat\",\"fieldType\":\"SELECT\",\"options\":[\"A\",\"B\"],\"required\":true}]," +
                "\"checkout\":{\"requireThreeDs\":true,\"paymentMethods\":[],\"enabledInstallments\":[1,3]}," +
                "\"metadata\":{\"campaign\":\"winter\"}}", sent.Body);
        }
    }

    [Fact]
    public async Task CreateChecksTheKeyAndTheFieldsBeforeSending()
    {
        var (client, requests) = RecordingClient.Create();
        using (client)
        {
            var request = CreatePaymentLinkRequest.Fixed(PaymentLinkUsages.SingleUse, "150000", "TRY", "Invoice 42");
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PaymentLinks.CreateAsync(request, null!));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PaymentLinks.CreateAsync(request, "short"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PaymentLinks.CreateAsync(request, new string('k', 201)));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PaymentLinks.CreateAsync(
                new { usage = "SINGLE_USE", amountMode = "FIXED", currency = "TRY", title = "x", amountMinor = "100", price = "100" }, "plink-create:0002"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PaymentLinks.CreateAsync(
                new { usage = "SINGLE_USE", amountMode = "FIXED", currency = "TRY", title = "x", amountMinor = "100", startsAt = 5 }, "plink-create:0002"));
            Assert.Empty(requests);

            await client.PaymentLinks.CreateAsync(request, new string('k', 200));
            Assert.Equal("{\"usage\":\"SINGLE_USE\",\"amountMode\":\"FIXED\",\"currency\":\"TRY\",\"title\":\"Invoice 42\",\"amountMinor\":\"150000\"}", Assert.Single(requests).Body);
        }
        Assert.Throws<LunixiConfigurationException>(() => new CreatePaymentLinkRequest("ONCE", "FIXED", "TRY", "x"));
        Assert.Throws<LunixiConfigurationException>(() => CreatePaymentLinkRequest.Fixed("SINGLE_USE", "15.00", "TRY", "x"));
        Assert.Throws<LunixiConfigurationException>(() => CreatePaymentLinkRequest.Itemized("MULTI_USE", "TRY", "x", []));
        Assert.Throws<LunixiConfigurationException>(() => CreatePaymentLinkRequest.Fixed("SINGLE_USE", "100", "TRY", "x").WithImageAssetId("not-a-uuid"));
        Assert.Throws<LunixiConfigurationException>(() => CreatePaymentLinkRequest.Fixed("SINGLE_USE", "100", "TRY", "x")
            .WithRecipient(new Dictionary<string, string> { ["address"] = "street" }));
        Assert.Throws<LunixiConfigurationException>(() => CreatePaymentLinkRequest.Fixed("SINGLE_USE", "100", "TRY", "x").WithCheckout(enabledInstallments: [2]));
    }

    [Fact]
    public async Task UpdatePatchesTheRowVersionFirstAndRejectsFixedFields()
    {
        var (client, requests) = RecordingClient.Create();
        using (client)
        {
            await client.PaymentLinks.UpdateAsync("plk_1", 3, new JsonObject { ["title"] = "New title", ["expiresAt"] = null });
            var sent = Assert.Single(requests);
            Assert.Equal(("PATCH", "/api/v1/payments/links/plk_1"), (sent.Method, sent.PathAndQuery));
            Assert.True(sent.Signed);
            Assert.Null(sent.IdempotencyKey);
            Assert.Equal("{\"expectedRowVersion\":3,\"title\":\"New title\",\"expiresAt\":null}", sent.Body);

            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PaymentLinks.UpdateAsync("plk_1", 3, new { currency = "USD" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PaymentLinks.UpdateAsync("plk_1", 3, new { colour = "red" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PaymentLinks.UpdateAsync("plk_1", 3, new { }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PaymentLinks.UpdateAsync("plk_1", -1, new { title = "x" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PaymentLinks.UpdateAsync(" ", 3, new { title = "x" }));
            Assert.Single(requests);
        }
    }

    [Fact]
    public async Task ActionsPostTheirOptionalFields()
    {
        var (client, requests) = RecordingClient.Create();
        using (client)
        {
            await client.PaymentLinks.PauseAsync("plk_1");
            await client.PaymentLinks.ResumeAsync("plk_1", expectedRowVersion: 4);
            await client.PaymentLinks.DeactivateAsync("plk_1", 5, "Sold out", "plink-close:0001");
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PaymentLinks.ActionAsync("plk_1", "archive"));

            Assert.Equal(new[] { "/api/v1/payments/links/plk_1/pause", "/api/v1/payments/links/plk_1/resume", "/api/v1/payments/links/plk_1/deactivate" }, requests.Select(r => r.PathAndQuery));
            Assert.Equal(new[] { "{}", "{\"expectedRowVersion\":4}", "{\"expectedRowVersion\":5,\"reason\":\"Sold out\"}" }, requests.Select(r => r.Body));
            Assert.All(requests, r => Assert.True(r.Signed));
            Assert.Equal("plink-close:0001", requests[2].IdempotencyKey);
        }
    }

    [Fact]
    public async Task SendRequiresTheKeyAndAKnownChannel()
    {
        var (client, requests) = RecordingClient.Create();
        using (client)
        {
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PaymentLinks.SendAsync("plk_1", "EMAIL", ""));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PaymentLinks.SendAsync("plk_1", "FAX", "plink-send:0001"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PaymentLinks.SendAsync("plk_1", "EMAIL", "plink-send:0001", new PaymentLinkSendOptions { Locale = "de" }));
            Assert.Empty(requests);

            await client.PaymentLinks.SendAsync("plk_1", PaymentLinkDeliveryChannels.Email, "plink-send:0001",
                new PaymentLinkSendOptions { RecipientEmail = "buyer@example.com", Locale = PaymentLinkLocales.English });
            var sent = Assert.Single(requests);
            Assert.Equal(("POST", "/api/v1/payments/links/plk_1/send", "plink-send:0001"), (sent.Method, sent.PathAndQuery, sent.IdempotencyKey));
            Assert.Equal("{\"channel\":\"EMAIL\",\"recipientEmail\":\"buyer@example.com\",\"locale\":\"en\"}", sent.Body);
            Assert.True(sent.Signed);
        }
    }

    [Fact]
    public async Task QrReturnsTheImageBytes()
    {
        var png = new byte[] { 0x89, 0x50, 0x4e, 0x47 };
        var (client, requests) = RecordingClient.Create(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png) };
            response.Content.Headers.ContentType = new("image/png");
            return response;
        });
        using (client)
        {
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PaymentLinks.QrAsync("plk_1", "gif"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PaymentLinks.QrAsync("plk_1", size: 127));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PaymentLinks.QrAsync("plk_1", size: 2049));
            Assert.Empty(requests);

            var qr = await client.PaymentLinks.QrAsync("plk_1", PaymentLinkQrFormats.Png, 256);
            Assert.Equal(png, qr.ToArray());
            Assert.Equal("image/png", qr.ContentType);
            var sent = Assert.Single(requests);
            Assert.Equal(("GET", "/api/v1/payments/links/plk_1/qr?format=png&size=256"), (sent.Method, sent.PathAndQuery));
            Assert.Equal("image/png, application/json", sent.Headers["Accept"]);
            Assert.True(sent.Signed);
        }
    }

    [Fact]
    public async Task ListsAreSignedReadsWithTheirFilters()
    {
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json("{\"data\":{\"items\":[]},\"pageInfo\":{\"hasMore\":false}}"));
        using (client)
        {
            await client.PaymentLinks.ListAsync(new Dictionary<string, string> { ["state"] = "ACTIVE,PAUSED", ["limit"] = "20", ["includeTotal"] = "true" });
            await client.PaymentLinks.GetAsync("plk 1");
            await client.PaymentLinks.ListAttemptsAsync("plk_1", new Dictionary<string, string> { ["state"] = "FAILED" });
            await client.PaymentLinks.ListPaymentsAsync("plk_1", new Dictionary<string, string> { ["cursor"] = "c2" });
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PaymentLinks.ListPaymentsAsync("plk_1", new Dictionary<string, string> { ["state"] = "FAILED" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PaymentLinks.ListAsync(new Dictionary<string, string> { ["includeTotal"] = "yes" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PaymentLinks.ListAsync(new Dictionary<string, string> { ["limit"] = "101" }));

            Assert.Equal(new[]
            {
                "/api/v1/payments/links?state=ACTIVE%2CPAUSED&limit=20&includeTotal=true",
                "/api/v1/payments/links/plk%201",
                "/api/v1/payments/links/plk_1/attempts?state=FAILED",
                "/api/v1/payments/links/plk_1/payments?cursor=c2",
            }, requests.Select(r => r.PathAndQuery));
            Assert.All(requests, r => Assert.True(r.Signed && r.Method == "GET"));
        }
    }

    [Fact]
    public async Task UploadImagePutsToStorageWithOnlyTheUploadHeaders()
    {
        var (client, requests) = RecordingClient.Create(request => request.RequestUri!.Host == "storage.example"
            ? new HttpResponseMessage(HttpStatusCode.OK)
            : request.RequestUri.AbsolutePath.EndsWith("/confirm", StringComparison.Ordinal)
                ? FakeHandler.Json($"{{\"data\":{{\"assetId\":\"{AssetId}\",\"url\":\"https://img.example/{AssetId}\",\"state\":\"PUBLISHED\"}}}}")
                : FakeHandler.Json(UploadTarget(), HttpStatusCode.Created));
        using (client)
        {
            var image = await client.PaymentLinks.UploadImageAsync("logo.png", PaymentLinkImageContentTypes.Png, new byte[] { 1, 2, 3, 4 });
            Assert.Equal((AssetId, "PUBLISHED", $"https://img.example/{AssetId}"), (image.AssetId, image.State, image.Url));

            Assert.Equal(3, requests.Count);
            var (create, put, confirm) = (requests[0], requests[1], requests[2]);
            Assert.Equal(("POST", "/api/v1/payments/links/images"), (create.Method, create.PathAndQuery));
            Assert.Equal("{\"fileName\":\"logo.png\",\"contentType\":\"image/png\",\"sizeBytes\":4}", create.Body);
            Assert.True(create.Signed);

            Assert.Equal("PUT", put.Method);
            Assert.Equal(new Uri(UploadUrl), put.Uri);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, put.Bytes);
            Assert.Equal("image/png", put.Headers["Content-Type"]);
            foreach (var header in CredentialHeaders) Assert.False(put.Headers.ContainsKey(header), header + " must not reach the storage host.");
            Assert.False(put.Signed);

            Assert.Equal(("POST", $"/api/v1/payments/links/images/{AssetId}/confirm", "{}"), (confirm.Method, confirm.PathAndQuery, confirm.Body));
            Assert.True(confirm.Signed);
        }
    }

    [Fact]
    public async Task AStorageRefusalStopsBeforeConfirmation()
    {
        var (client, requests) = RecordingClient.Create(request => request.RequestUri!.Host == "storage.example"
            ? new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("<Error><Code>AccessDenied</Code><Message>Request has expired</Message></Error>", Encoding.UTF8, "application/xml") }
            : FakeHandler.Json(UploadTarget(), HttpStatusCode.Created));
        using (client)
        {
            var error = await Assert.ThrowsAsync<LunixiStorageUploadException>(() =>
                client.PaymentLinks.UploadImageAsync("logo.png", PaymentLinkImageContentTypes.Png, new byte[] { 1 }));
            Assert.Equal((403, "AccessDenied"), (error.StatusCode, error.Code));
            Assert.DoesNotContain("X-Amz-Signature", error.Message);
            Assert.Equal(2, requests.Count);
        }
    }

    [Fact]
    public async Task ImageUploadChecksTheDeclarationAndTheTarget()
    {
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json(UploadTarget(method: "POST"), HttpStatusCode.Created));
        using (client)
        {
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PaymentLinks.CreateImageUploadAsync("logo.svg", "image/svg+xml", 10));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PaymentLinks.CreateImageUploadAsync("logo.png", "image/png", 0));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PaymentLinks.CreateImageUploadAsync("", "image/png", 10));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PaymentLinks.ConfirmImageUploadAsync("asset-1"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PaymentLinks.UploadImageAsync("logo.png", "image/png", ReadOnlyMemory<byte>.Empty));
            Assert.Empty(requests);

            await Assert.ThrowsAsync<LunixiTransportException>(() => client.PaymentLinks.CreateImageUploadAsync("logo.png", "image/png", 10));
            Assert.Single(requests);
        }
        var target = await CreateTargetAsync();
        Assert.DoesNotContain("X-Amz-Signature", target.ToString());
    }

    private static async Task<PaymentLinkImageUpload> CreateTargetAsync()
    {
        var (client, _) = RecordingClient.Create(_ => FakeHandler.Json(UploadTarget(), HttpStatusCode.Created));
        using (client) return await client.PaymentLinks.CreateImageUploadAsync("logo.png", "image/png", 10);
    }

    private static string UploadTarget(string method = "PUT")
        => $"{{\"data\":{{\"assetId\":\"{AssetId}\",\"uploadUrl\":\"{UploadUrl}\",\"uploadMethod\":\"{method}\",\"uploadHeaders\":{{\"Content-Type\":\"image/png\"}},\"expiresAt\":\"2026-11-15T10:15:00.000Z\"}}}}";
}
