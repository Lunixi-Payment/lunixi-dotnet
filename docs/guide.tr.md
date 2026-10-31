# .NET / C# SDK

Güncel kaynak sürümü: **0.1.1**. NuGet yayını ayrı onaylı yayın adımıdır; kayıt deposunda yayımlandığı iddia edilmez.

Yeni kaynak yaması mağaza sitesi için somut bir sunum örneği ekler. **Varsayılan gömülü ödemedir**: yalnız resmi Lunixi bundle yüklenir ve mevcut form mount edilir; yöntem ve çoklu kart seçenekleri Lunixi panelinde kalır. İsteğe bağlı yönlendirme yalnız backend `paymentPageUrl` adresini, tam HTTPS `pay.lunixi.com/payment/` doğrulamasından sonra kullanır. Adresin token’ı gömülü token’dan farklı olabilir. Eksik adres gömülü ödemeyi engellemez; yönlendirme aynı deneme/anahtarla inceleme veya uzlaştırmada kalır. Tarayıcı token’ından adres üretmeyin. Formdan önce müşteri/sipariş sahipliğini ve yerel tarayıcı CSRF’yi doğrulayın; `Cache-Control: private, no-store` gönderin, mağazanın CSP nonce değerini koruyun; token, HTML ve yönlendirme adreslerini loglamayın. Genel yanıt verisi `paymentPageUrl` alanını korur; yeni tipli ürün eşitliği iddiası yoktur.

`Lunixi.Sdk` 0.1.1; Ed25519 imza, token önbelleği, ödeme formu intent'i, ödeme işlemleri, parçalı ödeme DTO doğrulaması, fraud istemcileri ve ham gövdeden webhook doğrulaması sağlar. .NET 8 ve üzerindeki ASP.NET Core, arka plan servisi ve diğer sunucu uygulamalarını destekler.

## Kurulum

Kaynak: [Lunixi-Payment/lunixi-dotnet](https://github.com/Lunixi-Payment/lunixi-dotnet). NuGet.org yayını ayrı bir sürüm adımıdır; kaynağın bulunması paketin kayıt sisteminde yayımlandığı anlamına gelmez. SDK klasöründe:

```bash
dotnet pack src/Lunixi.Sdk/Lunixi.Sdk.csproj -c Release -o artifacts
dotnet add /path/to/YourApp.csproj package Lunixi.Sdk --version 0.1.1 --source /absolute/path/to/sdk/artifacts
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

var request = new CreateIntentRequest(10050, "TRY", "ORDER-1001")
{
    CallbackUrl = new Uri("https://merchant.example/payments/return"),
    BasketItems = [new BasketItem("SKU-1", "Örnek ürün", 10050)],
};
// Anahtarı kalıcı ödeme denemesinden okuyun; ağ hatasından sonra aynı anahtarı kullanın.
var response = await lunixi.Payments.CreateIntentAsync(request, persistedAttemptKey);
var paymentId = response.Data.GetProperty("paymentId").GetString();
var token = response.Data.GetProperty("token").GetString();
```

Uygulama başına tek istemci oluşturun; kapanışta dispose edin. TEST/LIVE ortamını anahtar belirler. Açık Ed25519 anahtarını panelde kaydedin; PKCS#8 PEM özel anahtarı yalnız sunucuda tutun. `Ed25519Signer.GenerateKeyPair()` ikisini de üretir. → [Kimlik doğrulama](/getting-started/authentication)

Tutarlar pozitif tam sayı alt birimdir: `10050` = 100,50 TRY. Tutarı sipariş veritabanından okuyun. Sepet `Price` değeri satırın tamamının tutarıdır; satır toplamı `Amount` ile eşit olmalıdır. Eski üst düzey tutar alanları reddedilir. Akış gerektiriyorsa alıcı/fatura/teslimat JSON bilgisini güvenilir sunucu kaydından ekleyin.

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
var captured = await lunixi.Payments.CaptureAsync(paymentId, persistedCaptureKey, amount: 10050);
var refunded = await lunixi.Payments.RefundAsync(paymentId, persistedRefundKey, amount: 1000);
var voided = await lunixi.Payments.VoidAsync(paymentId, persistedVoidKey);
var methods = await lunixi.Payments.ListMethodsAsync();
```

Ön provizyon için `CreateIntentRequest.Preauth = true` kullanın; sonra capture/void çağırın. `LunixiResponse`, `StatusCode`, tam `Envelope`, `Data` ve `RequestId` değerlerini; sayfalama bilgisi dahil korur. Bekleyen durum veya HTTP 202 kesin ödeme başarısı değildir.

| Yüzey | .NET 0.1.1 |
| --- | --- |
| Form intent'i, ödeme durum/liste/capture/refund/void/yöntemler | `lunixi.Payments` |
| Parçalı ödeme oluşturma/durum/liste/iade ve alıcı aksiyonları | `SplitPaymentRequest`, `lunixi.Payments` |
| Fraud kararı, işlem olayı ve karar kayıtları | `lunixi.Fraud` |
| Webhook doğrulama ve kalıcı tekrar koordinasyonu | `WebhookVerifier`, `IWebhookReplayStore`, `WebhookProcessor` |
| Ödeme linki, cüzdan, bank-connect, yaptırım, referans, sigorta ve diğer açık JSON ürünleri | Açık `SendAsync` çağrıları; tipli servis eşitliği iddiası yoktur |

`SplitPaymentRequest`; belgelenmiş JSON DTO'larını, tam tutar toplamını, 1–5 bacağı, kart/sağlayıcı tekilliğini, yöntem alanlarının biçimini, taksit teklifini ve 3DS dönüşünü denetler. Kayıtlı kart token'ı kullanın. Devamda aynı gövde/anahtarı; alıcı aksiyonlarında `ConfirmActionAsync` ve `ResendActionAsync` kullanın. Belgelenmiş devam eden HTTP 400 durumları döner; başarısız/mutabakat durumları hata kalır.

> 
  Kaynakta parçalı ödeme desteği bulunması, uçların canlıya alındığı veya merchant için açıldığı anlamına gelmez. Kullanılabilirlik gateway dağıtımı ve yeteneklerine bağlıdır. Bekleyen ve mutabakat gerektiren durumlar teslimat onayı değildir.


```csharp
var banks = await lunixi.SendAsync(HttpMethod.Get, "/api/v1/bank-connect/banks?limit=25");
var decision = await lunixi.Fraud.EvaluateAsync(new FraudEvaluateRequest(new
{
    transactionRef = "ORDER-1001", subjectType = "CUSTOMER", subjectId = "customer-1",
}));
```

Genel taşıma varsayılan olarak imzalar; yapılandırılan kökte `/api/v1/` altındaki açık JSON rotalarını alır. Ürünün belgelenmiş DTO, kapsam ve işlem anahtarı kurallarına uyun. PDF/görsel, multipart yükleme ve tarayıcı checkout oturum uçları bu taşımanın dışındadır. → [SDK olmadan REST](/sdks/rest)

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
