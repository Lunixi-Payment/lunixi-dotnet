using System.Security.Cryptography;
using System.Text;
using Lunixi;
using Lunixi.AspNetCoreSample;

var app = SampleApplication.Build(args);
await app.RunAsync();

namespace Lunixi.AspNetCoreSample
{
    /// <summary>A small server-side integration with durable SQLite attempts and webhook inbox transactions.</summary>
    public static class SampleApplication
    {
        /// <summary>Builds the example host; the optional callback is used by offline integration tests.</summary>
        public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
        {
            var builder = WebApplication.CreateBuilder(args);
            // Do not enable HTTP body/header logging: checkout tokens, signatures and buyer data are sensitive.
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1024 * 1024);
            builder.Services.AddSingleton(provider => SampleSettings.From(provider.GetRequiredService<IConfiguration>()));
            builder.Services.AddSingleton(provider => new LunixiClient(provider.GetRequiredService<SampleSettings>().ClientOptions));
            builder.Services.AddSingleton(provider => new SampleStore(provider.GetRequiredService<SampleSettings>().DatabasePath));
            builder.Services.AddSingleton(provider => new WebhookVerifier(provider.GetRequiredService<SampleSettings>().WebhookSecret));
            configure?.Invoke(builder);
            var app = builder.Build();
            // Resolve configuration and schema at startup; missing credentials never create a pretend working checkout.
            _ = app.Services.GetRequiredService<SampleSettings>();
            app.Services.GetRequiredService<SampleStore>().Initialize();
            app.MapGet("/health", () => Results.Ok(new { status = "ready" }));
            app.MapPost("/checkout/{orderId}/{presentation?}", async (string orderId, string? presentation, HttpContext context, SampleSettings settings, SampleStore store, LunixiClient client) =>
            {
                if (!Authorized(context, settings.ApiToken)) return Results.Unauthorized();
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                if (presentation is not (null or "form")) return Results.NotFound();
                var mode = context.Request.Query["mode"].ToString();
                if (presentation == "form" && mode is not ("" or "embedded" or "hosted")) return Results.BadRequest(new { code = "invalid_checkout_mode" });
                // This authenticated backend endpoint accepts an order reference only. Price comes from our database.
                var order = store.GetOrCreateAttempt(orderId);
                if (order is null) return Results.NotFound();
                if (order.State != "UNPAID") return Results.Conflict(new { code = "order_has_settlement" });
                // Gateway idempotency expires after 24h: an older attempt requires reconciliation, never blind recreation.
                if (DateTimeOffset.UtcNow - order.CreatedAt > TimeSpan.FromHours(23))
                    return Results.Conflict(new { code = "reconciliation_required" });
                try
                {
                    var result = await client.Payments.CreateIntentAsync(new(order.Amount, order.Currency, order.OrderId)
                    {
                        CallbackUrl = settings.CallbackUrl,
                        BasketItems = [new("DEMO-SKU", "Demo product", order.Amount)],
                    }, order.IdempotencyKey, context.RequestAborted);
                    var id = SampleStore.Text(result.Data, "paymentId");
                    var token = SampleStore.Text(result.Data, "token");
                    if (!SampleStore.ValidIdentifier(id) || token is null || token.Length is < 1 or > 16384 || token.Any(c => c < 0x21 || c > 0x7e) ||
                        result.Data.TryGetProperty("intentId", out _) && SampleStore.Text(result.Data, "intentId") != id)
                        return Results.Json(new { code = "invalid_gateway_response" }, statusCode: 502);
                    store.BindPayment(orderId, id!);
                    if (presentation == "form")
                    {
                        if (mode == "hosted") return Results.Redirect(CheckoutPresentation.HostedUrl(SampleStore.Text(result.Data, "paymentPageUrl")));
                        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
                        return Results.Content(CheckoutPresentation.EmbeddedHtml(token, nonce), "text/html; charset=utf-8");
                    }
                    // Preserve the server-to-server JSON contract; browsers never receive the backend API token.
                    return Results.Json(result.Envelope, statusCode: result.StatusCode);
                }
                catch (LunixiApiException error) { return Results.Json(new { code = error.Code, requestId = error.RequestId }, statusCode: error.StatusCode); }
                catch (LunixiTransportException) { return Results.Json(new { code = "outcome_unknown_reuse_original_attempt" }, statusCode: 503); }
                catch (SampleReconciliationException) { return Results.Json(new { code = "reconciliation_required" }, statusCode: 503); }
                catch (SamplePresentationException) { return Results.Json(new { code = "checkout_presentation_requires_reconciliation" }, statusCode: 502); }
            });
            app.MapGet("/orders/{orderId}", (string orderId, HttpContext context, SampleSettings settings, SampleStore store) =>
            {
                if (!Authorized(context, settings.ApiToken)) return Results.Unauthorized();
                context.Response.Headers.CacheControl = "no-store";
                var state = store.GetState(orderId);
                return state is null ? Results.NotFound() : Results.Ok(new { orderId, state });
            });
            app.MapPost("/webhooks/lunixi", async (HttpContext context, WebhookVerifier verifier, SampleStore store, SampleSettings settings, LunixiClient client) =>
            {
                try
                {
                    using var body = new MemoryStream(); var buffer = new byte[8192]; int read;
                    while ((read = await context.Request.Body.ReadAsync(buffer, context.RequestAborted)) != 0)
                    {
                        if (body.Length + read > 1024 * 1024) return Results.StatusCode(413);
                        body.Write(buffer, 0, read);
                    }
                    // Preserve each header value separately; duplicate critical headers must fail verification.
                    var headers = context.Request.Headers.SelectMany(h => h.Value.Select(value => new KeyValuePair<string, string>(h.Key, value ?? "")));
                    var webhook = verifier.Verify(body.ToArray(), headers);
                    if (store.IsWebhookComplete(webhook)) return Results.Ok();
                    var known = store.FindKnownPayment(webhook, settings.MerchantId);
                    // Query only a payment already bound to this server's order. Event/browser status is never authoritative.
                    var payment = await client.Payments.GetAsync(known.PaymentId, context.RequestAborted);
                    store.ApplyWebhook(webhook, known, payment, settings.MerchantId);
                    return Results.Ok();
                }
                catch (LunixiWebhookException) { return Results.BadRequest(new { code = "invalid_webhook" }); }
                catch (SampleReconciliationException) { return Results.Json(new { code = "reconciliation_required" }, statusCode: 503); }
                catch (Exception error) when (error is LunixiApiException or LunixiTransportException or OperationCanceledException)
                { return Results.Json(new { code = "reconciliation_required" }, statusCode: 503); }
                catch (Microsoft.Data.Sqlite.SqliteException) { return Results.StatusCode(503); }
            });
            // A browser return is informational. It never changes the order state.
            app.MapMethods("/payments/return", ["GET", "POST"], () => Results.Ok(new { message = "Payment is being verified. Check the authenticated order endpoint." }));
            return app;
        }

        private static bool Authorized(HttpContext context, string expected)
        {
            var value = context.Request.Headers.Authorization.ToString();
            if (!value.StartsWith("Bearer ", StringComparison.Ordinal) || value.Length > 4096) return false;
            return CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(value[7..])), SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
        }
    }

    /// <summary>Server-side settings loaded from environment variables or a secret provider.</summary>
    public sealed class SampleSettings
    {
        /// <summary>SDK options kept on the server.</summary>
        public required LunixiOptions ClientOptions { get; init; }
        /// <summary>Webhook endpoint secret.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string WebhookSecret { get; init; } = string.Empty;
        /// <summary>Demo backend API access token; replace with real merchant/customer authorization.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string ApiToken { get; init; } = string.Empty;
        /// <summary>Persistent database file, shared by this example's handlers.</summary>
        public required string DatabasePath { get; init; }
        /// <summary>The merchant identity expected in authenticated gateway payment records.</summary>
        public required string MerchantId { get; init; }
        /// <summary>HTTPS browser return URL, not a payment authority.</summary>
        public required Uri CallbackUrl { get; init; }
        internal static SampleSettings From(IConfiguration configuration)
        {
            string Required(string key) => !string.IsNullOrWhiteSpace(configuration[key]) ? configuration[key]! : throw new InvalidOperationException($"Required server setting {key} is missing.");
            var token = Required("DEMO_API_TOKEN");
            if (token.Length < 32) throw new InvalidOperationException("DEMO_API_TOKEN must contain at least 32 characters.");
            var callback = new Uri(Required("MERCHANT_CALLBACK_URL"));
            if (callback.Scheme != "https" || callback.UserInfo.Length != 0 || callback.Fragment.Length != 0) throw new InvalidOperationException("MERCHANT_CALLBACK_URL must be HTTPS.");
            return new()
            {
                ClientOptions = new() { BaseUri = new Uri(Required("LUNIXI_BASE_URL")), KeyId = Required("LUNIXI_KEY_ID"), PrivateKeyPem = Required("LUNIXI_PRIVATE_KEY_PEM") },
                ApiToken = token,
                WebhookSecret = Required("LUNIXI_WEBHOOK_SECRET"),
                DatabasePath = Required("LUNIXI_DATABASE_PATH"),
                MerchantId = Required("LUNIXI_MERCHANT_ID"),
                CallbackUrl = callback,
            };
        }
        /// <summary>Returns no configuration values.</summary>
        public override string ToString() => "SampleSettings { Credentials = [REDACTED] }";
    }
}
