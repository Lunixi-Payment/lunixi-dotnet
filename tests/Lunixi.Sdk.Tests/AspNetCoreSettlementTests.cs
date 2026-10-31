using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lunixi.AspNetCoreSample;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Lunixi.Sdk.Tests;

public sealed class AspNetCoreSettlementTests
{
    private const string ApiToken = "synthetic-backend-token-at-least-32-characters";
    private const string Secret = "synthetic-webhook-secret-at-least-32";
    private const string Merchant = "merchant-demo-1";
    private const string PaymentId = "pay-demo-1";
    private static string Evidence(string status = "CAPTURED", long captured = 10050, long refunded = 0)
        => JsonSerializer.Serialize(new { data = new { id = PaymentId, merchantId = Merchant, orderId = "DEMO-1001", amount = 10050, currency = "TRY", status, details = new { capturedAmount = captured, refundedAmount = refunded } } });
    private static string Delivery(string eventId, string paymentId = PaymentId, string type = "payment.captured")
        => JsonSerializer.Serialize(new { id = eventId, type, data = new { intentId = paymentId, capturedAmountMinor = "10050", currency = "TRY" } });

    [Theory]
    [InlineData("{\"data\":{\"intentId\":\"pay-demo-1\",\"token\":\"synthetic-token\"}}")]
    [InlineData("{\"data\":{\"paymentId\":\"pay-demo-1\",\"intentId\":\"conflicting-id\",\"token\":\"synthetic-token\"}}")]
    [InlineData("{\"data\":{\"paymentId\":\"pay-demo-1\"}}")]
    [InlineData("{\"data\":{\"paymentId\":\"pay-demo-1\",\"token\":\"\"}}")]
    [InlineData("{\"data\":{\"paymentId\":\"pay-demo-1\",\"token\":123}}")]
    public async Task InvalidOrConflictingCheckoutIdentityAndTokenAreNeverBound(string response)
    {
        await using var host = new SampleHost { CreateReply = response }; await host.Start();
        Assert.Equal(HttpStatusCode.BadGateway, (await host.Checkout()).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await host.Send(Delivery("not-bound"), "not-bound")).StatusCode);
        Assert.Equal(0, host.GetCalls); Assert.Equal("UNPAID", await host.State());
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("processing")]
    [InlineData("authorized")]
    [InlineData("partial-capture")]
    [InlineData("merchant")]
    [InlineData("payment")]
    [InlineData("order")]
    [InlineData("currency")]
    [InlineData("amount")]
    [InlineData("captured-missing")]
    [InlineData("refunded-missing")]
    [InlineData("captured-fraction")]
    [InlineData("nonzero-refund")]
    [InlineData("excess-refund")]
    [InlineData("split-leg")]
    public async Task MissingDeclinedMismatchedAndIncompleteAuthenticatedReadsNeverMarkPaid(string fault)
    {
        await using var host = new SampleHost(); await host.Start(); await host.Checkout();
        host.PaymentReply = fault switch
        {
            "failed" => Evidence("FAILED", 0),
            "processing" => Evidence("PROCESSING", 0),
            "authorized" => Evidence("AUTHORIZED", 0),
            "partial-capture" => Evidence("PARTIALLY_CAPTURED", 500),
            "merchant" => Evidence().Replace(Merchant, "other-merchant", StringComparison.Ordinal),
            "payment" => Evidence().Replace(PaymentId, "other-payment", StringComparison.Ordinal),
            "order" => Evidence().Replace("DEMO-1001", "OTHER-ORDER", StringComparison.Ordinal),
            "currency" => Evidence().Replace("TRY", "USD", StringComparison.Ordinal),
            "amount" => Evidence().Replace("\"amount\":10050", "\"amount\":10051", StringComparison.Ordinal),
            "captured-missing" => Evidence().Replace("\"capturedAmount\":10050,", "", StringComparison.Ordinal),
            "refunded-missing" => Evidence().Replace(",\"refundedAmount\":0", "", StringComparison.Ordinal),
            "captured-fraction" => Evidence().Replace("\"capturedAmount\":10050", "\"capturedAmount\":100.50", StringComparison.Ordinal),
            "nonzero-refund" => Evidence(refunded: 100),
            "excess-refund" => Evidence(refunded: 10051),
            "split-leg" => Evidence().Replace("\"status\":\"CAPTURED\"", "\"status\":\"CAPTURED\",\"split\":{\"role\":\"LEG\"}", StringComparison.Ordinal),
            _ => throw new InvalidOperationException(),
        };
        var delivery = Delivery("event-query-fault");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await host.Send(delivery, "event-query-fault")).StatusCode);
        Assert.Equal("UNPAID", await host.State()); Assert.Equal(1, host.GetCalls);
        host.PaymentReply = Evidence();
        Assert.Equal(HttpStatusCode.OK, (await host.Send(delivery, "event-query-fault")).StatusCode);
        Assert.Equal("PAID", await host.State()); Assert.Equal(2, host.GetCalls);
    }

    [Fact]
    public async Task UnknownSignedPaymentNeverCausesAnUnboundGatewayRead()
    {
        await using var host = new SampleHost(); await host.Start(); await host.Checkout();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await host.Send(Delivery("other-event", "unbound-payment"), "other-event")).StatusCode);
        Assert.Equal(0, host.GetCalls); Assert.Equal("UNPAID", await host.State());
    }

    [Fact]
    public async Task ActualIntentIdEventIsAuthenticatedAndDuplicateAndStaleReadsCannotDowngradeRefund()
    {
        await using var host = new SampleHost(); await host.Start();
        Assert.Equal(HttpStatusCode.OK, (await host.Checkout()).StatusCode);
        var captured = Delivery("captured");
        Assert.Equal(HttpStatusCode.OK, (await host.Send(captured, "captured")).StatusCode);
        Assert.Equal("PAID", await host.State()); Assert.Equal(1, host.GetCalls);
        Assert.Equal(HttpStatusCode.OK, (await host.Send(captured, "captured")).StatusCode);
        Assert.Equal(1, host.GetCalls);
        host.PaymentReply = Evidence("REFUNDED", 10050, 10050);
        Assert.Equal(HttpStatusCode.OK, (await host.Send(Delivery("refunded", type: "payment.refunded"), "refunded")).StatusCode);
        Assert.Equal("REFUNDED", await host.State());
        host.PaymentReply = Evidence();
        Assert.Equal(HttpStatusCode.OK, (await host.Send(Delivery("stale-captured"), "stale-captured")).StatusCode);
        Assert.Equal("REFUNDED", await host.State());
    }

    [Fact]
    public async Task AuthorizationRemainsOnHoldAndCannotRestartCheckout()
    {
        await using var host = new SampleHost(); await host.Start(); await host.Checkout();
        host.PaymentReply = Evidence("AUTHORIZED", 0);
        Assert.Equal(HttpStatusCode.OK, (await host.Send(Delivery("held", type: "payment.authorized"), "held")).StatusCode);
        Assert.Equal("AUTHORIZED", await host.State());
        Assert.Equal(HttpStatusCode.Conflict, (await host.Checkout()).StatusCode);
    }

    [Fact]
    public void MissingMerchantConfigurationPreventsStartup()
    {
        var error = Assert.Throws<InvalidOperationException>(() => SampleApplication.Build([], builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LUNIXI_BASE_URL"] = "https://gateway.example",
                ["LUNIXI_KEY_ID"] = "mk_test_synthetic",
                ["LUNIXI_PRIVATE_KEY_PEM"] = TestOptions.Keys.PrivateKeyPem,
                ["LUNIXI_WEBHOOK_SECRET"] = Secret,
                ["DEMO_API_TOKEN"] = ApiToken,
                ["LUNIXI_DATABASE_PATH"] = "unused-test-database.db",
                ["MERCHANT_CALLBACK_URL"] = "https://merchant.example/payments/return",
                ["LUNIXI_MERCHANT_ID"] = null,
            });
        }));
        Assert.Contains("LUNIXI_MERCHANT_ID", error.Message);
    }

    [Fact]
    public async Task AuthenticatedStringMinorBalancesAreAccepted()
    {
        await using var host = new SampleHost(); await host.Start(); await host.Checkout();
        host.PaymentReply = Evidence().Replace("\"capturedAmount\":10050", "\"capturedAmount\":\"10050\"", StringComparison.Ordinal)
            .Replace("\"refundedAmount\":0", "\"refundedAmount\":\"0\"", StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await host.Send(Delivery("string-balances"), "string-balances")).StatusCode);
        Assert.Equal("PAID", await host.State());
    }

    private sealed class SampleHost : IAsyncDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "lunixi-test-" + Guid.NewGuid());
        private WebApplication app = null!;
        public int GetCalls { get; private set; }
        public string PaymentReply { get; set; } = Evidence();
        public string CreateReply { get; init; } = "{\"data\":{\"paymentId\":\"pay-demo-1\",\"token\":\"synthetic-checkout-token\"}}";
        public HttpClient Http { get; private set; } = null!;
        public async Task Start()
        {
            Directory.CreateDirectory(directory);
            app = SampleApplication.Build([], builder =>
            {
                builder.WebHost.UseTestServer();
                builder.Services.AddSingleton(new SampleSettings { ClientOptions = TestOptions.Create(), DatabasePath = Path.Combine(directory, "sample.db"), ApiToken = ApiToken, WebhookSecret = Secret, MerchantId = Merchant, CallbackUrl = new("https://merchant.example/payments/return") });
                builder.Services.AddSingleton(new LunixiClient(TestOptions.Create(), new FakeHandler((request, _) =>
                {
                    if (request.RequestUri!.AbsolutePath.EndsWith("/auth/token", StringComparison.Ordinal)) return Task.FromResult(FakeHandler.Json("{\"access_token\":\"token\"}"));
                    if (request.Method == HttpMethod.Get)
                    {
                        Assert.Equal("/api/v1/payments/" + PaymentId, request.RequestUri.AbsolutePath); Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                        GetCalls++; return Task.FromResult(FakeHandler.Json(PaymentReply));
                    }
                    Assert.Equal("/api/v1/payments/intents", request.RequestUri.AbsolutePath); return Task.FromResult(FakeHandler.Json(CreateReply));
                })));
            });
            await app.StartAsync(); Http = app.GetTestClient(); Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiToken);
        }
        public Task<HttpResponseMessage> Checkout() => Http.PostAsync("/checkout/DEMO-1001", null);
        public async Task<string> State() { using var json = JsonDocument.Parse(await Http.GetStringAsync("/orders/DEMO-1001")); return json.RootElement.GetProperty("state").GetString()!; }
        public Task<HttpResponseMessage> Send(string raw, string eventId)
        {
            var time = DateTimeOffset.UtcNow.ToString("O");
            var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(eventId + "." + time + "." + raw)));
            var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/lunixi") { Content = new StringContent(raw, Encoding.UTF8, "application/json") };
            request.Headers.Add("x-lunixi-signature", "v2=" + signature); request.Headers.Add("x-lunixi-event-id", eventId); request.Headers.Add("x-lunixi-signature-timestamp", time);
            return Http.SendAsync(request);
        }
        public async ValueTask DisposeAsync()
        {
            Http?.Dispose(); if (app is not null) { await app.StopAsync(); await app.DisposeAsync(); }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(directory, true);
        }
    }
}
