using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Lunixi.AspNetCoreSample;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Lunixi.Sdk.Tests;

public sealed class CheckoutPresentationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("http://pay.lunixi.com/payment/backend-hosted-token-12345")]
    [InlineData("https://evil.example/payment/backend-hosted-token-12345")]
    [InlineData("https://user@pay.lunixi.com/payment/backend-hosted-token-12345")]
    [InlineData("https://pay.lunixi.com:443/payment/backend-hosted-token-12345")]
    [InlineData("https://pay.lunixi.com/payment/backend-hosted-token-12345?x=1")]
    [InlineData("https://pay.lunixi.com/payment/backend-hosted-token-12345#x")]
    [InlineData("https://pay.lunixi.com/payment/backend-hosted-token-12345\n")]
    [InlineData("https://pay.lunixi.com/payment/short")]
    public void HostedNeverBuildsUnsafeOrMissingBackendUrl(string? value)
        => Assert.Throws<SamplePresentationException>(() => CheckoutPresentation.HostedUrl(value));

    [Fact]
    public void EmbeddedRendererUsesExistingFormAndEscapesTokenAndNonce()
    {
        const string token = "</script><svg/onload=unexpected()>";
        var html = CheckoutPresentation.EmbeddedHtml(token, "server_nonce_123456789");
        Assert.DoesNotContain(token, html); Assert.Contains("https://cdn.lunixi.com/lunixi-checkout.min.js", html);
        Assert.Contains("checkout.mount('#lunixi-checkout-form')", html);
        Assert.Throws<SamplePresentationException>(() => CheckoutPresentation.EmbeddedHtml("token", "bad\"nonce"));
    }

    [Fact]
    public async Task NativeHostDefaultsToEmbeddedAndOptionalRedirectUsesBackendUrlWithoutNewKey()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lunixi-presentation-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        const string apiToken = "synthetic-backend-token-at-least-32-characters";
        const string backendUrl = "https://pay.lunixi.com/payment/separate-hosted-token-123456";
        string? url = backendUrl; var keys = new List<string>();
        await using var app = SampleApplication.Build([], builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(new SampleSettings { ClientOptions = TestOptions.Create(), DatabasePath = Path.Combine(directory, "sample.db"), ApiToken = apiToken, WebhookSecret = "synthetic-webhook-secret-at-least-32", MerchantId = "merchant-demo-1", CallbackUrl = new("https://merchant.example/payments/return") });
            builder.Services.AddSingleton(new LunixiClient(TestOptions.Create(), new FakeHandler((request, _) =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/auth/token")) return Task.FromResult(FakeHandler.Json("{\"access_token\":\"synthetic-token\"}"));
                keys.Add(request.Headers.GetValues("Idempotency-Key").Single());
                return Task.FromResult(FakeHandler.Json(JsonSerializer.Serialize(new { data = new { paymentId = "pay-demo-1", token = "different-browser-token", paymentPageUrl = url } })));
            })));
        });
        try
        {
            await app.StartAsync(); var http = app.GetTestClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsync("/checkout/DEMO-1001/form", null)).StatusCode);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
            var form = await http.PostAsync("/checkout/DEMO-1001/form", null);
            Assert.Equal(HttpStatusCode.OK, form.StatusCode); Assert.Equal("text/html", form.Content.Headers.ContentType!.MediaType);
            Assert.True(form.Headers.CacheControl!.NoStore); Assert.Contains("different-browser-token", await form.Content.ReadAsStringAsync());
            var redirect = await http.PostAsync("/checkout/DEMO-1001/form?mode=hosted", null);
            Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode); Assert.Equal(backendUrl, redirect.Headers.Location!.AbsoluteUri);
            url = null;
            Assert.Equal(HttpStatusCode.OK, (await http.PostAsync("/checkout/DEMO-1001/form", null)).StatusCode);
            Assert.Equal(HttpStatusCode.BadGateway, (await http.PostAsync("/checkout/DEMO-1001/form?mode=hosted", null)).StatusCode);
            Assert.Single(keys.Distinct());
            var before = keys.Count;
            Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsync("/checkout/DEMO-1001/form?mode=other", null)).StatusCode);
            Assert.Equal(before, keys.Count);
        }
        finally { await app.StopAsync(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }
    }
}
