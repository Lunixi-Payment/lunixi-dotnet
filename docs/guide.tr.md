# .NET / C# SDK

1.0.1 para alanlarını kanonik küçük birim string olarak taşır. [Geçiş rehberini](../MIGRATION-1.0.md) uygulayın.

1.0.1 sürümü mağaza sitesi için somut bir sunum örneği içerir. **Varsayılan gömülü ödemedir**: yalnız resmi Lunixi bundle yüklenir ve mevcut form mount edilir; yöntem ve çoklu kart seçenekleri Lunixi panelinde kalır. İsteğe bağlı yönlendirme yalnız backend `paymentPageUrl` adresini, tam HTTPS `pay.lunixi.com/payment/` doğrulamasından sonra kullanır. Adresin token’ı gömülü token’dan farklı olabilir. Eksik adres gömülü ödemeyi engellemez; yönlendirme aynı deneme/anahtarla inceleme veya uzlaştırmada kalır. Tarayıcı token’ından adres üretmeyin. Formdan önce müşteri/sipariş sahipliğini ve yerel tarayıcı CSRF’yi doğrulayın; `Cache-Control: private, no-store` gönderin, mağazanın CSP nonce değerini koruyun; token, HTML ve yönlendirme adreslerini loglamayın. Genel yanıt verisi `paymentPageUrl` alanını korur; yeni tipli ürün eşitliği iddiası yoktur.

`Lunixi.Sdk` 1.0.1; Ed25519 imza, token önbelleği, ödeme formu intent'i, ödeme işlemleri, parçalı ödeme DTO doğrulaması, fraud istemcileri ve ham gövdeden webhook doğrulaması sağlar. .NET 8 ve üzerindeki ASP.NET Core, arka plan servisi ve diğer sunucu uygulamalarını destekler.

## Kurulum

1.0.1 [NuGet](https://www.nuget.org/packages/Lunixi.Sdk) üzerinde kullanılabilir olduğunda uygulamanıza ekleyin:

```bash
dotnet add package Lunixi.Sdk --version 1.0.1 --source https://api.nuget.org/v3/index.json
```

Kaynak geliştirmesi için [Lunixi-Payment/lunixi-dotnet](https://github.com/Lunixi-Payment/lunixi-dotnet) kullanılabilir. SDK klasöründen yerel paket de üretilebilir:

```bash
dotnet pack src/Lunixi.Sdk/Lunixi.Sdk.csproj -c Release -o artifacts
dotnet add /path/to/YourApp.csproj package Lunixi.Sdk --version 1.0.1 --source /absolute/path/to/sdk/artifacts
```

Bağımlılıklar için normal NuGet kaynağınızı açık tutun veya SDK projesini doğrudan referans ekleyin. Ed25519, NSec/libsodium ve sunucu ortamına uygun yerel kitaplıklarla çalışır. Tarayıcı/WASM desteklenmez. Örnek ve test sunucusu .NET 8 çalışma zamanını kullanır.

## İstemci ve ödeme formu

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
    BasketItems = [new BasketItem("SKU-1", "Örnek ürün", "10050")],
    EnabledInstallments = [1, 2, 3], // isteğe bağlı: en fazla 3 taksit
};
// Anahtarı kalıcı ödeme denemesinden okuyun; ağ hatasından sonra aynı anahtarı kullanın.
var response = await lunixi.Payments.CreateIntentAsync(request, persistedAttemptKey);
var paymentId = response.Data.GetProperty("paymentId").GetString();
var token = response.Data.GetProperty("token").GetString();
```

Uygulama başına tek istemci oluşturun; kapanışta dispose edin. TEST/LIVE ortamını anahtar belirler. Açık Ed25519 anahtarını panelde kaydedin; PKCS#8 PEM özel anahtarı yalnız sunucuda tutun. `Ed25519Signer.GenerateKeyPair()` ikisini de üretir. → [Kimlik doğrulama](/getting-started/authentication)

Tutarlar pozitif kanonik alt birim string değerleridir: `"10050"` = 100,50 TRY. Tutarı sipariş veritabanından okuyun. Sepet `Price` değeri satırın tamamının tutarıdır; satır toplamı `Amount` ile eşit olmalıdır. Eski üst düzey tutar alanları reddedilir. Akış gerektiriyorsa alıcı/fatura/teslimat JSON bilgisini güvenilir sunucu kaydından ekleyin. İsteğe bağlı `BasketItem.ProductCategory` (`productCategory` olarak gönderilir) Lunixi ürün kategorisidir; yasal taksit sınırı olan ödeme yöntemleri (ör. alışveriş kredisi) ve diğer platform özellikleri tarafından kullanılır. Değer için `ProductCategories` sabitlerini kullanın (`ProductCategories.All` bu sürümün bildiği kodları listeler). Null bırakılırsa alan gönderilmez ve mevcut davranış korunur; bilinmeyen kod API tarafından HTTP 400 ile reddedilir. Sözlük yalnız sona ekleme ile büyür; özellik bu yüzden string'dir.

`EnabledInstallments` bu ödeme için formun sunacağı taksit sayılarını yalnızca daraltır (`[1]` = yalnız tek çekim); form, panel ayarlarınızın, kartın ve platform üst sınırının da izin verdiği sayıları gösterir, null kısıt yok demektir. Sayılar 1–36 arası, tekrarsız olmalı ve 1'i içermelidir; aksi hâlde `CreateIntentAsync` istek göndermeden `LunixiConfigurationException` fırlatır. Liste `enabledInstallments` olarak artan sırada gönderilir.

Token'ı mevcut [Lunixi Ödeme Formu](/payments/checkout-form) entegrasyonuna aktarın. Açık yöntemleri ve alıcı etkileşimlerini form yürütür. SDK kart alanlarını yeniden üretmez; varsayılan olarak ham kartla tahsilat açmaz. Tarayıcı dönüşü bilgilendirmedir; teslimat kararı doğrulanmış webhook ve kimliği doğrulanmış durum sorgusuna dayanır.

| Ayar | Varsayılan | Davranış |
| --- | --- | --- |
| `Timeout` | 30 saniye | Yanıt okuma dahil her deneme |
| `MaxRetries` | 2 | Okumalar ve sabit anahtarlı yazmalar için ağ/5xx tekrarı |
| `RetryDelay` | 100 ms | En fazla iki saniyeye çıkan üstel bekleme |
| `MaxResponseBytes` | 1 MiB | Sınırlı yanıt gövdesi |
| `TimeProvider` | Sistem UTC | İmza ve token süresi için test edilebilir saat |

Token isteği imzalı ve gövdesizdir. Token süresinde 30 saniye pay bırakılır; 401 sonrasında bir kez yenilenir. Varsayılan taşıma yönlendirme izlemez ve HTTPS kökünden çıkmaz. Test için güvenilir `HttpMessageHandler` verilebilir; bu katman kimlik bilgilerini korumalı ve gizli bilgi loglamamalıdır.

## İşlemler ve kapsam

```csharp
var payment = await lunixi.Payments.GetAsync(paymentId);
var captured = await lunixi.Payments.CaptureAsync(paymentId, persistedCaptureKey, amount: "10050");
var refunded = await lunixi.Payments.RefundAsync(paymentId, persistedRefundKey, amount: "1000");
var voided = await lunixi.Payments.VoidAsync(paymentId, persistedVoidKey);
var methods = await lunixi.Payments.ListMethodsAsync();
```

Ön provizyon için `CreateIntentRequest.Preauth = true` kullanın; sonra capture/void çağırın. `LunixiResponse`, `StatusCode`, tam `Envelope`, `Data` ve `RequestId` değerlerini; sayfalama bilgisi dahil korur. Bekleyen durum veya HTTP 202 kesin ödeme başarısı değildir.

| Yüzey | .NET 1.0.1 |
| --- | --- |
| Form intent'i, ödeme durum/liste/capture/refund/void/yöntemler | `lunixi.Payments` |
| BIN, taksit seçenekleri, kart sınıflandırması, saklı kartlar, pazaryeri intent'leri, ödeme analizleri | Tipli `lunixi.Payments` metotları |
| Herhangi bir yöntemle ödeme, ödeme adımları, Para Transferi istisnaları, senet (PDF dahil) | `DirectMethodPaymentRequest` ve tipli `lunixi.Payments` metotları |
| QR görseli ve link görseli yükleme dahil ödeme linkleri | `lunixi.PaymentLinks` |
| Fiziki POS ödemeleri, ters işlemler, raporlar, gün sonu çalıştırmaları ve yönetim (mağazalar, kasalar, terminaller, cihazlar, cihaz anahtarları, gün sonu politikası) | `lunixi.PhysicalPos`, `lunixi.PhysicalPosManagement` |
| Türkiye adres sözlüğü | `lunixi.Reference` |
| Parçalı ödeme oluşturma/durum/liste/iade ve alıcı aksiyonları | `SplitPaymentRequest`, `lunixi.Payments` |
| Fraud kararı, işlem olayı (tekli ve toplu) ve karar kayıtları | `lunixi.Fraud` |
| Webhook doğrulama ve kalıcı tekrar koordinasyonu | `WebhookVerifier`, `IWebhookReplayStore`, `WebhookProcessor` |
| Cüzdan, banka hesapları (transfer ve döviz dahil), yaptırım taraması, sigorta | `lunixi.Wallet`, `lunixi.BankConnect`, `lunixi.Sanction`, `lunixi.Insurance` |
| Abonelik, KYC ve kimlik sinyalleri, pazaryeri alt bayileri ve bölünmüş çekimler | `lunixi.Subscriptions`, `lunixi.Kyc`, `lunixi.Identity`, `lunixi.Marketplace` |
| Diğer açık JSON ürünleri | Açık `SendAsync` çağrıları; tipli servis eşitliği iddiası yoktur |
| Diğer ikili indirmeler, multipart yüklemeler, tarayıcı checkout oturumu uçları | Bu JSON taşımasıyla uygulanmaz |

`SplitPaymentRequest`; belgelenmiş JSON DTO'larını, tam tutar toplamını, 1–5 bacağı, kart/sağlayıcı tekilliğini, yöntem alanlarının biçimini, taksit teklifini ve 3DS dönüşünü denetler. Kayıtlı kart token'ı kullanın. Kart dışı bir bacak `SplitPaymentFields.LegBlocks` bloklarından birini taşır (`mobileBilling`, `wallet`, `promissoryNote`); yemek kartı, açık bankacılık, alışveriş kredisi ve Para Transferi parçalı ödeme bacağı olamaz. `SplitPaymentEvents` parçalı ödeme webhook'larını listeler; bacak olaylarını da almak için uç noktaya `SplitPaymentEvents.LegEventsSubscription` ekleyin. Devamda aynı gövde/anahtarı; alıcı aksiyonlarında `ConfirmActionAsync` ve `ResendActionAsync` kullanın. Belgelenmiş devam eden HTTP 400 durumları döner; başarısız/mutabakat durumları hata kalır.

> 
  Kaynakta parçalı ödeme desteği bulunması, uçların canlıya alındığı veya merchant için açıldığı anlamına gelmez. Kullanılabilirlik gateway dağıtımı ve yeteneklerine bağlıdır. Bekleyen ve mutabakat gerektiren durumlar teslimat onayı değildir.


```csharp
var plans = await lunixi.Subscriptions.Plans.ListAsync(new Dictionary<string, object?> { ["pageSize"] = 25 });
var decision = await lunixi.Fraud.EvaluateAsync(new FraudEvaluateRequest(new
{
    transactionRef = "ORDER-1001", subjectType = "CUSTOMER", subjectId = "customer-1",
}));
```

Genel taşıma varsayılan olarak imzalar; yapılandırılan kökte `/api/v1/` altındaki (ve `/api/panel/v1/subscriptions/portal/` altındaki abonelik portalı rotalarındaki) açık JSON rotalarını alır. Ürünün belgelenmiş DTO, kapsam ve işlem anahtarı kurallarına uyun. Tipli istemcilerin dışındaki ikili indirmeler, multipart yükleme ve tarayıcı checkout oturum uçları bu taşımanın dışındadır. → [SDK olmadan REST](/sdks/rest)

## Herhangi bir yöntemle ödeme

`ListMethodsAsync("MOBILE_BILLING")` listeyi tek yönteme daraltır (`QR`, `QR_CODE` olarak gönderilir). Her yöntem sağlayıcılarını ve gönderilecek `inputFields` alanlarını listeler; sağlayıcı kodunu her zaman bu yanıttan okuyun.

`DirectMethodPaymentAsync` açık olan herhangi bir yöntemle ödeme başlatır. Gövdeyi `DirectMethodPaymentRequest` ile kurun; idempotency anahtarı zorunludur.

```csharp
var request = new DirectMethodPaymentRequest("10000", "TRY", "ORDER-1001")
    .WithMethodFields(PaymentMethods.MobileBilling, providerCode, new { gsmNumber = "+905321234567" })
    .WithCallbackUrl(new Uri("https://shop.example/payments/return"));
var started = await lunixi.Payments.DirectMethodPaymentAsync(request, persistedPaymentKey);
// HTTP 202 with data.action: OTP → ConfirmActionAsync(paymentId, actionId, new { otp = "123456" })
```

SDK yalnız seçilen yöntemin bloğunu gönderir (`PaymentMethodFields.MethodBlocks`), kart alanlarını (`card`, `installment`, `installmentQuoteToken`, `force3D`) `WithCard` için ayırır, blok yapısını denetler (en fazla 32 alan; metin, sayı, boolean, `ADDRESS` nesneleri ve `GROUP` listeleri) ve kural ihlalinde istek göndermeden `LunixiConfigurationException` fırlatır. Alışveriş kredisi 1–100 `BasketItems` ister. Ham JSON gövdesi `object` overload'una verilebilir ve aynı denetimlerden geçer.

**Para Transferi.** `WithMethodFields(PaymentMethods.BankTransfer, null, new { bankCode = "0062" })` `bankTransfer: { bankCode }` gönderir, sağlayıcı kodu asla göndermez; `BankTransferValues.OtherBanksCode` (`"9999"`) "Diğer bankalar" seçeneğidir. Yanıt bir `DISPLAY` adımı taşır; `BankTransferInstruction.FromResponse(started)` alıcıya gösterilecek hesabı, tutarı ve ödeme kodunu döndürür (böyle bir adım yoksa null). Sonuç `payment.captured` / `payment.failed` olarak gelir; `PaymentBankTransferEvents` transfer olaylarını listeler. Geç, eksik ve fazla gelen transferleri `ListBankTransferExceptionsAsync(new BankTransferExceptionFilters { Status = BankTransferExceptionStatuses.PendingDecision })` ile listeleyin; `AcceptBankTransferExceptionAsync` veya `MarkBankTransferExceptionRefundedAsync` ile karara bağlayın.

**Senetle Ödeme.** Planı `WithPromissoryNoteSchedule([new("5000", "2027-01-15"), new("5000", "2027-02-15")])` (vade sırasında, toplamı tutara eşit 1–24 satır) veya `WithPromissoryNoteCount(2)` ile verin, ikisini birden değil. Senetleri `GetPromissoryNotesAsync` ile okuyun, `CancelPromissoryNotesAsync` ile iptal edin ve yazdırılabilir PDF'i `DownloadPromissoryNoteDocumentAsync(paymentId, PromissoryNoteDocumentLocales.Turkish)` ile indirin; bu çağrı `LunixiBinaryResponse` (`Content`, `ContentType`) döndürür. `PromissoryNoteEvents` senet webhook'larını listeler.

**Adresler.** `ADDRESS` alanları kodlarını `lunixi.Reference.ListProvincesAsync()`, `ListDistrictsAsync("06")` ve `ListNeighborhoodsAsync(districtCode)` çağrılarından alır.

## Kartlar, taksitler ve pazaryeri intent'leri

```csharp
var bin = await lunixi.Payments.BinInfoAsync("45438712");
var table = await lunixi.Payments.InstallmentOptionsAsync(new InstallmentOptionsRequest("10000", "TRY")
{
    BinOrPan = "45438712", Installment = 3, EnabledInstallments = [1, 2, 3],
});
var taxonomy = await lunixi.Payments.CardTaxonomyAsync();
```

`InstallmentOptionsAsync` taksit tablosunu döndürür; BIN ile birlikte 1'den büyük taksit sayısında `DirectMethodPaymentRequest.WithInstallment(count, quoteToken)` için gereken `quoteToken` değerini de döndürür. `EnabledInstallments` intent kuralını izler (1–36, tekrarsız, 1 dahil, artan sırada gönderilir). BIN veya PAN öneki 6–19 hane olmalıdır ve hiçbir hata mesajına konmaz.

Saklı kartlar: `StoreCardAsync(new StoreCardRequest(card, cardUserKey, callbackUrl), persistedKey)` (imzalı, idempotency anahtarı zorunlu) `storedCardToken` değerini döndürür; `ListStoredCardsAsync(cardUserKey)`, `GetStoredCardAsync(storedCardToken)` ve `DeactivateStoredCardAsync(storedCardToken, persistedKey)` (imzalı, anahtar zorunlu) kartları yönetir. `StoreCardRequest.ToString()` kart bilgisini gizler.

`CreateMarketplaceIntentAsync(intent, [new MarketplaceSeller("dealer-a", "10000"), new MarketplaceSeller("dealer-b", "5000")], persistedKey, MarketplaceSellerPolicies.AllOrNothing)` barındırılan intent gövdesine `sellers[]` ekler; satıcıların `amountMinor` değerlerinin toplamı intent tutarına tam eşit olmalıdır, aksi hâlde SDK göndermeden önce hata fırlatır. Raporlar: `FailoverRecoveryAnalyticsAsync(filters)` ve `ProviderCredentialAnalyticsAsync(credentialId, filters)`.

## Ödeme linkleri

```csharp
var link = await lunixi.PaymentLinks.CreateAsync(
    CreatePaymentLinkRequest.Fixed(PaymentLinkUsages.SingleUse, "150000", "TRY", "Invoice 1001")
        .WithRecipient(new Dictionary<string, string> { ["email"] = "buyer@example.com" })
        .WithExpiresAt(DateTimeOffset.UtcNow.AddDays(7)),
    persistedLinkKey);
var linkId = link.Data.GetProperty("id").GetString()!;
await lunixi.PaymentLinks.SendAsync(linkId, PaymentLinkDeliveryChannels.Email, persistedSendKey);
```

Okumalar dahil her ödeme linki çağrısı imzalıdır; ortamı imzalayan anahtar belirler. `CreateAsync` ve `SendAsync` idempotency anahtarı ister (8–200 yazdırılabilir karakter). `UpdateAsync(linkId, expectedRowVersion, changes)` `{ expectedRowVersion, ...changes }` gönderir; `usage`, `amountMode`, `currency` ve bilinmeyen alanları göndermeden önce reddeder. Bir alanı null ile temizlemek için `JsonObject` verin. `PauseAsync`, `ResumeAsync`, `DeactivateAsync` (veya `ActionAsync`), `GetAsync`, `ListAsync`, `ListAttemptsAsync` ve `ListPaymentsAsync` geri kalanı kapsar; `QrAsync(linkId, PaymentLinkQrFormats.Png, 512)` QR görselini `LunixiBinaryResponse` olarak döndürür. `CreatePaymentLinkRequest.Open` ve `.Itemized` (`PaymentLinkItem` ile) diğer tutar modlarını kurar; `PaymentLinkCustomField` ödeme sayfasına soru ekler.

Link görselleri üç adımda yüklenir; `UploadImageAsync` adımları birlikte çalıştırır:

```csharp
PaymentLinkImage image = await lunixi.PaymentLinks.UploadImageAsync("logo.png", PaymentLinkImageContentTypes.Png, bytes);
// use image.AssetId as WithImageAssetId(...) of a link or an item
```

`CreateImageUploadAsync` önceden imzalı bir depolama hedefi döndürür; SDK baytları oraya **yalnız** dönen yükleme başlıklarıyla PUT eder (erişim token'ı, anahtar kimliği veya istek imzası asla gönderilmez); `ConfirmImageUploadAsync`, gateway saklanan baytları denetledikten sonra görseli yayımlar (PNG, JPEG veya WebP, en fazla 2 MiB). Reddedilen PUT `LunixiStorageUploadException` fırlatır (ör. adresin süresi dolduktan sonra `Code = "AccessDenied"`) ve yeniden denenmez; yeni bir hedef için yüklemeyi baştan yapın. `PaymentLinkImageUpload.UploadUrl` değerini süresi dolana kadar bir kimlik bilgisi gibi koruyun.

## Fiziki POS

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

`lunixi.PhysicalPos` kart terminallerinde ödeme başlatır ve ödemeleri okur; `lunixi.PhysicalPosManagement` mağazaları, kasaları, terminalleri, cihazları, cihaz anahtarlarını ve gün sonu politikasını yönetir. Her çağrı imzalıdır ve ortamı anahtar belirler (TEST anahtarı yalnız okuyabilir). Ödeme yazımı asenkrondur: sonucu `physical_pos.payment.*` webhook'larıyla veya `GetPaymentAsync` ile izleyin. `CreatePaymentAsync` ve `CreateReversalAsync` sakladığınız `Idempotency-Key` değerini ister (8–200 yazdırılabilir ASCII karakter, verildiği gibi gönderilir) ve `PhysicalPosWriteResult` döndürür: yeni ödeme için 202, aynı anahtar ve gövde için `Replayed` ile 200. SDK ödeme yazımını aynı anahtar ve gövdeyle kendisi yeniden dener; herhangi bir deneme yanıt almadıysa, ödeme içermeyen 2xx ya da "başlamadı" demeyen 5xx geldiyse çağrı `PhysicalPosOutcomeUnknownException` fırlatır. İptal, gün sonu başlatma ve anahtarlı yönetim oluşturma çağrıları anahtarınızı kabul eder ya da bir UUID üretir; kullanılan anahtar `IdempotencyKey` olarak döner.

Gövdeler anonim nesne, dictionary veya `JsonObject` değerleridir (null gönderip null olabilen bir alanı temizlemek için `JsonObject` veya dictionary kullanın); para alanları küçük birim string'idir, adetler sayıdır. Filtreler dictionary'dir: `ListPaymentsAsync(new Dictionary<string, object?> { ["limit"] = 50, ["createdFrom"] = DateTimeOffset.UtcNow.AddDays(-1) })`; raporların `dateFrom` / `dateTo` filtreleri `DateOnly` kabul eder. Bilinmeyen alan veya filtre, yanlış JSON tipi ve kanonik olmayan tutar göndermeden önce `LunixiConfigurationException` fırlatır. Kart verisi asla gönderilmez: kart cihazda okunur; kart verisi alan adı ya da `merchantReference`, kalem adı veya vergi grubunda, `merchantReference` / `q` filtresinde kart numarası `PhysicalPosCardDataRejectedException` fırlatır ve yalnız alan yolunu verir. 429 yanıtı `PhysicalPosRateLimitException`, gün sonundan sonra iptal `PhysicalPosVoidAfterSettlementException` olur (`REFUND` kullanın). Listeler `PhysicalPosPage` döndürür (`Items`, `HasMore`, `NextCursor`, `includeTotal` ile `TotalCount`); `Iterate…Async` her sayfayı dolaşır.

```csharp
PhysicalPosWebhookEvent pos = PhysicalPosClient.ParseWebhookEvent(rawRequestBytes, requestHeaders, webhookSecret);
// pos.Category: payment, fiscal_document, settlement or other; deduplicate by pos.Id and pos.HistorySeq per payment.
```

`ParseWebhookEvent` yükü okumadan önce imzayı doğrular, para alanlarını denetler ve `Original` (ters işlemin asıl ödemesi ve kaynağı), `Cashier` (yalnız cihaz kasiyer oturumundan başlatılan ödemede dolu), `IsDeviceSale` ve `IsSettlementEvent` değerlerini sunar; gün sonu olayları `HistorySeq` taşımaz.

## Cüzdan

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

`lunixi.Wallet` cüzdanın üye işyeri rotalarını alt istemcilerle kapsar: `Programs`, `EndUsers`, `Wallets`, `Deposits`, `Corporate`, `Fees`, `Transfers`, `Withdrawals`, `Operations`, `Topups`, `Payments`, `Qr` ve `BulkPayouts`, ayrıca `PingAsync`. Okumalar dahil her çağrı imzalıdır. Her çağrı, hiçbir şey gönderilmeden önce `WalletRoutes` ile denetlenir (metot, yol, anahtar kuralı, gövde ve sorgu alanları): bilinmeyen alan veya filtre, eksik zorunlu alan, küçük birim rakam string'i olmayan tutar (`"12550"` = 125,50 TRY; sayılar ve ondalıklar reddedilir, asla yuvarlanmaz) ya da anahtar isteyen bir rotada eksik `Idempotency-Key` `LunixiConfigurationException` fırlatır. Para iki adımda hareket eder: `Initiate…` limiti ayırır ve `operationId`, `integrityHash` ve `nonce` döndürür; `Complete…` kesinleştirir ve `operationId` değerini gövdeye kendisi ekler. Metotlar zarfın `data` alanını `JsonElement` olarak döndürür; listeler `LunixiPage` (`Items`, `HasMore`, `NextCursor`) döndürür, `IterateAsync` her sayfayı dolaşır. `Qr.ImageAsync(qrId, WalletQrImageFormats.Png, 8)` görseli `LunixiBinaryResponse` olarak döndürür.

## Banka hesapları, transferler ve döviz

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

`lunixi.BankConnect` bankaları, bağlantıları, hesapları, bakiyeleri ve hesap hareketlerini okur (`List…Async`, `Iterate…Async`, `Get…Async`) ve `RequestSyncAsync` ile yeni bir okuma ister (HTTP 202). Her çağrı imzalıdır ve ortamı anahtar belirler; banka bağlama yalnız Lunixi panelinden yapılır. Filtreler göndermeden önce denetlenir (`BankConnectFilters`): bilinmeyen filtreler, kümesi dışındaki değerler, 1–100 dışındaki `limit`, 100'den fazla hesap kimliği, kanonik olmayan tutarlar, `maxAmountMinor` değerinden büyük `minAmountMinor`, `bookedFrom` olmadan `bookedTo` ve 92 günden geniş aralıklar `LunixiConfigurationException` fırlatır. Durum ve tür filtreleri tek değer veya liste alır.

`Transfers` (`ListPurposesAsync`, `CreateAsync`, `ListAsync`, `IterateAsync`, `GetAsync`) ve `Fx` (`ListRatesAsync`, `CreateQuoteAsync`, `CreateOrderAsync`, `ListOrdersAsync`, `IterateOrdersAsync`, `GetOrderAsync`) `bank-connect:transfer` kapsamını ve IP izin listesi olan bir anahtarı ister. `CreateAsync` ve `CreateOrderAsync` verdiğiniz `Idempotency-Key` değerini denetler (8–200 yazdırılabilir ASCII karakter) ya da vermediğinizde bir UUID üretir; ağ hatasını veya 5xx yanıtı aynı anahtarla yeniden dener ve kullanılan anahtar (`IdempotencyKey`), `Replayed` ve kayıtla (`Entity`) `BankConnectWriteResult` döndürür. Süreçler arasında güvenle yeniden denemek için ödeme kimliğiniz gibi kendi anahtarınızı verin. Gövdeler `BankTransferRoutes` ile denetlenir; döviz teklifi `sourceAmountMinor` ve `targetAmountMinor` alanlarından tam olarak birini ve iki farklı hesap alır. Doğrulanmış webhook'ları `BankAccountTransactionReceivedData.FromWebhook` (`BankConnectEvents`) ve `BankTransferWebhookData.FromWebhook` (`BankTransferEvents`, `bank_transfer.*`; ödemenin `payment.bank_transfer.*` olaylarından ayrıdır) ile okuyun. Döviz emirlerinin webhook'u yoktur.

## Yaptırım taraması

```csharp
var result = await lunixi.Sanction.ScreenNameAsync(new
{
    name = "Jane Doe", subjectType = SanctionSubjectTypes.Person, mode = SanctionScreenModes.SanctionsPep,
});
if (!SanctionClient.IsClearScreening(result)) { /* review the matches; the decision is yours */ }
```

`lunixi.Sanction` isimleri, cüzdanları, hesapları, işlemleri, kamu ihalesi yasaklarını ve olumsuz medyayı tarar; davranışsal uyarıları değerlendirir, kişi ve kurumları sürekli izlemeye kaydeder; uyarıları, eşleşmeleri, izlenen varlıkları, veri seti güncelliğini, varlık istihbaratını ve engelli listenizi okur. Çağrılar yalnız erişim token'ını taşır (istek başına imza yoktur) ve `sanction:view_results` yetkisini ister. Gövdeler ve filtreler göndermeden önce `SanctionRoutes` ile denetlenir; iç içe `identifiers`, `parties` ve `accounts` listeleri, `anyOf` grupları ve değer kümeleri (`SanctionRoutes.ValueSets`) dahildir. Tarama karar değil eşleşme döndürür: `IsClearScreening` yalnız hiçbir eşleşme veya isabet yoksa ve yanıt kapsam bildiriyorsa `coverageStatus` değeri `COMPLETE` olduğunda true döner. Çalıştırılamayan tarama HTTP 503'tür ve hata fırlatır; `status: "success"` olmayan 2xx yanıt `SanctionScreeningException` fırlatır.

## Sigorta

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

`lunixi.Insurance` ürünleri listeler ve arar, referans kodlarını okur, teklif fiyatlar, sipariş oluşturur ve okur, bir Lunixi ödemesini bağlar ya da Lunixi dışında tahsil edilen primi kaydeder, poliçe talep eder, iptal eder ve iade eder, potansiyel müşteri gönderir ve okur, mutabakatı okur. Her çağrı imzalıdır. `CreateOrderAsync`, `ConfirmPaymentAsync`, `IssuePolicyAsync`, `CancelOrderAsync`, `RefundPremiumAsync` ve `CreateLeadAsync` bir `Idempotency-Key` ister (8–200 yazdırılabilir ASCII karakter); `CreateQuoteAsync` ve `BindPaymentAsync` isteğe bağlı anahtar alır. Üst düzey alanlar ve filtreler göndermeden önce denetlenir (`InsuranceFields`); teklifin sepeti ya `paymentIntentId` ya da `context.items` olur, ikisi birden olmaz. Bir sigorta türünün kendi nesnesine ait alan kuralları gateway'e bırakılır; gateway bütün alan hatalarını tek seferde 422 `insurance.validation.invalid` ile döndürür. `GetPolicyDocumentAsync`, `GetPolicyReceiptAsync` ve `GetPolicyCertificateAsync` PDF'i `LunixiBinaryResponse` olarak döndürür; `ExportReconciliationAsync` CSV'yi son satırı okunmuş olarak döndürür (`Truncated`, `NextCursor`, `Aborted`). Doğrulanmış webhook'ları `InsuranceWebhookData.FromWebhook` (sipariş olayları) ve `InsuranceLeadWebhookData.FromWebhook` (`insurance.lead.submitted`) ile okuyun.

## Abonelik

```csharp
var customer = await lunixi.Subscriptions.Customers.UpsertAsync(new { externalId = "user-42", email = "ada@example.com" }, persistedCustomerKey);
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

`lunixi.Subscriptions` abonelik oluşturur, okur, listeler, iptal eder, duraklatır, sürdürür ve planını değiştirir; ilgili kaynakları alt istemcilerde toplar: `Plans`, `Customers`, `Cards` (checkout SDK'sının `paymentMethodToken` değeriyle kaydedilir; kart numarası alan adından tanınıp reddedilir), `Invoices`, `Portal`, `Products`, `Features`, `Addons` ve `Usage`. Çağrılar yalnız erişim token'ını taşır (istek başına imza yoktur). `CreateAsync` bir `Idempotency-Key` ister ve onu gövdede de gönderir; plan, müşteri, kart, plan değişikliği ve tahsilat yeniden denemesi yazımları isteğe bağlı anahtar alır. Her çağrı göndermeden önce `SubscriptionRoutes` ile denetlenir (bilinmeyen alanlar ve filtreler, eksik zorunlu alanlar, `SubscriptionCollectionMethods` gibi değer kümeleri, planın `billing` kuralı ve desteklenen para birimleri). Tutarlar kanonik küçük birim string değerleridir (`"0"` ücretsiz plandır); gövdelerde, filtrelerde ve yanıtlarda denetlenir. Listeler `LunixiPage` döndürür; abonelik, plan, müşteri ve fatura listeleri `pageToken` ile sayfalanır, `IterateAsync` bu değeri geri gönderir. Gömülü portal `Portal.CreateSdkSessionAsync(customerId, origin)` ile başlar; `Portal.CreatePortalLinkAsync` ile alınan barındırılan portal linki `SubscriptionPortalLinks.HostedUrl(link)` adresinde açılır. Webhook olay adları `SubscriptionEvents` içindedir.

## KYC ve kimlik sinyalleri

```csharp
var created = await lunixi.Kyc.CreateSessionAsync(new
{
    subjectId = "user-42", sessionType = KycSessionTypes.Kyc, externalCustomerId = "user-42", person = new { firstName = "Ada" },
}, persistedSessionKey);

var links = await lunixi.Identity.AccountLinksAsync(traceId);
if (IdentitySignals.IsDeviceShared(links)) { /* one device, several accounts: review */ }
```

`lunixi.Kyc` KYC, KYB ve kanıt oturumları oluşturur (`person` / `company`, `customerType` ile birlikte `customer` olarak; `metadata` ise `evidenceContextJson` olarak gönderilir), yetki bölgesini ve birincil belgeyi ayarlar, belgeleri sunucudan yükler, gönderir, manuel incelemeye yollar; zaman çizelgesini, KYC politikanızı, webhook teslimatlarını ve silme taleplerini okur. `ListSessionsAsync` yalnız `limit` ve `cursor` alır. Tarayıcıdaki çekim yayımlanabilir anahtarla çalışır; sonucun belirleyicisi `kyc.*` webhook'udur (`KycEvents`) ve `KycAssuranceLevels.Meets` ulaşılan güvence seviyesini istediğiniz seviyeyle karşılaştırır. `lunixi.Identity` tarayıcı parmak izi izlerini analiz eder (`AnalyzeSyncAsync`, `AnalyzeAsync`); hesap bağlantılarını, oturumları, ilişkili oturumları, zaman çizelgelerini ve öne çıkan kaynakları okur. İzin ver / engelle kararı değil, sinyal döndürür. İki yüzey de yalnız erişim token'ı kullanır, `KycRoutes` / `IdentityRoutes` ile denetlenir ve hiçbir hata mesajına belge görseli veya alan değeri koymaz.

## Pazaryeri alt bayileri

```csharp
MarketplaceSeller[] sellers = [new("dealer-a", "60000"), new("dealer-b", "40000")];
var intent = await lunixi.Marketplace.CreateCheckoutIntentAsync(
    new CreateIntentRequest("100000", "TRY", "ORDER-7"), sellers, MarketplaceSellerPolicies.AllOrNothing, persistedIntentKey);

var dealer = await lunixi.Marketplace.CreateDealerAsync(new { legalName = "Acme Ltd", dealerType = MarketplaceDealerTypes.LegalEntity });
var link = await lunixi.Marketplace.OnboardingTokenAsync(dealer.GetProperty("id").GetString()!);
var onboardingUrl = MarketplaceDealerOnboarding.Url(link);
```

`lunixi.Marketplace` alt bayiler arasında bölünmüş çekimler gönderir; bayi yaşam döngüsünü (oluşturma, toplu oluşturma, listeleme, durum, katılım linki, başvuru, koşullar, sözleşme, aktivasyon, KYC sonucu), komisyonları (kurallar, ön izleme, hesaplama), hakediş ödemelerini (oluşturma, ödendi işaretleme, toplu ödendi işaretleme), hakediş defterini, işlemleri ve hash zincirli denetim kaydını yürütür. Bölünmüş çekimler (`CreateCheckoutIntentAsync`, `ChargeMarketplaceCard2dAsync`, `ChargeMarketplaceCard3dAsync`, `ChargeMarketplaceCardAsync`) istek başına imzalıdır ve isteğe bağlı `Idempotency-Key` alır; diğer rotalar yalnız erişim token'ı kullanır. Satıcı parçalarının toplamı `amount` değerine eşit olmalı, kart çekimi ise ticari bağlamını (`MarketplaceRoutes.ChargeRequiredFields`) taşımalıdır; aksi hâlde hiçbir şey gönderilmez. Kart verisi verildiği gibi gönderilir ve hiçbir hata mesajına konmaz; doğrudan kart çekimi PCI DSS SAQ-D kapsamına girdiğinden barındırılan checkout intent'ini tercih edin. Token veya form içermeyen intent yanıtı `MarketplaceCheckoutException` fırlatır.

## Ham webhook ve kalıcı idempotency

```csharp
var verifier = new WebhookVerifier(webhookSecret);
var webhook = verifier.Verify(rawRequestBytes, requestHeaders);
// Tek DB işleminde: webhook.Id tekilliği, ödeme/sipariş/tutar/para birimi denetimi, sipariş güncelleme.
```

JSON middleware'den önce birebir ham baytları okuyun. v2 doğrulayıcı; `eventId.timestamp.rawBody` HMAC'ini, rotasyondaki imzaları sabit zamanda, ±300 saniye sınırını, zarf/başlık kimliğini ve olay türünü denetler; tekrarlı kritik başlıkları reddeder. Testte güncelliği kapatmak yerine test saati verin. → [Webhook'lar](/getting-started/webhooks)

İş alanınız ödeme/sipariş referansı, tutar, para birimi, merchant bağlamı ve ödeme yöntemini eşleştirmelidir. Provizyon tahsilat değildir; parçalı bacaklar, sigorta durumu ve senet düzenlenmesi ayrı kural ister. `WebhookProcessor` için `IWebhookReplayStore` arayüzünü kalıcı atomik rezervasyon, gövde özeti çakışması, kira süresi ve sahiplik belirteciyle uygulayın. Tamamlanan kopyalar atlanır; dolu kiralar yeniden denenebilir hata alır. İş alanı yazmaları da idempotent olmalıdır. Inbox tamamlanması ve sipariş güncellemesini aynı işlemde tutun. Olayları en az yedi gün; eski manuel tekrarlar için iş alanı kayıtlarını daha uzun saklayın.

## ASP.NET Core örneği ve hatalar

`samples/AspNetCore`; gerçek SQLite, sunucuda fiyatlanmış sipariş, kimliği doğrulanan checkout, kalıcı deneme anahtarları ve atomik inbox/sipariş işlemi içerir. Örnek README'sindeki sunucu ayarlarından sonra:

```bash
dotnet run --project samples/AspNetCore/AspNetCore.csproj --urls http://127.0.0.1:5080
```

`LUNIXI_MERCHANT_ID` değerini sunucu kimlik bilgileriyle birlikte tanımlayın. Webhook yalnız yerelde kayıtlı ödemeyi bulur; veritabanı işlemi tamamlanmadan önce kimliği doğrulanmış GET yanıtında merchant/ödeme/sipariş, para birimi, tam tutar ve çekim/iade bakiyeleri eşleşmelidir.

Dağıtımda HTTPS kullanın. Örnek backend token'ını gerçek sipariş yetkilendirmenizle değiştirin. Örnek; parçalı ödeme/senet/düzeltilmiş tutar tahsilatlarını mutabakata bırakır. Canlı kullanım öncesi taksit, sigorta, kısmi çekim, iade, itiraz ve mutabakat politikanızı ekleyin. Tarayıcı dönüşü siparişi ödenmiş yapamaz.

`LunixiValidationException` (400/422), `LunixiRateLimitException` (429), `LunixiApiException` ve `LunixiTransportException` yakalanabilir. Hatalar `Code`, `RequestId`, `RetryAfterSeconds` taşır; mesajlar sağlayıcı gövdesini veya sırları kopyalamaz. Tam yanıtları loglamayın. Hız sınırı otomatik tekrar edilmez.

Zaman aşımı veya iptal, yazmanın sonucunu belirsiz bırakabilir. Durumu uzlaştırın; kaydedilmiş ilk anahtarı kullanın. Normal gateway idempotency süresi 24 saattir; süre bitince körlemesine tekrar göndermeyin. Örnek 23 saatten sonra mutabakat ister. Tekrarlar aynı gövde/anahtarı ve yeni imza nonce'unu kullanır.

`dotnet test tests/Lunixi.Sdk.Tests/Lunixi.Sdk.Tests.csproj -c Release` çalıştırın. Çevrimdışı HTTP, Node.js imza/kanonik JSON vektörü, ortak webhook vektörleri, gerçek ASP.NET Core TestServer ve kalıcı SQLite testleri vardır; canlı ödeme yapılmaz. → [Test](/getting-started/testing), [Hata yönetimi](/getting-started/errors)

## Mağaza içi entegrasyon örneği

Çalışan ASP.NET Core örneğinde yetkili `POST /checkout/{orderId}/form` varsayılan olarak gömülü HTML üretir; `?mode=hosted` doğrulanmış backend adresini kullanır. Mevcut `/checkout/{orderId}` JSON sözleşmesi korunur. Renderer ve adres doğrulayıcısı `Lunixi.AspNetCoreSample.CheckoutPresentation` içindedir. `DEMO_API_TOKEN` yalnız sunucudan sunucuya kullanılır; tarayıcı JavaScript’ine vermeyin. Mağaza checkout controller’ı bu yanıtı sunmadan önce müşteri oturumunu, sipariş sahipliğini ve yerel CSRF’yi doğrulamalıdır. Örnek müşteri girişi veya üretim tenant yetkilendirmesi uygulamaz.
