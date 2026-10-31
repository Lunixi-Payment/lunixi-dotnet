using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Lunixi.AspNetCoreSample;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Lunixi.Sdk.Tests;

public sealed class AspNetCoreTests
{
    private const string ApiToken = "synthetic-backend-token-at-least-32-characters";
    private const string WebhookSecret = "synthetic-webhook-secret-at-least-32";

    [Fact]
    public async Task RealHostUsesServerAmountsAndDurableAttemptAndNeverTrustsBrowserReturn()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lunixi-test-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        var keys = new List<string>(); var writes = new List<string>();
        await using var app = SampleApplication.Build([], builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(new SampleSettings
            {
                ClientOptions = TestOptions.Create(),
                DatabasePath = Path.Combine(directory, "sample.db"),
                ApiToken = ApiToken,
                WebhookSecret = WebhookSecret,
                MerchantId = "merchant-demo-1",
                CallbackUrl = new("https://merchant.example/payments/return"),
            });
            builder.Services.AddSingleton(new LunixiClient(TestOptions.Create(), new FakeHandler(async (request, ct) =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/auth/token")) return FakeHandler.Json("{\"access_token\":\"token\"}");
                if (request.Method == HttpMethod.Get)
                    return FakeHandler.Json("{\"data\":{\"id\":\"pay-demo-1\",\"merchantId\":\"merchant-demo-1\",\"orderId\":\"DEMO-1001\",\"currency\":\"TRY\",\"amount\":10050,\"status\":\"CAPTURED\",\"details\":{\"capturedAmount\":10050,\"refundedAmount\":0}}}");
                keys.Add(request.Headers.GetValues("Idempotency-Key").Single()); writes.Add(await request.Content!.ReadAsStringAsync(ct));
                return FakeHandler.Json("{\"data\":{\"paymentId\":\"pay-demo-1\",\"token\":\"synthetic-checkout-token\"}}");
            })));
        });
        try
        {
            await app.StartAsync(); var http = app.GetTestClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsync("/checkout/DEMO-1001", null)).StatusCode);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiToken);
            Assert.Equal(HttpStatusCode.OK, (await http.PostAsync("/checkout/DEMO-1001", new StringContent("{\"amount\":1}"))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await http.PostAsync("/checkout/DEMO-1001", null)).StatusCode);
            Assert.Equal(2, writes.Count); Assert.Single(keys.Distinct()); Assert.All(writes, body => Assert.Contains("\"amount\":10050", body));
            await http.GetAsync("/payments/return?success=true&status=CAPTURED&orderId=DEMO-1001");
            Assert.Contains("UNPAID", await http.GetStringAsync("/orders/DEMO-1001"));

            var invalidAmount = "{\"id\":\"event-amount\",\"type\":\"payment.captured\",\"data\":{\"paymentId\":\"pay-demo-1\",\"orderId\":\"DEMO-1001\",\"capturedAmountMinor\":\"1\",\"currency\":\"TRY\"}}";
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await SendWebhook(http, invalidAmount, "event-amount")).StatusCode);
            Assert.Contains("UNPAID", await http.GetStringAsync("/orders/DEMO-1001"));
            var valid = invalidAmount.Replace("event-amount", "event-valid").Replace("\"capturedAmountMinor\":\"1\"", "\"capturedAmountMinor\":\"10050\"");
            Assert.Equal(HttpStatusCode.OK, (await SendWebhook(http, valid, "event-valid")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await SendWebhook(http, valid, "event-valid")).StatusCode);
            Assert.Contains("PAID", await http.GetStringAsync("/orders/DEMO-1001"));
            Assert.Equal(HttpStatusCode.BadRequest, (await SendWebhook(http, valid + " ", "event-valid")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await http.PostAsync("/checkout/DEMO-1001", null)).StatusCode);
            // Reopen the real SQLite store to prove the inbox/order survive application object lifetimes.
            var reopened = new SampleStore(Path.Combine(directory, "sample.db")); reopened.Initialize(); Assert.Equal("PAID", reopened.GetState("DEMO-1001"));
            Assert.Equal(keys[0], reopened.GetOrCreateAttempt("DEMO-1001")!.IdempotencyKey);
        }
        finally { await app.StopAsync(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }
    }

    private static Task<HttpResponseMessage> SendWebhook(HttpClient http, string raw, string eventId)
    {
        var time = DateTimeOffset.UtcNow.ToString("O");
        var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(WebhookSecret), Encoding.UTF8.GetBytes(eventId + "." + time + "." + raw)));
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/lunixi") { Content = new StringContent(raw, Encoding.UTF8, "application/json") };
        request.Headers.Add("x-lunixi-signature", "v2=" + signature); request.Headers.Add("x-lunixi-event-id", eventId); request.Headers.Add("x-lunixi-signature-timestamp", time);
        return http.SendAsync(request);
    }
}
