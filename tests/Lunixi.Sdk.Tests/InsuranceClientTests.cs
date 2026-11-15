using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using static Lunixi.Sdk.Tests.PhysicalPosTestSupport;

namespace Lunixi.Sdk.Tests;

public class InsuranceClientTests
{
    private const string PaymentIntentId = "3f1c2b4a-5d6e-4f70-8a9b-0c1d2e3f4a5b";
    private const string OrderAnswer = "{\"data\":{\"orderId\":\"inso_1\",\"premiumCollectedBy\":\"MERCHANT\",\"premiumAmount\":\"12500\",\"currency\":\"TRY\"}}";

    [Fact]
    public async Task RoutesAreSignedAndKeysFollowTheContract()
    {
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json(OrderAnswer));
        using (client)
        {
            var insurance = client.Insurance;
            await insurance.CreateQuoteAsync(new { insuranceType = "TRAVEL_HEALTH", currency = "try", paymentIntentId = PaymentIntentId, merchantOrderId = "" });
            await insurance.CreateQuoteAsync(new { insuranceType = "DEVICE", currency = "TRY", context = new { items = new[] { new BasketItem("i1", "Phone", "1000000") } } }, "quote-trace-000001");
            await insurance.CreateOrderAsync(new
            {
                quoteId = "insq_1",
                consent = new { termsVersion = "v3", termsSha256 = "abc", acceptedAt = "2026-11-15T07:59:00+03:00" },
            }, "order-create-000001");
            await insurance.BindPaymentAsync("inso_1", PaymentIntentId);
            await insurance.ConfirmPaymentAsync("inso_1", new { externalPaymentId = "ext-1", paidAmount = "12500", paidCurrency = "TRY", paidAt = "2026-11-15T08:00:00Z" }, "confirm-payment-01");
            await insurance.IssuePolicyAsync("inso_1", "issue-policy-000001");
            await insurance.CancelOrderAsync("inso_1", "cancel-order-000001");
            await insurance.RefundPremiumAsync("inso_1", "refund-premium-0001");
            await insurance.RefundPremiumAsync("inso_1", "refund-premium-0002", new { externalRefund = new { externalRefundId = "r-1", refundedAt = "2026-11-15T09:00:00Z" } });
            await insurance.CreateLeadAsync(new { insuranceType = "CARGO_TRANSIT", policyholder = new { name = "A" }, note = " as given " }, "lead-create-000001");

            Assert.Equal(new[]
            {
                "POST /api/v1/insurance/quotes",
                "POST /api/v1/insurance/quotes",
                "POST /api/v1/insurance/orders",
                "POST /api/v1/insurance/orders/inso_1/bind-payment",
                "POST /api/v1/insurance/orders/inso_1/confirm-payment",
                "POST /api/v1/insurance/orders/inso_1/issue-policy",
                "POST /api/v1/insurance/orders/inso_1/cancel",
                "POST /api/v1/insurance/orders/inso_1/refund-premium",
                "POST /api/v1/insurance/orders/inso_1/refund-premium",
                "POST /api/v1/insurance/leads",
            }, requests.Select(r => r.Method + " " + r.PathAndQuery));
            Assert.All(requests, request => Assert.True(request.Signed));
            Assert.Equal(new string?[]
            {
                null, "quote-trace-000001", "order-create-000001", null, "confirm-payment-01", "issue-policy-000001", "cancel-order-000001",
                "refund-premium-0001", "refund-premium-0002", "lead-create-000001",
            }, requests.Select(r => r.IdempotencyKey));
            Assert.Equal("{\"insuranceType\":\"TRAVEL_HEALTH\",\"currency\":\"TRY\",\"paymentIntentId\":\"" + PaymentIntentId + "\"}", requests[0].Body);
            Assert.Contains("\"context\":{\"items\":[{\"id\":\"i1\",\"name\":\"Phone\",\"itemType\":\"PHYSICAL\",\"price\":\"1000000\"}]}", requests[1].Body);
            Assert.Equal("{\"quoteId\":\"insq_1\",\"consent\":{\"termsVersion\":\"v3\",\"termsSha256\":\"abc\",\"acceptedAt\":\"2026-11-15T07:59:00+03:00\"}}", requests[2].Body);
            Assert.Equal("{\"paymentIntentId\":\"" + PaymentIntentId + "\"}", requests[3].Body);
            Assert.StartsWith("{\"payment\":{\"externalPaymentId\":\"ext-1\",\"paidAmount\":\"12500\"", requests[4].Body);
            Assert.Null(requests[5].Body);
            Assert.Equal("{}", requests[7].Body);
            Assert.Equal("{\"externalRefund\":{\"externalRefundId\":\"r-1\",\"refundedAt\":\"2026-11-15T09:00:00Z\"}}", requests[8].Body);
            Assert.Equal("{\"insuranceType\":\"CARGO_TRANSIT\",\"policyholder\":{\"name\":\"A\"},\"note\":\" as given \"}", requests[9].Body);
        }
    }

    [Fact]
    public async Task InvalidRequestsFailBeforeSending()
    {
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json(OrderAnswer));
        using (client)
        {
            var insurance = client.Insurance;
            var consent = new { termsVersion = "v3", termsSha256 = "abc", acceptedAt = "2026-11-15T07:59:00Z" };
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => insurance.CreateQuoteAsync(new
            {
                insuranceType = "DEVICE",
                currency = "TRY",
                paymentIntentId = PaymentIntentId,
                context = new { items = new[] { new { id = "i1" } } },
            }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => insurance.CreateQuoteAsync(new { insuranceType = "DEVICE", currency = "TRY", providerCode = "X" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => insurance.CreateQuoteAsync(new { insuranceType = "DEVICE", currency = "TRY", paymentIntentId = "pay_1" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => insurance.CreateQuoteAsync(new { insuranceType = "DEVICE", currency = "TRY", context = new { items = Array.Empty<object>() } }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => insurance.CreateQuoteAsync(new { insuranceType = "DEVICE", currency = "TRY" }, "short"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => insurance.CreateOrderAsync(new { quoteId = "insq_1", consent }, ""));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => insurance.CreateOrderAsync(new { quoteId = "insq_1", consent = new { termsVersion = "v3", termsSha256 = "abc" } }, "order-create-000002"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => insurance.CreateOrderAsync(new { quoteId = "insq_1", consent, amount = "1" }, "order-create-000003"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => insurance.ConfirmPaymentAsync("inso_1", new { externalPaymentId = "e", paidAmount = 12500, paidCurrency = "TRY", paidAt = "2026-11-15T08:00:00Z" }, "confirm-payment-02"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => insurance.IssuePolicyAsync("inso_1", "has a space"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => insurance.CancelOrderAsync("inso_1", null!));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => insurance.RefundPremiumAsync("inso_1", "refund-premium-0003", new { externalRefund = new { externalRefundId = "r-1" }, reason = "x" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => insurance.CreateLeadAsync(new { insuranceType = "CARGO_TRANSIT", currency = "TRY" }, "lead-create-000002"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => insurance.CreateLeadAsync(new { insuranceType = "CARGO_TRANSIT", note = new string('n', 1001) }, "lead-create-000003"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => insurance.CreateLeadAsync(new { insuranceType = "CARGO_TRANSIT" }, ""));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => insurance.ListProductsAsync(new Dictionary<string, object?> { ["insuranceType"] = "DEVICE" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => insurance.ListOrdersAsync(new Dictionary<string, object?> { ["limit"] = 0 }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => insurance.ListLeadsAsync(new Dictionary<string, object?> { ["status"] = "DONE" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => insurance.ListReferenceValuesAsync("ADDRESS_CITY"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => insurance.GetReconciliationSummaryAsync(new Dictionary<string, object?> { ["from"] = "2026-11-01T00:00:00Z" }));
            Assert.Empty(requests);
        }
    }

    [Fact]
    public async Task ReadsEncodeTheirFilters()
    {
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json("{\"data\":{\"items\":[]},\"pageInfo\":{\"hasMore\":false,\"nextCursor\":null}}"));
        using (client)
        {
            var insurance = client.Insurance;
            await insurance.ListProductsAsync(new Dictionary<string, object?> { ["currency"] = "try", ["paymentIntentId"] = PaymentIntentId });
            await insurance.ListReferenceValuesAsync(InsuranceReferenceKinds.District, new Dictionary<string, object?> { ["parentCode"] = "34", ["limit"] = 100 });
            await insurance.ListReconciliationOrdersAsync(new Dictionary<string, object?>
            {
                ["from"] = DateTimeOffset.Parse("2026-11-01T00:00:00+03:00"),
                ["to"] = "2026-12-01T00:00:00+03:00",
                ["limit"] = 100,
            });
            await insurance.ListLeadsAsync(new Dictionary<string, object?> { ["status"] = InsuranceLeadStatuses.Unknown });

            Assert.Equal(new[]
            {
                "/api/v1/insurance/products?currency=TRY&paymentIntentId=" + PaymentIntentId,
                "/api/v1/insurance/reference/DISTRICT?parentCode=34&limit=100",
                "/api/v1/insurance/reconciliation/orders?from=2026-10-31T21%3A00%3A00.000Z&to=2026-12-01T00%3A00%3A00%2B03%3A00&limit=100",
                "/api/v1/insurance/leads?status=UNKNOWN",
            }, requests.Select(r => r.PathAndQuery));
            Assert.All(requests, request => Assert.True(request.Signed && request.Method == "GET"));
        }
    }

    [Fact]
    public async Task PolicyFilesAreBytesAndTheExportTrailerIsRead()
    {
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.7 synthetic");
        var csv = Encoding.UTF8.GetBytes("﻿orderId,policyId\ninso_1,insp_1\n# truncated=true orderCap=20000 nextCursor=abc123\n");
        var (client, requests) = RecordingClient.Create(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var isCsv = path.EndsWith("/export", StringComparison.Ordinal);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(isCsv ? csv : pdf) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(isCsv ? "text/csv" : "application/pdf");
            return response;
        });
        using (client)
        {
            var document = await client.Insurance.GetPolicyDocumentAsync("insp_1");
            var receipt = await client.Insurance.GetPolicyReceiptAsync("insp_1");
            var certificate = await client.Insurance.GetPolicyCertificateAsync("insp_1");
            var export = await client.Insurance.ExportReconciliationAsync(new Dictionary<string, object?> { ["from"] = "2026-11-01T00:00:00Z", ["to"] = "2026-12-01T00:00:00Z" });

            Assert.Equal(pdf, document.ToArray());
            Assert.Equal(("application/pdf", "application/pdf"), (receipt.ContentType, certificate.ContentType));
            Assert.Equal((true, "abc123", false), (export.Truncated, export.NextCursor, export.Aborted));
            Assert.Equal(csv, export.File.ToArray());
            Assert.Equal(new[]
            {
                "/api/v1/insurance/policies/insp_1/document",
                "/api/v1/insurance/policies/insp_1/receipt",
                "/api/v1/insurance/policies/insp_1/certificate",
                "/api/v1/insurance/reconciliation/export?from=2026-11-01T00%3A00%3A00Z&to=2026-12-01T00%3A00%3A00Z",
            }, requests.Select(r => r.PathAndQuery));
            Assert.Equal("application/pdf, application/json", requests[0].Headers["Accept"]);
            Assert.Equal("text/csv, application/json", requests[3].Headers["Accept"]);
            Assert.All(requests, request => Assert.True(request.Signed));
        }
    }

    [Fact]
    public async Task AnAbortedExportIsFlagged()
    {
        var (client, _) = RecordingClient.Create(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("orderId\ninso_1\r\n# export-aborted=true\r\n")) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
            return response;
        });
        using (client)
        {
            var export = await client.Insurance.ExportReconciliationAsync(new Dictionary<string, object?> { ["from"] = "2026-11-01T00:00:00Z", ["to"] = "2026-12-01T00:00:00Z" });
            Assert.Equal((false, (string?)null, true), (export.Truncated, export.NextCursor, export.Aborted));
        }
    }

    [Fact]
    public void ConsentAndPremiumItemHelpers()
    {
        var option = Element("{\"quoteId\":\"insq_1\",\"terms\":{\"version\":\"v3\",\"sha256\":\"abc\",\"url\":\"https://x\"}}");
        var consent = InsuranceClient.ConsentFromQuote(option, DateTimeOffset.Parse("2026-11-15T10:59:00+03:00"));
        Assert.Equal("{\"termsVersion\":\"v3\",\"termsSha256\":\"abc\",\"acceptedAt\":\"2026-11-15T07:59:00.000Z\"}", consent.ToJsonString());
        Assert.Throws<LunixiConfigurationException>(() => InsuranceClient.ConsentFromQuote(Element("{\"quoteId\":\"insq_1\"}"), "2026-11-15T07:59:00Z"));

        var item = InsuranceClient.PremiumBasketItem(Element(OrderAnswer).GetProperty("data"), "Travel insurance", "Insurance");
        Assert.Equal(("inso_1", "12500", "VIRTUAL", "Insurance"), (item.Id, item.Price, item.ItemType, item.Category1));
        Assert.Throws<LunixiConfigurationException>(() => InsuranceClient.PremiumBasketItem(
            Element("{\"orderId\":\"inso_1\",\"premiumCollectedBy\":\"PROVIDER\",\"premiumAmount\":\"12500\"}"), "Travel insurance", "Insurance"));
        Assert.Throws<LunixiConfigurationException>(() => InsuranceClient.PremiumBasketItem(
            Element("{\"orderId\":\"inso_1\",\"premiumCollectedBy\":\"MERCHANT\",\"premiumAmount\":12500}"), "Travel insurance", "Insurance"));
    }

    [Fact]
    public void WebhookPayloadsAreRead()
    {
        var order = new JsonObject
        {
            ["eventId"] = "evt_1",
            ["eventType"] = InsuranceEvents.PolicyIssued,
            ["merchantId"] = "m_1",
            ["insuranceOrderId"] = "inso_1",
            ["paymentIntentId"] = PaymentIntentId,
            ["merchantOrderId"] = null,
            ["insuranceType"] = "TRAVEL_HEALTH",
            ["premiumCollectedBy"] = "MERCHANT",
            ["provider"] = new JsonObject { ["code"] = "PROVIDER_A", ["name"] = "Provider", ["logoUrl"] = "https://x/logo.png", ["connectionId"] = "c_1" },
            ["status"] = "ACTIVE",
            ["collectionStatus"] = "COLLECTED",
            ["policyStatus"] = "ISSUED",
            ["refundStatus"] = "NONE",
            ["documentStatus"] = "AVAILABLE",
            ["premiumAmount"] = "12500",
            ["currency"] = "TRY",
            ["createdAt"] = "2026-11-15T07:59:00Z",
            ["policies"] = new JsonArray(new JsonObject
            {
                ["policyId"] = "insp_1",
                ["basketItemId"] = null,
                ["policyNumber"] = "P-1",
                ["policyStatus"] = "ISSUED",
                ["documentStatus"] = "AVAILABLE",
                ["documentUrl"] = "https://gateway.example/api/v1/insurance/policies/insp_1/document",
            }),
        };
        var data = InsuranceWebhookData.FromWebhook(Verify(InsuranceEvents.PolicyIssued, order));
        Assert.Equal(("evt_1", "inso_1", "12500", "c_1"), (data.EventId, data.InsuranceOrderId, data.PremiumAmount, data.Provider!.ConnectionId));
        Assert.Equal("insp_1", Assert.Single(data.Policies).PolicyId);
        Assert.Null(data.MerchantOrderId);
        Assert.Throws<LunixiWebhookException>(() => InsuranceWebhookData.FromWebhook(Verify(InsuranceEvents.LeadSubmitted, order)));
        order["premiumAmount"] = 12500;
        Assert.Throws<LunixiWebhookException>(() => InsuranceWebhookData.FromWebhook(Verify(InsuranceEvents.PolicyIssued, order)));

        var lead = new JsonObject
        {
            ["eventId"] = "evt_2",
            ["eventType"] = InsuranceEvents.LeadSubmitted,
            ["merchantId"] = "m_1",
            ["createdAt"] = "2026-11-15T07:59:00Z",
            ["lead"] = new JsonObject { ["leadId"] = "insl_1", ["insuranceType"] = "CARGO_TRANSIT", ["schemaVersion"] = 2, ["status"] = "SUBMITTED", ["submittedAt"] = "2026-11-15T07:59:00Z" },
        };
        var submitted = InsuranceLeadWebhookData.FromWebhook(Verify(InsuranceEvents.LeadSubmitted, lead));
        Assert.Equal(("insl_1", 2, "SUBMITTED"), (submitted.Lead.LeadId, submitted.Lead.SchemaVersion, submitted.Lead.Status));
        Assert.Throws<LunixiWebhookException>(() => InsuranceLeadWebhookData.FromWebhook(Verify(InsuranceEvents.PolicyIssued, lead)));
    }

    private static LunixiWebhook Verify(string type, JsonObject data)
    {
        var (raw, headers) = Webhook(type, (JsonObject)data.DeepClone());
        return new WebhookVerifier(WebhookSecret, new TestClock(Now)).Verify(raw, headers);
    }
}
