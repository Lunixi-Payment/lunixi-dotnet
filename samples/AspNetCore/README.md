# ASP.NET Core integration

Version **1.0.1** uses canonical minor-unit strings. Follow the SDK money-string migration and install after the version is available on NuGet.

The source patch adds a concrete merchant-site presentation example. **Embedded checkout is the default**: load only the official Lunixi bundle and mount the existing form; methods and multi-card choices stay in the Lunixi panel. Optional hosted mode uses the backend `paymentPageUrl` after exact HTTPS `pay.lunixi.com/payment/` validation. Its token may differ from the embedded token. Missing URLs keep embedded available and hosted on hold/reconciliation with the original attempt/key. Never derive a URL from the browser token. Authorize the customer/order and native browser CSRF before rendering, send `Cache-Control: private, no-store`, preserve the merchant CSP nonce and keep tokens/HTML/redirect URLs out of logs. Generic response data preserves `paymentPageUrl`; no new typed product parity is claimed.

This runnable .NET 8 example demonstrates server-side hosted-intent creation and a durable raw-body webhook receiver. The SDK supplies signing/authentication; this application supplies its order database, checkout-attempt keys and atomic event deduplication.

## Run

Set these server environment variables through your secret manager or shell. No secret belongs in appsettings committed to source control.

| Variable | Value |
| --- | --- |
| `LUNIXI_BASE_URL` | `https://api-gateway.lunixi.com` |
| `LUNIXI_MERCHANT_ID` | Merchant identity expected in authenticated payment records |
| `LUNIXI_KEY_ID` | Merchant TEST SECRET key identifier |
| `LUNIXI_PRIVATE_KEY_PEM` | Ed25519 PKCS#8 PEM private key, including real newlines |
| `LUNIXI_WEBHOOK_SECRET` | Endpoint's signing secret |
| `DEMO_API_TOKEN` | Random backend token, at least 32 characters |
| `LUNIXI_DATABASE_PATH` | Absolute writable SQLite path on persistent storage |
| `MERCHANT_CALLBACK_URL` | Your HTTPS `/payments/return` URL |

```bash
dotnet run --project samples/AspNetCore/AspNetCore.csproj --urls http://127.0.0.1:5080
```

Use HTTPS at the deployed reverse proxy. The local HTTP listener is for development only. Register your externally accessible HTTPS `/webhooks/lunixi` endpoint in the Lunixi dashboard. Choose your TEST environment and payment event subscriptions.

Call `POST /checkout/DEMO-1001` with `Authorization: Bearer <DEMO_API_TOKEN>` from your trusted backend. It reads 10050 TRY minor units from SQLite; supplied request-body prices are ignored. It returns the actual gateway envelope with `data.paymentId` and `data.token`, which your authenticated checkout can hand to the existing Lunixi form. Repeated requests reuse the database's original key, even across process restarts. After 23 hours the sample requires reconciliation rather than sending a possibly expired key again.

`GET /orders/DEMO-1001` requires the same backend authorization. `/payments/return` is informational. A verified event locates only a locally bound payment. The server then performs an authenticated GET and matches payment ID, configured merchant ID, order reference, exact amount, currency and complete captured/refunded balances. Only complete capture with zero refunds marks the order paid. Authorized payments remain on hold; verified refunds retain their later settlement state even if a later GET is stale. Event fields and browser returns are never settlement authority. Invalid signatures return 400; unknown/mismatched captured payments return 503 for reconciliation; identical committed deliveries return 200. Inbox insert and order update commit in the same SQLite transaction. A reused event ID with changed bytes is rejected.

## Adaptation boundaries

- The seeded order and static backend token demonstrate integration, not a customer identity/permission system. Replace them with your authenticated order ownership and server-side pricing.
- This sample handles ordinary capture, authorization and refund settlement reads. It deliberately fails closed for split settlements, split-leg delivery and promissory-note captures; implement the payment rail's fulfillment and reconciliation policy for your business. Unsupported or incomplete settlement evidence requires reconciliation and is not acknowledged as payment success.
- The demo requires the final captured amount to equal the stored order total. If you enable installment fees, insurance premiums, partial capture or other adjusted amounts, persist and validate the approved final amount and separate order fulfillment from provider/insurance state.
- Send buyer, billing/shipping and product-specific information from your trusted order records where required. No raw card is collected here. Use the shared Lunixi form for cards and available alternative methods.
- Add a background status reconciler for ambiguous outcomes, operational review for overdue attempts, inbox retention/monitoring and refund/dispute handling before production deployment. Retain deduplication/domain records through manual dashboard replay.
- SQLite is suitable for this executable single-store demonstration. For distributed production workers use your transactional order database and suitable locking/isolation. Do not deploy the database on ephemeral storage.

Offline TestServer tests exercise authentication, original attempt-key reuse, server pricing, untrusted browser return, amount mismatch, valid/duplicate/conflicting deliveries and durable database state. Tests make no gateway calls.

## Merchant-page integration example

The runnable ASP.NET Core sample now exposes authenticated `POST /checkout/{orderId}/form` for embedded HTML by default; `?mode=hosted` uses the validated backend URL. The existing `/checkout/{orderId}` JSON contract is retained. `Lunixi.AspNetCoreSample.CheckoutPresentation` contains the renderer and URL validator. `DEMO_API_TOKEN` remains strictly server-to-server: never send it to browser JavaScript. Your merchant checkout controller must authenticate the customer, verify order ownership and native CSRF before proxying/rendering this response. This sample does not implement customer login or a production tenant authorization model.
