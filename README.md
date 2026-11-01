<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset=".github/assets/lunixi-logo-dark.svg">
    <img src=".github/assets/lunixi-logo.svg" alt="Lunixi" width="240">
  </picture>
</p>

# Lunixi .NET SDK

**Version 1.0.1 uses canonical minor-unit strings for money.** This is a breaking change from 0.x; update request and response handling together. See [migration](MIGRATION-1.0.md).

Historical release record: **[Lunixi.Sdk 0.1.1 is published on NuGet](https://www.nuget.org/packages/Lunixi.Sdk/0.1.1)**. The downloaded public package, source commit and NuGet catalog SHA-512 were verified on 31 October 2026.

Version 1.0.1 includes a concrete merchant-site presentation example. **Embedded checkout is the default**: load only the official Lunixi bundle and mount the existing form; methods and multi-card choices stay in the Lunixi panel. Optional hosted mode uses the backend `paymentPageUrl` after exact HTTPS `pay.lunixi.com/payment/` validation. Its token may differ from the embedded token. Missing URLs keep embedded available and hosted on hold/reconciliation with the original attempt/key. Never derive a URL from the browser token. Authorize the customer/order and native browser CSRF before rendering, send `Cache-Control: private, no-store`, preserve the merchant CSP nonce and keep tokens/HTML/redirect URLs out of logs. Generic response data preserves `paymentPageUrl`; no new typed product parity is claimed.

Server-side Lunixi integration for .NET 8 or newer: Ed25519 authentication, checkout-form intents, payment operations, split-payment DTOs, fraud and signed webhooks. Works with ASP.NET Core, worker services and other .NET applications. Version **1.0.1**, MIT licensed.

[Türkçe kullanım kılavuzu](docs/guide.tr.md) · [Documentation](https://docs.lunixi.com/en/sdks/dotnet) · [Lunixi](https://lunixi.com)

## Installation

Install the exact package after version 1.0.1 is available on NuGet:

```bash
dotnet add package Lunixi.Sdk --version 1.0.1 --source https://api.nuget.org/v3/index.json
```

For source development or a local package build, this repository also produces `Lunixi.Sdk.1.0.1.nupkg`:

```bash
dotnet pack src/Lunixi.Sdk/Lunixi.Sdk.csproj -c Release -o artifacts
dotnet add /path/to/YourApp.csproj package Lunixi.Sdk --version 1.0.1 --source /absolute/path/to/this/repo/artifacts
```

Alternatively reference `src/Lunixi.Sdk/Lunixi.Sdk.csproj` directly. Restore the generated package's dependencies from your normal NuGet feed. [NSec.Cryptography 25.4.0](https://www.nuget.org/packages/NSec.Cryptography/25.4.0) supplies Ed25519 via libsodium and supports .NET 8; compatible native runtime assets must be present when deploying. Browser/WASM is not supported.

## Create one client per application

```csharp
using Lunixi;

using var lunixi = new LunixiClient(new LunixiOptions
{
    BaseUri = new Uri("https://api-gateway.lunixi.com"),
    KeyId = Environment.GetEnvironmentVariable("LUNIXI_KEY_ID")!,
    PrivateKeyPem = Environment.GetEnvironmentVariable("LUNIXI_PRIVATE_KEY_PEM")!,
});
```

The merchant API key determines TEST/LIVE. Register the public Ed25519 key in the dashboard and keep the PKCS#8 PEM private key on your server. `Ed25519Signer.GenerateKeyPair()` produces both; register only `PublicKeyPem`. The SDK never logs credentials or bodies. Do not serialize gateway envelopes or customer DTOs into application logs.

| Option | Default | Behavior |
| --- | --- | --- |
| `BaseUri`, `KeyId`, `PrivateKeyPem` | Required | HTTPS origin, merchant SECRET key identifier and Ed25519 PKCS#8 PEM |
| `Timeout` | 30 seconds | Per HTTP attempt, including authentication and response body |
| `MaxRetries` | 2 | Network/5xx retries for reads and writes with an explicit stable key |
| `RetryDelay` | 100 milliseconds | Exponential delay capped at two seconds |
| `MaxResponseBytes` | 1 MiB | Bounded response reads |
| `TimeProvider` | System UTC | Injectable clock for signing/token cache |

The client sends signed **bodyless** token requests, caches tokens with a 30-second safety margin, and refreshes once after HTTP 401. Redirects are never followed. Requests cannot leave the configured origin. Supply a custom trusted `HttpMessageHandler` for instrumentation or tests; SDK validation rejects known handlers with automatic redirects enabled.

## Checkout: embedded by default, optional hosted mode

```csharp
var request = new CreateIntentRequest("10050", "TRY", "ORDER-1001")
{
    CallbackUrl = new Uri("https://merchant.example/payments/return"),
    BasketItems = [new BasketItem("SKU-1", "Demo product", "10050")],
};

// Persist this key with the checkout attempt BEFORE the first gateway request.
var result = await lunixi.Payments.CreateIntentAsync(request, persistedAttemptKey);
var paymentId = result.Data.GetProperty("paymentId").GetString();
var checkoutToken = result.Data.GetProperty("token").GetString();
```

Use your server's order amount. `10050` means 100.50 TRY. All typed payment amounts are positive canonical minor-unit strings, capped at the signed 64-bit fiat limit. Basket item `Price` is the entire line total, and line totals must equal `Amount`. Root `price` / `paidPrice` fields are rejected, including through generic transport; basket `price` remains valid. Supply buyer/billing/shipping JSON from trusted server records when your flow requires them. No raw-card charge API is enabled by default.

Pass the token to the existing Lunixi Checkout Form integration according to the [checkout documentation](https://docs.lunixi.com/en/payments/checkout-form). The SDK does not recreate card fields or the shared form. Browser callbacks are informational; use verified webhooks and authenticated status reads to decide fulfillment.

## Payment operations

```csharp
var payment = await lunixi.Payments.GetAsync(paymentId);
var capture = await lunixi.Payments.CaptureAsync(paymentId, persistedCaptureKey, amount: "10050");
var refund = await lunixi.Payments.RefundAsync(paymentId, persistedRefundKey, amount: "1000", reason: "Customer request");
var released = await lunixi.Payments.VoidAsync(paymentId, persistedVoidKey);
var methods = await lunixi.Payments.ListMethodsAsync();
```

`LunixiResponse.StatusCode`, `.Envelope`, `.Data` and `.RequestId` preserve pending states and pagination metadata. A 202 or a pending status is not proof of payment. Preauthorization uses `CreateIntentRequest.Preauth = true`, then capture or void.

Split APIs: `CreateSplitAsync`, `GetSplitAsync`, `ListSplitsAsync`, `RefundSplitAsync`. `SplitPaymentRequest` accepts the documented JSON DTO, validates 1–5 legs, exact `totalAmount`, distinct stored-card/provider references, allowed method fields, installment quotes and required 3DS callback. Use stored-card tokens; never raw card numbers. Action APIs are `ConfirmActionAsync` and `ResendActionAsync`. Documented in-progress HTTP 400 split responses are returned as `LunixiResponse`; failed/reconciliation states remain exceptions. **Split endpoint availability depends on gateway rollout and merchant capabilities. Source support does not assert live availability.** The same split request and operation key must be reused to continue the saga.

## Coverage and explicit transport

| Surface | .NET 1.0.1 |
| --- | --- |
| Hosted intents, payment status/list, capture/refund/void, methods | Typed clients |
| Split creation/status/list/refund and payment actions | Validated JSON DTO and typed client methods; rollout dependent |
| Fraud decisions, transaction event ingest, decision logs | `lunixi.Fraud` |
| Webhooks | `WebhookVerifier`, `IWebhookReplayStore`, `WebhookProcessor` |
| Payment links, wallet, bank-connect, sanctions, reference, insurance and other public JSON APIs | `SendAsync` with explicit documented route/body; no typed parity claim |
| Binary image/PDF downloads, multipart uploads, browser checkout-session APIs | Not implemented by this JSON transport |

```csharp
var banks = await lunixi.SendAsync(HttpMethod.Get, "/api/v1/bank-connect/banks?limit=25");
var decision = await lunixi.Fraud.EvaluateAsync(new FraudEvaluateRequest(new
{
    transactionRef = "ORDER-1001", subjectType = "CUSTOMER", subjectId = "customer-1",
}));
```

Generic requests are signed by default and restricted to `/api/v1/` on the configured HTTPS origin. Consult the product's public DTO and scope documentation. Product validation remains the caller's responsibility; supply its required stable idempotency key explicitly. Generic transport never grants an unavailable product or an API scope.

## Verify exact webhook bytes

```csharp
var verifier = new WebhookVerifier(webhookSecret);
LunixiWebhook webhook = verifier.Verify(rawRequestBytes, requestHeaders);
// Commit event-id dedupe and validated order changes in the SAME database transaction.
```

The v2 verifier checks HMAC-SHA256 of `eventId.timestamp.rawBody`, constant-time rotated-signature comparisons, timestamp freshness (±300 seconds), envelope/header event-id binding and event-type binding. It rejects missing/duplicated critical headers and legacy-only signatures. `TimeProvider` permits deterministic tests without disabling freshness. `Data` remains untrusted for your business rules until payment/order references, amount, currency, merchant context and event semantics are matched.

Implement `IWebhookReplayStore` with durable atomic acquire/complete/release and fencing tokens when using `WebhookProcessor`. A completed duplicate is skipped; busy leases must return a retriable failure, body-hash conflicts must be rejected, and handler failures release the lease. **Handlers still need idempotent domain writes**; a generic coordinator cannot atomically commit your database and another external service. Prefer one database transaction for inbox + order updates. Retain event IDs at least seven days; dashboard replay can deliver older events, so retain domain idempotency records longer as required.

## ASP.NET Core sample

[samples/AspNetCore](samples/AspNetCore) is a runnable application with a real SQLite database, server-priced demo order, authenticated checkout endpoint, persisted attempt keys, strict raw-body verification and an atomic webhook-inbox/order update. It contains no mock payment-success endpoint. The browser return cannot mark an order paid. Read its setup and limitations before adapting it.

## Errors and retries

Catch `LunixiValidationException` (400/422), `LunixiRateLimitException` (429), `LunixiApiException` and `LunixiTransportException`. Gateway errors expose `Code`, `RequestId` and `RetryAfterSeconds`; bodies are available only for explicit inspection. Error messages omit upstream messages, secrets and customer input. Rate limits are not retried automatically.

Financial writes may have completed even when a connection times out. `OutcomeMayBeUnknown` identifies SDK transport failures on writes. Reconcile status and reuse the **original persisted operation key**. Caller cancellation may surface as `OperationCanceledException` and also leaves a write outcome uncertain. Never generate a new key merely because a request timed out. Normal gateway idempotency retention is 24 hours; after its retention window reconcile before resubmitting. The client uses a new signature nonce per attempt while retaining exactly the same body and key.

## Build and verification

Install the .NET 8 SDK/runtime (a newer SDK can compile the library; the sample/test host runs on .NET 8).

```bash
dotnet restore tests/Lunixi.Sdk.Tests/Lunixi.Sdk.Tests.csproj --locked-mode
dotnet build tests/Lunixi.Sdk.Tests/Lunixi.Sdk.Tests.csproj -c Release --no-restore
dotnet test tests/Lunixi.Sdk.Tests/Lunixi.Sdk.Tests.csproj -c Release --no-build
dotnet format tests/Lunixi.Sdk.Tests/Lunixi.Sdk.Tests.csproj --verify-no-changes --no-restore
dotnet list tests/Lunixi.Sdk.Tests/Lunixi.Sdk.Tests.csproj package --vulnerable --include-transitive
dotnet pack src/Lunixi.Sdk/Lunixi.Sdk.csproj -c Release --no-restore -o artifacts
```

Tests use offline HTTP handlers, a Node.js Ed25519/JSON.stringify fixture, existing cross-SDK webhook vectors, and a real ASP.NET Core TestServer + SQLite database. CI runs on Linux, Windows and macOS. No live charge is made by the test suite.

[Changelog](CHANGELOG.md) · [Contributing](CONTRIBUTING.md) · [Security](SECURITY.md) · [MIT license](LICENSE)

## Merchant-page integration example

The runnable ASP.NET Core sample now exposes authenticated `POST /checkout/{orderId}/form` for embedded HTML by default; `?mode=hosted` uses the validated backend URL. The existing `/checkout/{orderId}` JSON contract is retained. `Lunixi.AspNetCoreSample.CheckoutPresentation` contains the renderer and URL validator. `DEMO_API_TOKEN` remains strictly server-to-server: never send it to browser JavaScript. Your merchant checkout controller must authenticate the customer, verify order ownership and native CSRF before proxying/rendering this response. This sample does not implement customer login or a production tenant authorization model.

Before upgrading from 0.x, follow [the money-string migration](MIGRATION-1.0.md).
