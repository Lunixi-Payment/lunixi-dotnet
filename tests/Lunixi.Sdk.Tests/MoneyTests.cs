using System.Text.Json;
using Lunixi;
using Xunit;
namespace Lunixi.Sdk.Tests;

public class MoneyTests
{
    [Fact]
    public void CanonicalStringsAndCurrencyExponentsRemainExact()
    {
        const string large = "9007199254740993";
        foreach (var item in new[] { (large, "JPY"), ("90071992547409.93", "TRY"), ("9007199254740.993", "KWD") })
            Assert.Equal(large, Money.DecimalToMinor(item.Item1, item.Item2));
        Assert.Equal(-9007199254740993L, Money.Parse("-9007199254740993", signed: true));
        foreach (var value in new[] { "0", "-0", "01", "+1", " 1", "1.0", "1e3", "9223372036854775808" })
            Assert.Throws<LunixiConfigurationException>(() => new CreateIntentRequest(value, "TRY", "order"));
        var request = new CreateIntentRequest(large, "TRY", "order");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(request));
        Assert.Equal(large, json.RootElement.GetProperty("Amount").GetString());
        Assert.Throws<LunixiConfigurationException>(() => Money.DecimalToMinor("1.1", "JPY"));
        Assert.Throws<LunixiConfigurationException>(() => Money.DecimalToMinor("1", "ZZZ"));
    }
    [Fact]
    public async Task TransportPreservesLargeStringsAndOpaqueMerchantData()
    {
        const string large = "9007199254740993";
        using var client = new LunixiClient(TestOptions.Create(), new FakeHandler(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/auth/token")) return FakeHandler.Json("{\"access_token\":\"token\"}");
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal(large, body.RootElement.GetProperty("amount").GetString());
            Assert.Equal(1, body.RootElement.GetProperty("metadata").GetProperty("amount").GetInt32());
            Assert.Equal(1.5m, body.RootElement.GetProperty("providerPayload").GetProperty("price").GetDecimal());
            return FakeHandler.Json("{\"data\":{\"amount\":\"9007199254740993\",\"balanceMinor\":\"-9007199254740993\",\"metadata\":{\"amount\":1},\"providerPayload\":{\"price\":1.5}}}");
        }));
        var result = await client.SendAsync(HttpMethod.Post, "/api/v1/payments/intents",
            new { amount = large, metadata = new { amount = 1 }, providerPayload = new { price = 1.5m } }, "checkout:large-string");
        Assert.Equal(large, result.Data.GetProperty("amount").GetString());
        Assert.Equal("-9007199254740993", result.Data.GetProperty("balanceMinor").GetString());
        Assert.Equal(1, result.Data.GetProperty("metadata").GetProperty("amount").GetInt32());
    }

    [Fact]
    public async Task NumericMoneyCannotEnterOrLeaveTheTransport()
    {
        using var noNetwork = new LunixiClient(TestOptions.Create(), new FakeHandler((_, _) => throw new Xunit.Sdk.XunitException("No network expected")));
        await Assert.ThrowsAsync<LunixiConfigurationException>(() => noNetwork.SendAsync(HttpMethod.Post, "/api/v1/payments/intents", new { amount = 1 }));
        await Assert.ThrowsAsync<LunixiConfigurationException>(() => noNetwork.Payments.CaptureAsync("pay-1", "capture:zero-test", "0"));
        await Assert.ThrowsAsync<LunixiConfigurationException>(() => noNetwork.Payments.RefundAsync("pay-1", "refund:zero-test", "0"));
        using var numericResponse = new LunixiClient(TestOptions.Create(), new FakeHandler((request, _) => Task.FromResult(FakeHandler.Json(
            request.RequestUri!.AbsolutePath.EndsWith("/auth/token") ? "{\"access_token\":\"token\"}" : "{\"data\":{\"amount\":1}}"))));
        await Assert.ThrowsAsync<LunixiTransportException>(() => numericResponse.Payments.GetAsync("pay-1"));
    }

    [Fact]
    public async Task ExplicitNullCannotBecomeRemainingCaptureOrRefund()
    {
        using var client = new LunixiClient(TestOptions.Create(), new FakeHandler((_, _) => throw new Xunit.Sdk.XunitException("No network expected")));
        await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Payments.CaptureAsync("pay-1", "capture:null-test", amount: null!));
        await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Payments.RefundAsync("pay-1", "refund:null-test", amount: null!));
        using var raw = JsonDocument.Parse("{\"amount\":null}");
        foreach (var operation in new[] { "capture", "refund" })
        {
            var path = "/api/v1/payments/pay-1/" + operation;
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.SendAsync(HttpMethod.Post, path, new { amount = (string?)null }, "operation:null-test"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.SendAsync(HttpMethod.Post, path, raw.RootElement, "operation:null-test"));
        }
    }

    [Fact]
    public async Task OmittedCaptureAndRefundAmountsRemainAbsent()
    {
        var bodies = new List<JsonElement>();
        using var client = new LunixiClient(TestOptions.Create(), new FakeHandler(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/auth/token")) return FakeHandler.Json("{\"access_token\":\"token\"}");
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            bodies.Add(document.RootElement.Clone());
            return FakeHandler.Json();
        }));
        await client.Payments.CaptureAsync("pay-1", "capture:remaining");
        await client.Payments.RefundAsync("pay-1", "refund:remaining");
        await client.Payments.RefundRemainingAsync("pay-1", "refund:remaining-reason", "Customer request");
        Assert.Equal(3, bodies.Count);
        Assert.All(bodies, body => Assert.False(body.TryGetProperty("amount", out _)));
    }
    [Fact]
    public async Task PendingSplitProtocolResponsesRejectNumericMoney()
    {
        var responseBody = "{\"code\":\"AWAITING_ACTION\",\"data\":{\"splitPaymentId\":\"split-1\",\"totalAmount\":1}}";
        using var client = new LunixiClient(TestOptions.Create(), new FakeHandler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath.EndsWith("/auth/token") ? FakeHandler.Json("{\"access_token\":\"token\"}") : FakeHandler.Json(responseBody, System.Net.HttpStatusCode.BadRequest))));
        var split = new SplitPaymentRequest(new { totalAmount = "1", currency = "TRY", ownerRef = "owner", legs = new[] { new { cardToken = "card-1", amount = "1" } } });
        await Assert.ThrowsAsync<LunixiTransportException>(() => client.Payments.CreateSplitAsync(split, "split:numeric-response"));
        responseBody = "{\"code\":\"AWAITING_ACTION\",\"data\":{\"splitPaymentId\":\"split-1\",\"totalAmount\":\"1\"}}";
        Assert.Equal("1", (await client.Payments.CreateSplitAsync(split, "split:string-response")).Data.GetProperty("totalAmount").GetString());
    }
}
