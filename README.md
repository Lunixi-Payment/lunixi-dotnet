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

Install the published [Lunixi.Sdk 1.0.1 package from NuGet](https://www.nuget.org/packages/Lunixi.Sdk/1.0.1):

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
using System.Text.Json;
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
// Order details every payment requires: take them from your checkout, never a placeholder.
var buyer = JsonSerializer.SerializeToElement(new
{
    name = "Ada", surname = "Yilmaz", identityNumber = "11111111111", email = "ada@example.com",
    gsmNumber = "+905350000000", city = "Istanbul", country = "Turkey", zipCode = "34000",
});
var billingAddress = JsonSerializer.SerializeToElement(new
{
    contactName = "Ada Yilmaz", address = "Example Street 1", city = "Istanbul", country = "Turkey", zipCode = "34000",
});

var request = new CreateIntentRequest("10050", "TRY", "ORDER-1001")
{
    CallbackUrl = new Uri("https://merchant.example/payments/return"),
    Buyer = buyer,
    BillingAddress = billingAddress,
    BasketItems = [new BasketItem("SKU-1", "Gift card", "10050", "VIRTUAL") { Category1 = "Digital" }],
    EnabledInstallments = [1, 2, 3], // optional: at most 3 installments
};

// Persist this key with the checkout attempt BEFORE the first gateway request.
var result = await lunixi.Payments.CreateIntentAsync(request, persistedAttemptKey);
var paymentId = result.Data.GetProperty("paymentId").GetString();
var checkoutToken = result.Data.GetProperty("token").GetString();
```

Use your server's order amount. `10050` means 100.50 TRY. All typed payment amounts are positive canonical minor-unit strings, capped at the signed 64-bit fiat limit. Basket item `Price` is the entire line total, and line totals must equal `Amount`. Root `price` / `paidPrice` fields are rejected, including through generic transport; basket `price` remains valid. Read the buyer, billing and shipping JSON from trusted server records. Optional `BasketItem.ProductCategory` (sent as `productCategory`) is the Lunixi product category, used by payment methods with legal installment limits (e.g. shopping loan) and other platform features; use a `ProductCategories` constant (`ProductCategories.All` lists the codes known to this version). Null omits the field and keeps today's behaviour; an unknown code is rejected by the API with HTTP 400. The dictionary only grows, so the property stays a string. No raw-card charge API is enabled by default.

Every payment body carries the order details (`OrderDetails`): `buyer` (`name`, `surname`, `identityNumber` — Turkish ID or passport number, `email` — ASCII, `gsmNumber`, `city`, `country`, `zipCode`, `ip` — the buyer's browser IP), `billingAddress` (`contactName`, `address`, `city`, `country`, `zipCode`) and at least one `basketItems` entry (`id`, `name`, `category1`, `itemType`, `price`; the prices add up to the amount); a `PHYSICAL` item also needs `shippingAddress`. On `CreateIntentRequest` and the marketplace intents `ip` may be left out: the Lunixi form takes it from the buyer's browser. `CreateIntentAsync`, `CreateMarketplaceIntentAsync`, `DirectMethodPaymentAsync`, `SplitPaymentRequest`, the marketplace `CreateCheckoutIntentAsync` and card charges, `StoreCardAsync` (buyer and billing address only) and `Wallet.Topups.StartCardAsync` (buyer and billing address) throw `LunixiConfigurationException` naming the missing paths before sending (`OrderDetails.MissingFields(body)` returns the same list, for example `buyer.email` or `basketItems.0.category1`). The gateway answers a missing field with HTTP 400 `payment.order.field_required` and an invalid one with `payment.order.field_invalid` (`OrderFieldErrorCodes`, `OrderFieldReasons`).

`EnabledInstallments` only narrows the installment counts the hosted form offers for this payment (`[1]` = single payment only); the form shows only the counts your panel settings, the card and the platform maximum also allow, and null means no restriction. Counts are 1–36, distinct and include 1, otherwise `CreateIntentAsync` throws `LunixiConfigurationException` before any request; the list is sent as `enabledInstallments` in ascending order.

Pass the token to the existing Lunixi Checkout Form integration according to the [checkout documentation](https://docs.lunixi.com/en/payments/checkout-form). The SDK does not recreate card fields or the shared form. Browser callbacks are informational; use verified webhooks and authenticated status reads to decide fulfillment.

## Payment operations

```csharp
var payment = await lunixi.Payments.GetAsync(paymentId);
var capture = await lunixi.Payments.CaptureAsync(paymentId, persistedCaptureKey, amount: "10050");
var refund = await lunixi.Payments.RefundAsync(paymentId, persistedRefundKey, amount: "1000", reason: "Customer request");
var released = await lunixi.Payments.VoidAsync(paymentId, persistedVoidKey);
var methods = await lunixi.Payments.ListMethodsAsync();
```

`ListMethodsAsync("MOBILE_BILLING")` narrows the list to one method (`QR` is sent as `QR_CODE`). Each method lists its providers and the `inputFields` to send; always read the provider code from this response.

### Payments with any method

`DirectMethodPaymentAsync` starts a payment with any enabled method. Build the body with `DirectMethodPaymentRequest`; the idempotency key is required.

```csharp
var request = new DirectMethodPaymentRequest("10000", "TRY", "ORDER-1001")
    .WithMethodFields(PaymentMethods.MobileBilling, providerCode, new { gsmNumber = "+905321234567" })
    .WithCallbackUrl(new Uri("https://shop.example/payments/return"));
var started = await lunixi.Payments.DirectMethodPaymentAsync(request, persistedPaymentKey);
// HTTP 202 with data.action: OTP → ConfirmActionAsync(paymentId, actionId, new { otp = "123456" })
```

The SDK sends only the chosen method's block (`PaymentMethodFields.MethodBlocks`), keeps card fields (`card`, `installment`, `installmentQuoteToken`, `force3D`) for `WithCard`, checks the block structure (at most 32 fields; text, numbers, booleans, `ADDRESS` objects and `GROUP` lists) and throws `LunixiConfigurationException` before any request. A shopping loan needs 1–100 `BasketItems`. A raw JSON body can be passed to the `object` overload and goes through the same checks.

**Bank transfer.** `WithMethodFields(PaymentMethods.BankTransfer, null, new { bankCode = "0062" })` sends `bankTransfer: { bankCode }` and never a provider code; `BankTransferValues.OtherBanksCode` (`"9999"`) is the "Other banks" option. The response carries a `DISPLAY` action; `BankTransferInstruction.FromResponse(started)` returns the account, amount and payment code to show the buyer (null without such an action). The outcome arrives as `payment.captured` / `payment.failed`; `PaymentBankTransferEvents` lists the transfer events. Late, short and excess transfers are listed with `ListBankTransferExceptionsAsync(new BankTransferExceptionFilters { Status = BankTransferExceptionStatuses.PendingDecision })` and decided with `AcceptBankTransferExceptionAsync` or `MarkBankTransferExceptionRefundedAsync`.

**Promissory notes.** Give the plan with `WithPromissoryNoteSchedule([new("5000", "2027-01-15"), new("5000", "2027-02-15")])` (1–24 rows in due-date order adding up to the amount) or `WithPromissoryNoteCount(2)`, never both. Read the notes with `GetPromissoryNotesAsync`, cancel them with `CancelPromissoryNotesAsync` and download the printable PDF with `DownloadPromissoryNoteDocumentAsync(paymentId, PromissoryNoteDocumentLocales.Turkish)`, which returns `LunixiBinaryResponse` (`Content`, `ContentType`). `PromissoryNoteEvents` lists the note webhooks.

**Addresses.** `ADDRESS` fields take codes from `lunixi.Reference.ListProvincesAsync()`, `ListDistrictsAsync("06")` and `ListNeighborhoodsAsync(districtCode)`.

`LunixiResponse.StatusCode`, `.Envelope`, `.Data` and `.RequestId` preserve pending states and pagination metadata. A 202 or a pending status is not proof of payment. Preauthorization uses `CreateIntentRequest.Preauth = true`, then capture or void.

Split APIs: `CreateSplitAsync`, `GetSplitAsync`, `ListSplitsAsync`, `RefundSplitAsync`. `SplitPaymentRequest` accepts the documented JSON DTO (with `buyer`, `billingAddress`, `basketItems` and, for a `PHYSICAL` item, `shippingAddress`), validates the order details, 1–5 legs, exact `totalAmount`, distinct stored-card/provider references, allowed method fields, installment quotes and required 3DS callback. Use stored-card tokens; never raw card numbers. A non-card leg carries one of `SplitPaymentFields.LegBlocks` (`mobileBilling`, `wallet`, `promissoryNote`); meal card, open banking, shopping loan and bank transfer cannot be split legs. `SplitPaymentEvents` lists the split webhooks; add `SplitPaymentEvents.LegEventsSubscription` to an endpoint to also receive leg events. Action APIs are `ConfirmActionAsync` and `ResendActionAsync`. Documented in-progress HTTP 400 split responses are returned as `LunixiResponse`; failed/reconciliation states remain exceptions. **Split endpoint availability depends on gateway rollout and merchant capabilities. Source support does not assert live availability.** The same split request and operation key must be reused to continue the saga.

### Cards, installments and marketplace intents

```csharp
var bin = await lunixi.Payments.BinInfoAsync("45438712");
var table = await lunixi.Payments.InstallmentOptionsAsync(new InstallmentOptionsRequest("10000", "TRY")
{
    BinOrPan = "45438712", Installment = 3, EnabledInstallments = [1, 2, 3],
});
var taxonomy = await lunixi.Payments.CardTaxonomyAsync();
```

`InstallmentOptionsAsync` returns the table and, for an installment count above one with a BIN, the `quoteToken` that `DirectMethodPaymentRequest.WithInstallment(count, quoteToken)` needs. `EnabledInstallments` follows the intent rule (1–36, distinct, 1 included, sent ascending). A BIN or PAN prefix must be 6–19 digits and is never placed in an exception message.

Stored cards: `StoreCardAsync(new StoreCardRequest(card, cardUserKey, callbackUrl) { Buyer = buyer, BillingAddress = billingAddress }, persistedKey)` (signed, idempotency key required; buyer and billing address required) returns the `storedCardToken`; `ListStoredCardsAsync(cardUserKey)`, `GetStoredCardAsync(storedCardToken)` and `DeactivateStoredCardAsync(storedCardToken, persistedKey)` (signed, key required) manage them. `StoreCardRequest.ToString()` redacts the card.

`CreateMarketplaceIntentAsync(intent, [new MarketplaceSeller("dealer-a", "10000"), new MarketplaceSeller("dealer-b", "5000")], persistedKey, MarketplaceSellerPolicies.AllOrNothing)` adds `sellers[]` to the hosted intent body; the sellers' `amountMinor` values must add up exactly to the intent amount, otherwise the SDK throws before sending. Reports: `FailoverRecoveryAnalyticsAsync(filters)` and `ProviderCredentialAnalyticsAsync(credentialId, filters)`.

## Payment links

```csharp
var link = await lunixi.PaymentLinks.CreateAsync(
    CreatePaymentLinkRequest.Fixed(PaymentLinkUsages.SingleUse, "150000", "TRY", "Invoice 1001")
        .WithRecipient(new Dictionary<string, string> { ["email"] = "buyer@example.com" })
        .WithExpiresAt(DateTimeOffset.UtcNow.AddDays(7)),
    persistedLinkKey);
var linkId = link.Data.GetProperty("id").GetString()!;
await lunixi.PaymentLinks.SendAsync(linkId, PaymentLinkDeliveryChannels.Email, persistedSendKey);
```

Every payment-link call is signed, reads included; the signing key decides the environment. `CreateAsync` and `SendAsync` require an idempotency key (8–200 printable characters). `UpdateAsync(linkId, expectedRowVersion, changes)` sends `{ expectedRowVersion, ...changes }` and rejects `usage`, `amountMode`, `currency` and unknown fields before sending; pass a `JsonObject` to clear a field with null. `PauseAsync`, `ResumeAsync`, `DeactivateAsync` (or `ActionAsync`), `GetAsync`, `ListAsync`, `ListAttemptsAsync` and `ListPaymentsAsync` cover the rest; `QrAsync(linkId, PaymentLinkQrFormats.Png, 512)` returns the QR image as `LunixiBinaryResponse`. `CreatePaymentLinkRequest.Open` and `.Itemized` (with `PaymentLinkItem`) build the other amount modes; `PaymentLinkCustomField` adds questions to the payment page.

Link images take three steps, run together by `UploadImageAsync`:

```csharp
PaymentLinkImage image = await lunixi.PaymentLinks.UploadImageAsync("logo.png", PaymentLinkImageContentTypes.Png, bytes);
// use image.AssetId as WithImageAssetId(...) of a link or an item
```

`CreateImageUploadAsync` returns a presigned storage target, the SDK PUTs the bytes there with **only** the returned upload headers (never the access token, the key identifier or the request signature) and `ConfirmImageUploadAsync` publishes the image after the gateway checks the stored bytes (PNG, JPEG or WebP, at most 2 MiB). A refused PUT throws `LunixiStorageUploadException` (for example `Code = "AccessDenied"` after the URL expired) and is not retried; upload again for a new target. Treat `PaymentLinkImageUpload.UploadUrl` as a credential until it expires.

## Physical POS

```csharp
var key = PhysicalPosClient.NewIdempotencyKey();
await orders.SavePosKeyAsync(orderId, key);                       // store the key first
try
{
    PhysicalPosWriteResult started = await lunixi.PhysicalPos.CreatePaymentAsync(new
    {
        operation = PhysicalPosPaymentOperations.Sale,
        target = new { deviceId = "dev_1" },
        amountMinor = "4500",
        currency = "TRY",
        merchantReference = "ORDER-1001",
        allowDuplicateReference = false,
    }, key);
    var paymentId = started.Payment!.Value.GetProperty("paymentId").GetString();   // 202: accepted, not paid
}
catch (PhysicalPosOutcomeUnknownException unknown)
{
    // The payment may exist: call again later with unknown.IdempotencyKey, never with a new key.
}
```

`lunixi.PhysicalPos` starts payments on card terminals and reads them; `lunixi.PhysicalPosManagement` manages stores, registers, terminals, devices, device keys and the end-of-day policy. Every call is signed and the key decides the environment (a TEST key can only read). A payment write is asynchronous: follow the outcome with the `physical_pos.payment.*` webhooks or `GetPaymentAsync`. `CreatePaymentAsync` and `CreateReversalAsync` require your stored `Idempotency-Key` (8–200 printable ASCII characters, sent exactly as given) and return `PhysicalPosWriteResult`: 202 for a new payment, 200 with `Replayed` for the same key and body. The SDK retries a payment write itself with the same key and body; when any attempt got no answer, a 2xx without a payment or a 5xx that does not say "not started", the call throws `PhysicalPosOutcomeUnknownException`. Cancellation, end-of-day starts and keyed management creates accept your key or generate a UUID, returned as `IdempotencyKey`.

Bodies are anonymous objects, dictionaries or `JsonObject` values (use a `JsonObject` or a dictionary to send `null` and clear a nullable field); money fields are minor-unit strings and counts are numbers. Filters are dictionaries: `ListPaymentsAsync(new Dictionary<string, object?> { ["limit"] = 50, ["createdFrom"] = DateTimeOffset.UtcNow.AddDays(-1) })`; `dateFrom` / `dateTo` of the reports accept a `DateOnly`. Unknown fields or filters, wrong JSON types and non-canonical amounts throw `LunixiConfigurationException` before sending. Card data is never sent: the card is read on the device, and a card-data field name or a card number in `merchantReference`, an item name or tax group, or the `merchantReference` / `q` filter throws `PhysicalPosCardDataRejectedException` with only the field path. A 429 is `PhysicalPosRateLimitException`; a void after end-of-day is `PhysicalPosVoidAfterSettlementException` (use a `REFUND`). Lists return `PhysicalPosPage` (`Items`, `HasMore`, `NextCursor`, `TotalCount` with `includeTotal`), and `Iterate…Async` walks every page.

```csharp
PhysicalPosWebhookEvent pos = PhysicalPosClient.ParseWebhookEvent(rawRequestBytes, requestHeaders, webhookSecret);
// pos.Category: payment, fiscal_document, settlement or other; deduplicate by pos.Id and pos.HistorySeq per payment.
```

`ParseWebhookEvent` verifies the signature before reading the payload, checks its money fields, and exposes `Original` (a reversal's original payment and its origin), `Cashier` (set only for a payment started from a device cashier session), `IsDeviceSale` and `IsSettlementEvent`; end-of-day events carry no `HistorySeq`.

## Wallet

```csharp
var endUser = await lunixi.Wallet.EndUsers.CreateAsync(new
{
    programId, accountType = WalletAccountTypes.Personal, phone = "+905551112233", externalCustomerId = "customer-1",
}, persistedEndUserKey);

var started = await lunixi.Wallet.Transfers.InitiateW2WAsync(new
{
    endUserId, sourceWalletAccountId, amount = "12550", currency = "TRY", destinationWalletNo = "1002003004",
}, persistedTransferKey);
await lunixi.Wallet.Transfers.CompleteW2WAsync(started.GetProperty("operationId").GetString()!, new
{
    endUserId, integrityHash = started.GetProperty("integrityHash").GetString(), nonce = started.GetProperty("nonce").GetString(), otpCode,
}, persistedCompleteKey);
```

`lunixi.Wallet` covers the wallet's merchant routes through sub-clients: `Programs`, `EndUsers`, `Wallets`, `Deposits`, `Corporate`, `Fees`, `Transfers`, `Withdrawals`, `Operations`, `Topups`, `Payments`, `Qr` and `BulkPayouts`, plus `PingAsync`. Every call is signed, reads included. Each call is checked against `WalletRoutes` (method, path, key rule, body and query fields) before anything is sent: an unknown field or filter, a missing required field, an amount that is not a minor-unit digit string (`"12550"` is 125.50 TRY; numbers and decimals are refused, never rounded) or a missing `Idempotency-Key` on a route that requires one throws `LunixiConfigurationException`. Money moves in two steps: `Initiate…` reserves the limit and returns `operationId`, `integrityHash` and `nonce`; `Complete…` commits it and puts `operationId` into the body itself. Methods return the envelope's `data` as a `JsonElement`; lists return `LunixiPage` (`Items`, `HasMore`, `NextCursor`) and `IterateAsync` walks every page. `Qr.ImageAsync(qrId, WalletQrImageFormats.Png, 8)` returns the image as `LunixiBinaryResponse`.

## Bank accounts, transfers and currency exchange

```csharp
var transactions = await lunixi.BankConnect.ListTransactionsAsync(new Dictionary<string, object?>
{
    ["accountId"] = new[] { accountId },
    ["bookedFrom"] = DateTimeOffset.UtcNow.AddDays(-30),
    ["direction"] = BankTransactionDirections.Credit,
    ["limit"] = 100,
});

BankConnectWriteResult transfer = await lunixi.BankConnect.Transfers.CreateAsync(new
{
    accountId, receiverIban = "TR330006100519786457841326", receiverName = "Ayşe Yılmaz", amountMinor = "150075", description = "Invoice 1001",
}, payoutId);
```

`lunixi.BankConnect` reads banks, connections, accounts, balances and transactions (`List…Async`, `Iterate…Async`, `Get…Async`) and asks for a fresh read with `RequestSyncAsync` (HTTP 202). Every call is signed and the key decides the environment; connecting a bank happens in the Lunixi dashboard only. Filters are checked before sending (`BankConnectFilters`): unknown filters, values outside their set, `limit` outside 1–100, more than 100 account ids, non-canonical amounts, `minAmountMinor` above `maxAmountMinor`, `bookedTo` without `bookedFrom` and windows wider than 92 days throw `LunixiConfigurationException`. Status and type filters take one value or a list.

`Transfers` (`ListPurposesAsync`, `CreateAsync`, `ListAsync`, `IterateAsync`, `GetAsync`) and `Fx` (`ListRatesAsync`, `CreateQuoteAsync`, `CreateOrderAsync`, `ListOrdersAsync`, `IterateOrdersAsync`, `GetOrderAsync`) need the `bank-connect:transfer` scope and a key with an IP allowlist. `CreateAsync` and `CreateOrderAsync` check your `Idempotency-Key` (8–200 printable ASCII characters) or generate a UUID when you pass none, retry a network error or a 5xx with the same key, and return `BankConnectWriteResult` with the key used (`IdempotencyKey`), `Replayed` and the record (`Entity`). Pass your own key, such as your payout id, to retry safely across processes. Bodies are checked against `BankTransferRoutes`; a quote takes exactly one of `sourceAmountMinor` and `targetAmountMinor` and two different accounts. Read verified webhooks with `BankAccountTransactionReceivedData.FromWebhook` (`BankConnectEvents`) and `BankTransferWebhookData.FromWebhook` (`BankTransferEvents`, `bank_transfer.*`; distinct from the payment `payment.bank_transfer.*` events). Exchange orders have no webhook.

## Sanctions screening

```csharp
var result = await lunixi.Sanction.ScreenNameAsync(new
{
    name = "Jane Doe", subjectType = SanctionSubjectTypes.Person, mode = SanctionScreenModes.SanctionsPep,
});
if (!SanctionClient.IsClearScreening(result)) { /* review the matches; the decision is yours */ }
```

`lunixi.Sanction` screens names, wallets, accounts, transactions, public procurement bans and adverse media, evaluates behavioural alerts, registers subjects for ongoing monitoring and reads alerts, matches, monitored entities, dataset freshness, entity intelligence and your deny list. Calls carry the access token only (no per-request signature) and need the `sanction:view_results` permission. Bodies and filters are checked against `SanctionRoutes` before sending, including the nested `identifiers`, `parties` and `accounts` lists, the `anyOf` groups and the value sets (`SanctionRoutes.ValueSets`). Screening returns matches, not verdicts: `IsClearScreening` is true only without any match or hit and, when the answer reports coverage, with `coverageStatus` `COMPLETE`. A screening that cannot run is HTTP 503 and throws; a 2xx answer that is not `status: "success"` throws `SanctionScreeningException`.

## Insurance

```csharp
var quotes = await lunixi.Insurance.CreateQuoteAsync(new { insuranceType, currency = "TRY", paymentIntentId });
var option = quotes.GetProperty("options")[0];
var order = await lunixi.Insurance.CreateOrderAsync(new
{
    quoteId = option.GetProperty("quoteId").GetString(),
    consent = InsuranceClient.ConsentFromQuote(option, acceptedAt),
}, persistedOrderKey);
BasketItem premiumItem = InsuranceClient.PremiumBasketItem(order, "Travel insurance", "Insurance");
```

`lunixi.Insurance` lists and searches products, reads reference codes, prices quotes, creates and reads orders, binds a Lunixi payment or records a premium collected outside Lunixi, requests, cancels and refunds policies, submits and reads leads, and reads reconciliation. Every call is signed. `CreateOrderAsync`, `ConfirmPaymentAsync`, `IssuePolicyAsync`, `CancelOrderAsync`, `RefundPremiumAsync` and `CreateLeadAsync` require an `Idempotency-Key` (8–200 printable ASCII characters); `CreateQuoteAsync` and `BindPaymentAsync` take one optionally. Top-level fields and filters are checked before sending (`InsuranceFields`); a quote's basket is either `paymentIntentId` or `context.items`, never both. The field rules of an insurance type's own object are left to the gateway, which answers 422 `insurance.validation.invalid` with every field error at once. `GetPolicyDocumentAsync`, `GetPolicyReceiptAsync` and `GetPolicyCertificateAsync` return the PDF as `LunixiBinaryResponse`; `ExportReconciliationAsync` returns the CSV with its trailer read (`Truncated`, `NextCursor`, `Aborted`). Read verified webhooks with `InsuranceWebhookData.FromWebhook` (order events) and `InsuranceLeadWebhookData.FromWebhook` (`insurance.lead.submitted`).

## Subscriptions

```csharp
var customer = await lunixi.Subscriptions.Customers.UpsertAsync(new
{
    externalId = "user-42", name = "Ada", surname = "Yilmaz", email = "ada@example.com", gsmNumber = "+905350000000",
    identityNumber = "11111111111", billingAddress = "Example Street 1", billingCity = "Istanbul", billingCountry = "Turkey",
    billingZipCode = "34000",
}, persistedCustomerKey);
var plan = await lunixi.Subscriptions.Plans.CreateAsync(new
{
    productId, code = "pro-monthly", name = "Pro", price = "9900", currency = "TRY",
    billing = new { frequency = SubscriptionBillingFrequencies.Monthly },
}, persistedPlanKey);
var subscription = await lunixi.Subscriptions.CreateAsync(new
{
    customerId = customer.GetProperty("id").GetString(), planId = plan.GetProperty("id").GetString(), paymentCardId,
}, persistedSubscriptionKey);
if (SubscriptionStatuses.IsActive(subscription)) { /* grant access */ }
```

`lunixi.Subscriptions` creates, reads, lists, cancels, pauses, resumes and re-plans subscriptions and hangs the related resources off sub-clients: `Plans`, `Customers`, `Cards` (saved from the checkout SDK's `paymentMethodToken`; a card number is refused by field name), `Invoices`, `Portal`, `Products`, `Features`, `Addons` and `Usage`. Calls carry the access token only (no per-request signature). `CreateAsync` requires an `Idempotency-Key` and also sends it in the body; plan, customer, card, plan-change and charge-retry writes accept one. Every call is checked against `SubscriptionRoutes` before sending (unknown fields and filters, missing required fields, value sets such as `SubscriptionCollectionMethods`, the plan's `billing` rule and supported currencies). Amounts are canonical minor-unit strings (`"0"` is a free plan), checked in bodies, filters and answers. Lists return `LunixiPage`; the subscription, plan, customer and invoice lists page with `pageToken`, which `IterateAsync` sends back. The embedded portal starts with `Portal.CreateSdkSessionAsync(customerId, origin)`; a hosted portal link from `Portal.CreatePortalLinkAsync` opens at `SubscriptionPortalLinks.HostedUrl(link)`. Webhook event names are in `SubscriptionEvents`.

## KYC and identity signals

```csharp
var created = await lunixi.Kyc.CreateSessionAsync(new
{
    subjectId = "user-42", sessionType = KycSessionTypes.Kyc, externalCustomerId = "user-42", person = new { firstName = "Ada" },
}, persistedSessionKey);

var links = await lunixi.Identity.AccountLinksAsync(traceId);
if (IdentitySignals.IsDeviceShared(links)) { /* one device, several accounts: review */ }
```

`lunixi.Kyc` creates KYC, KYB and evidence sessions (`person` / `company` are sent as `customer` with its `customerType`, `metadata` as `evidenceContextJson`), sets the jurisdiction and primary document, uploads documents from the server, submits, sends to manual review and reads the timeline, your KYC policy, webhook deliveries and erasure requests. `ListSessionsAsync` takes only `limit` and `cursor`. The browser capture runs with the publishable key; the `kyc.*` webhook (`KycEvents`) is the authority for the result, and `KycAssuranceLevels.Meets` compares an achieved assurance level with the one you require. `lunixi.Identity` analyses browser fingerprint traces (`AnalyzeSyncAsync`, `AnalyzeAsync`) and reads account links, sessions, related sessions, timelines and top sources; it returns signals, not an allow / block verdict. Both surfaces are bearer-only, checked against `KycRoutes` / `IdentityRoutes`, and never put a document image or field value in an error message.

## Marketplace sub-dealers

```csharp
MarketplaceSeller[] sellers = [new("dealer-a", "60000"), new("dealer-b", "40000")];
var intent = await lunixi.Marketplace.CreateCheckoutIntentAsync(
    new CreateIntentRequest("100000", "TRY", "ORDER-7")
    {
        Buyer = buyer, BillingAddress = billingAddress, // order details as in the checkout example
        BasketItems = [new BasketItem("SKU-7", "Gift card", "100000", "VIRTUAL") { Category1 = "Digital" }],
    }, sellers, MarketplaceSellerPolicies.AllOrNothing, persistedIntentKey);

var dealer = await lunixi.Marketplace.CreateDealerAsync(new { legalName = "Acme Ltd", dealerType = MarketplaceDealerTypes.LegalEntity });
var link = await lunixi.Marketplace.OnboardingTokenAsync(dealer.GetProperty("id").GetString()!);
var onboardingUrl = MarketplaceDealerOnboarding.Url(link);
```

`lunixi.Marketplace` sends split charges across sub-dealers and runs the dealer lifecycle (create, bulk create, list, status, onboarding link, application, terms, contract, activation, KYC result), commissions (rules, preview, resolve), payouts (create, mark paid, bulk mark paid), the payout ledger, transactions and the hash-chained audit log. The split charges (`CreateCheckoutIntentAsync`, `ChargeMarketplaceCard2dAsync`, `ChargeMarketplaceCard3dAsync`, `ChargeMarketplaceCardAsync`) are signed per request and take an optional `Idempotency-Key`; the other routes are bearer-only. The seller legs must add up to `amount` and a card charge needs its commercial context (`MarketplaceRoutes.ChargeRequiredFields`), or nothing is sent. Card data is sent as given and never placed in an error message; prefer the hosted checkout intent, since a direct card charge is PCI DSS SAQ-D scope. An intent answer without a token or form throws `MarketplaceCheckoutException`.

## Coverage and explicit transport

| Surface | .NET 1.0.1 |
| --- | --- |
| Hosted intents, payment status/list, capture/refund/void, methods | Typed clients |
| BIN, installment options, card taxonomy, stored cards, marketplace intents, payment analytics | Typed `lunixi.Payments` methods |
| Payment links, including the QR image and link-image upload | `lunixi.PaymentLinks` |
| Physical POS payments, reversals, reports, end-of-day runs and management (stores, registers, terminals, devices, device keys, end-of-day policy) | `lunixi.PhysicalPos`, `lunixi.PhysicalPosManagement` |
| Payments with any method, payment actions, bank transfer exceptions, promissory notes (including the PDF) | `DirectMethodPaymentRequest` and typed `lunixi.Payments` methods |
| Türkiye address dictionary | `lunixi.Reference` |
| Split creation/status/list/refund and payment actions | Validated JSON DTO and typed client methods; rollout dependent |
| Fraud decisions, transaction event ingest (single and batch), decision logs | `lunixi.Fraud` |
| Webhooks | `WebhookVerifier`, `IWebhookReplayStore`, `WebhookProcessor` |
| Wallet: end users, wallets, transfers, withdrawals, top-ups, payments, QR codes, bulk payouts | `lunixi.Wallet` |
| Bank accounts (banks, connections, accounts, balances, transactions), outgoing transfers, currency exchange | `lunixi.BankConnect`, `.Transfers`, `.Fx` |
| Sanctions screening, ongoing monitoring, entity intelligence, deny list | `lunixi.Sanction` |
| Insurance products, quotes, orders, premium payment, policies and their PDFs, leads, reconciliation | `lunixi.Insurance` |
| Subscriptions with plans, customers, saved cards, invoices, the customer portal, products, features, add-ons and usage | `lunixi.Subscriptions` and its sub-clients |
| KYC, KYB and evidence sessions; identity signals (device and account linking) | `lunixi.Kyc`, `lunixi.Identity` |
| Marketplace split charges, sub-dealers, commissions, payouts, audit log | `lunixi.Marketplace` |
| Other public JSON APIs | `SendAsync` with explicit documented route/body; no typed parity claim |
| Other binary downloads, multipart uploads, browser checkout-session APIs | Not implemented by this JSON transport |

```csharp
var plans = await lunixi.Subscriptions.Plans.ListAsync(new Dictionary<string, object?> { ["pageSize"] = 25 });
var decision = await lunixi.Fraud.EvaluateAsync(new FraudEvaluateRequest(new
{
    transactionRef = "ORDER-1001", subjectType = "CUSTOMER", subjectId = "customer-1",
}));
```

Generic requests are signed by default and restricted to `/api/v1/` (and the subscription portal routes under `/api/panel/v1/subscriptions/portal/`) on the configured HTTPS origin. Consult the product's public DTO and scope documentation. Product validation remains the caller's responsibility; supply its required stable idempotency key explicitly. Generic transport never grants an unavailable product or an API scope.

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
