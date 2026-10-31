using System.Net;

namespace Lunixi.Sdk.Tests;

public class PaymentsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(9007199254740992)]
    public void InvalidMinorAmountsAreRejected(long amount) => Assert.Throws<LunixiConfigurationException>(() => new CreateIntentRequest(amount, "TRY", "order-1"));

    [Fact]
    public async Task BasketSumAndKeysAreValidatedBeforeAuthentication()
    {
        using var client = new LunixiClient(TestOptions.Create(), new FakeHandler((_, _) => throw new Xunit.Sdk.XunitException("No network expected")));
        await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Payments.CreateIntentAsync(new(10050, "TRY", "order-1") { BasketItems = [new("sku", "Product", 100)] }, "checkout:attempt-1"));
        await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Payments.CreateIntentAsync(new(10050, "TRY", "order-1"), "bad"));
        await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.SendAsync(HttpMethod.Get, "/api/v1/payments", new { amount = 1 }));
    }

    [Fact]
    public async Task CaptureRefundAndVoidUseTheDocumentedPathsAndBodies()
    {
        var bodies = new Dictionary<string, string?>();
        using var client = new LunixiClient(TestOptions.Create(), new FakeHandler(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/auth/token")) return FakeHandler.Json("{\"access_token\":\"token\"}");
            bodies[request.RequestUri.AbsolutePath] = request.Content is null ? null : await request.Content.ReadAsStringAsync(token);
            return FakeHandler.Json("{\"data\":{\"status\":\"PENDING\"}}");
        }));
        var capture = await client.Payments.CaptureAsync("pay-1", "capture:attempt-1", 10050);
        Assert.Equal("PENDING", capture.Data.GetProperty("status").GetString());
        await client.Payments.RefundAsync("pay-1", "refund:attempt-1", 100, "Requested by customer");
        await client.Payments.VoidAsync("pay-1", "void:attempt-1");
        Assert.Equal("{\"paymentIntentId\":\"pay-1\",\"amount\":10050}", bodies["/api/v1/payments/pay-1/capture"]);
        Assert.Equal("{\"paymentIntentId\":\"pay-1\",\"amount\":100,\"reason\":\"Requested by customer\"}", bodies["/api/v1/payments/pay-1/refund"]);
        Assert.Null(bodies["/api/v1/payments/pay-1/void"]);
    }

    [Fact]
    public void SplitRejectsDuplicateCardsBadTotalsRawCardFieldsAndUnsafeProviderFields()
    {
        Assert.Throws<LunixiConfigurationException>(() => new SplitPaymentRequest(new { totalAmount = 100, currency = "TRY", ownerRef = "owner", legs = new[] { new { cardToken = "card-1", amount = 50 }, new { cardToken = "card-1", amount = 50 } } }));
        Assert.Throws<LunixiConfigurationException>(() => new SplitPaymentRequest(new { totalAmount = 100, currency = "TRY", ownerRef = "owner", legs = new[] { new { cardToken = "card-1", amount = 99 } } }));
        Assert.Throws<LunixiConfigurationException>(() => new SplitPaymentRequest(new { totalAmount = 100, currency = "TRY", ownerRef = "owner", legs = new[] { new { cardNumber = "synthetic-forbidden", amount = 100 } } }));
        Assert.Throws<LunixiConfigurationException>(() => new SplitPaymentRequest(new { totalAmount = 100, currency = "TRY", ownerRef = "owner", legs = new[] { new { paymentMethod = "WALLET", amount = 100, wallet = new Dictionary<string, object> { ["providerCode"] = "wallet-1", ["constructor"] = "unsafe" } } } }));
        Assert.Throws<LunixiConfigurationException>(() => new SplitPaymentRequest(new { totalAmount = 100, currency = "TRY", ownerRef = "owner", legs = new[] { new { cardToken = "card-1", amount = 100, force3D = true } } }));
    }

    [Theory]
    [InlineData("AWAITING_ACTION", false)]
    [InlineData("CAPTURING", false)]
    [InlineData("RECONCILIATION_REQUIRED", true)]
    [InlineData("FAILED", true)]
    public async Task OnlyDocumentedSplitProgressErrorsAreReturned(string status, bool throws)
    {
        using var client = new LunixiClient(TestOptions.Create(), new FakeHandler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/auth/token")
            ? FakeHandler.Json("{\"access_token\":\"token\"}") : FakeHandler.Json("{\"code\":\"" + status + "\",\"data\":{\"splitPaymentId\":\"split-1\",\"status\":\"" + status + "\"}}", HttpStatusCode.BadRequest))));
        var split = new SplitPaymentRequest(new { totalAmount = 100, currency = "TRY", ownerRef = "owner", legs = new[] { new { cardToken = "card-1", amount = 100 } } });
        if (throws) await Assert.ThrowsAsync<LunixiValidationException>(() => client.Payments.CreateSplitAsync(split, "split:attempt-1"));
        else { var response = await client.Payments.CreateSplitAsync(split, "split:attempt-1"); Assert.Equal(400, response.StatusCode); Assert.Equal(status, response.Data.GetProperty("status").GetString()); }
    }

    [Fact]
    public void FraudDoesNotAllowTenantOverride() => Assert.Throws<LunixiConfigurationException>(() => new FraudEvaluateRequest(new { tenantId = "another-merchant" }));
}
