# .NET / C# SDK

Version **1.0.1** uses canonical minor-unit strings. Follow the SDK money-string migration and install after the version is available on NuGet.

The source patch adds a concrete merchant-site presentation example. **Embedded checkout is the default**: load only the official Lunixi bundle and mount the existing form; methods and multi-card choices stay in the Lunixi panel. Optional hosted mode uses the backend `paymentPageUrl` after exact HTTPS `pay.lunixi.com/payment/` validation. Its token may differ from the embedded token. Missing URLs keep embedded available and hosted on hold/reconciliation with the original attempt/key. Never derive a URL from the browser token. Authorize the customer/order and native browser CSRF before rendering, send `Cache-Control: private, no-store`, preserve the merchant CSP nonce and keep tokens/HTML/redirect URLs out of logs. Generic response data preserves `paymentPageUrl`; no new typed product parity is claimed.

`Lunixi.Sdk` 1.0.1 provides Ed25519 signing, token caching, checkout intents, payment operations, split DTO validation, fraud clients and raw-body webhook verification. It supports .NET 8 or newer and works with ASP.NET Core, background workers and other server applications.

## Installation

Install the exact version after it is available on NuGet:

```bash
dotnet add package Lunixi.Sdk --version 1.0.1 --source https://api.nuget.org/v3/index.json
```

Keep your normal NuGet feed enabled for dependencies, or reference the SDK project directly. NSec/libsodium supplies Ed25519 with native assets for supported server runtimes. Browser/WASM is not supported. The sample and test host require the .NET 8 runtime.

## Client and merchant-site checkout

```csharp
using Lunixi;

var lunixi = new LunixiClient(new LunixiOptions
{
    BaseUri = new Uri("https://api-gateway.lunixi.com"),
    KeyId = Environment.GetEnvironmentVariable("LUNIXI_KEY_ID")!,
    PrivateKeyPem = Environment.GetEnvironmentVariable("LUNIXI_PRIVATE_KEY_PEM")!,
});

var request = new CreateIntentRequest("10050", "TRY", "ORDER-1001")
{
    CallbackUrl = new Uri("https://merchant.example/payments/return"),
    BasketItems = [new BasketItem("SKU-1", "Demo product", "10050")],
};
// Load this key from a persisted checkout attempt; reuse it after network failures.
var response = await lunixi.Payments.CreateIntentAsync(request, persistedAttemptKey);
var paymentId = response.Data.GetProperty("paymentId").GetString();
var token = response.Data.GetProperty("token").GetString();
```

Register one client per application and dispose it during shutdown. The key determines TEST/LIVE. Register the public Ed25519 key in the dashboard; keep the PKCS#8 PEM private key on your server. `Ed25519Signer.GenerateKeyPair()` generates both. → [Authentication](/getting-started/authentication)

Amounts are positive canonical minor-unit strings: `"10050"` = 100.50 TRY. Read prices from your order database. Basket `Price` is the complete line total; line totals must equal `Amount`. Legacy top-level amount fields are rejected. Provide buyer/billing/shipping JSON from trusted server records when your checkout requires them.

Pass the token to the existing [Lunixi Checkout Form](/payments/checkout-form). The form handles enabled methods and buyer interactions. The SDK does not recreate card fields or enable raw-card charging by default. Browser returns are informational; fulfillment depends on verified webhooks and authenticated status reads.

| Setting | Default | Behavior |
| --- | --- | --- |
| `Timeout` | 30 seconds | Per attempt, including response reads |
| `MaxRetries` | 2 | Network/5xx retries for reads and writes carrying a stable key |
| `RetryDelay` | 100 ms | Exponential delay capped at two seconds |
| `MaxResponseBytes` | 1 MiB | Bounded response body |
| `TimeProvider` | System UTC | Injectable signing/token-expiry clock |

Token acquisition is signed and bodyless. Tokens use a 30-second expiry margin and one refresh after 401. The default transport never follows redirects or leaves its HTTPS origin. A trusted `HttpMessageHandler` can be injected for testing; it must preserve the credential boundary and avoid secret logging.

## Operations and coverage

```csharp
var payment = await lunixi.Payments.GetAsync(paymentId);
var captured = await lunixi.Payments.CaptureAsync(paymentId, persistedCaptureKey, amount: "10050");
var refunded = await lunixi.Payments.RefundAsync(paymentId, persistedRefundKey, amount: "1000");
var voided = await lunixi.Payments.VoidAsync(paymentId, persistedVoidKey);
var methods = await lunixi.Payments.ListMethodsAsync();
```

Set `CreateIntentRequest.Preauth = true` for authorization followed by capture/void. `LunixiResponse` preserves `StatusCode`, full `Envelope`, `Data` and `RequestId`, including pagination metadata. A pending status or HTTP 202 is not proof of payment.

| Surface | .NET 1.0.1 |
| --- | --- |
| Hosted intents, payment status/list/capture/refund/void/methods | `lunixi.Payments` |
| Split create/status/list/refund and customer actions | `SplitPaymentRequest`, `lunixi.Payments` |
| Fraud evaluation, transaction event ingest, decision logs | `lunixi.Fraud` |
| Webhook verification and durable replay coordination | `WebhookVerifier`, `IWebhookReplayStore`, `WebhookProcessor` |
| Payment links, wallet, bank-connect, sanctions, reference, insurance and other public JSON products | Explicit `SendAsync` calls; typed parity is not claimed |

`SplitPaymentRequest` validates documented JSON DTOs, exact totals, 1–5 legs, distinct card/provider references, method field shapes, installment quotes and 3DS callbacks. Use stored-card tokens. Continue with the same body/key; handle customer actions using `ConfirmActionAsync` and `ResendActionAsync`. Documented in-progress HTTP 400 responses are returned; failed/reconciliation states remain errors.

> 
  Split source support does not assert endpoint deployment or merchant activation. Availability depends on gateway rollout and capabilities. Pending and reconciliation states never authorize fulfillment.


```csharp
var banks = await lunixi.SendAsync(HttpMethod.Get, "/api/v1/bank-connect/banks?limit=25");
var decision = await lunixi.Fraud.EvaluateAsync(new FraudEvaluateRequest(new
{
    transactionRef = "ORDER-1001", subjectType = "CUSTOMER", subjectId = "customer-1",
}));
```

Generic transport signs by default and accepts explicit public JSON routes under `/api/v1/` on the configured origin. Follow the product's documented DTO, scopes and operation-key requirements. Binary PDF/images, multipart uploads and browser checkout-session APIs are outside this JSON transport. → [REST without an SDK](/sdks/rest)

## Raw-body webhooks and durable idempotency

```csharp
var verifier = new WebhookVerifier(webhookSecret);
var webhook = verifier.Verify(rawRequestBytes, requestHeaders);
// One DB transaction: dedupe webhook.Id, validate payment/order/amount/currency, update order.
```

Read exact bytes before JSON middleware. The v2 verifier checks HMAC over `eventId.timestamp.rawBody`, constant-time rotated-signature comparison, a ±300-second window, envelope/header event-id and event-type binding, and duplicate critical headers. Use a test clock rather than disabling freshness. → [Webhooks](/getting-started/webhooks)

Your domain must match payment/order references, amount, currency, merchant context and rail semantics. Authorization is not capture; split legs, insurance state and promissory-note issuance need their own policy. Implement `IWebhookReplayStore` with durable atomic reservations, body-hash conflicts, lease expiry and fencing tokens when using `WebhookProcessor`. Completed duplicates are skipped; busy leases require a retriable failure. Domain writes must also be idempotent. Prefer the same transaction for inbox completion and order updates. Retain event IDs at least seven days and keep domain records for older manual replays.

## ASP.NET Core sample and errors

`samples/AspNetCore` contains a runnable application with real SQLite persistence, a server-priced order, authenticated checkout, stable attempt keys and an atomic webhook inbox/order transaction. Configure its server secrets as documented in the sample README, then run:

```bash
dotnet run --project samples/AspNetCore/AspNetCore.csproj --urls http://127.0.0.1:5080
```

Set `LUNIXI_MERCHANT_ID` together with the server credentials. Webhooks locate only a locally bound payment, then an authenticated GET must match merchant/payment/order identity, currency, exact base amount and full capture/refund balances before the database transaction commits.

Use HTTPS at deployment. Replace the demo backend token with real order authorization. The demo fails closed for split/senet/adjusted-amount captures; add your installment, insurance, partial capture, refund, dispute and reconciliation policy before production deployment. Browser returns cannot mark an order paid.

Catch `LunixiValidationException` (400/422), `LunixiRateLimitException` (429), `LunixiApiException` and `LunixiTransportException`. Gateway errors expose `Code`, `RequestId` and `RetryAfterSeconds`; messages omit upstream body values and secrets. Do not log complete envelopes. Rate limits are not automatically retried.

A timed-out or canceled write can have an unknown final outcome. Reconcile and reuse the original persisted key. Normal gateway idempotency retention is 24 hours; do not blindly resend after expiry. The sample requires reconciliation after 23 hours. Retries preserve the body/key and use a new signature nonce.

Run `dotnet test tests/Lunixi.Sdk.Tests/Lunixi.Sdk.Tests.csproj -c Release`. Tests use offline HTTP handlers, Node.js signing/canonical-JSON fixtures, shared webhook vectors, a real ASP.NET Core TestServer and persistent SQLite. They do not make live charges. → [Testing](/getting-started/testing), [Error handling](/getting-started/errors)

## Merchant-page integration example

The runnable ASP.NET Core sample now exposes authenticated `POST /checkout/{orderId}/form` for embedded HTML by default; `?mode=hosted` uses the validated backend URL. The existing `/checkout/{orderId}` JSON contract is retained. `Lunixi.AspNetCoreSample.CheckoutPresentation` contains the renderer and URL validator. `DEMO_API_TOKEN` remains strictly server-to-server: never send it to browser JavaScript. Your merchant checkout controller must authenticate the customer, verify order ownership and native CSRF before proxying/rendering this response. This sample does not implement customer login or a production tenant authorization model.
