using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lunixi.Sdk.Tests;

/// <summary>Shared physical POS test data: a valid sale, gateway answers, scripted transports and signed webhooks.</summary>
internal static class PhysicalPosTestSupport
{
    public const string Key = "pos-order-1001-attempt";
    public const string WebhookSecret = "whsec_physical_pos_synthetic_0001";
    public static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-15T08:00:00Z");

    public static JsonObject Sale() => new()
    {
        ["operation"] = PhysicalPosPaymentOperations.Sale,
        ["target"] = new JsonObject { ["deviceId"] = "dev_1" },
        ["amountMinor"] = "4500",
        ["currency"] = "TRY",
        ["merchantReference"] = "ORDER-1001",
        ["allowDuplicateReference"] = false,
    };

    public static string PaymentBody(string paymentId = "pay_1", bool replayed = false, string status = "NOT_STARTED")
        => "{\"data\":{\"payment\":{\"paymentId\":\"" + paymentId + "\",\"paymentStatus\":\"" + status + "\",\"amountMinor\":\"4500\",\"currency\":\"TRY\"},\"replayed\":" +
           (replayed ? "true" : "false") + "}}";

    public static HttpResponseMessage Payment(HttpStatusCode code = HttpStatusCode.Accepted, bool replayed = false)
        => FakeHandler.Json(PaymentBody(replayed: replayed), code);

    public static HttpResponseMessage Error(HttpStatusCode code, string errorCode, string details = "{}")
        => FakeHandler.Json("{\"code\":\"" + errorCode + "\",\"message\":\"synthetic\",\"details\":" + details + "}", code);

    /// <summary>A client with <paramref name="retries"/> retries whose transport plays <paramref name="script"/> in order; the token call is answered apart.</summary>
    public static (LunixiClient Client, List<RecordedRequest> Requests) Scripted(int retries, params Func<HttpResponseMessage>[] script)
    {
        var requests = new List<RecordedRequest>();
        var index = 0;
        var client = new LunixiClient(TestOptions.Create(retries: retries), new FakeHandler(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v1/auth/token") return FakeHandler.Json("{\"access_token\":\"token\"}");
            var headers = request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
                .ToDictionary(h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase);
            var bytes = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(ct);
            requests.Add(new(request.Method.Method, request.RequestUri, bytes is null ? null : Encoding.UTF8.GetString(bytes), bytes, headers));
            return script[Math.Min(index++, script.Length - 1)]();
        }));
        return (client, requests);
    }

    public static Func<HttpResponseMessage> NetworkFailure => () => throw new HttpRequestException("synthetic connection reset");

    /// <summary>A signed webhook body and its headers, verified at <see cref="Now"/>.</summary>
    public static (byte[] Raw, Dictionary<string, string> Headers) Webhook(string type, JsonObject data, string id = "evt_1", JsonObject? extra = null)
    {
        var envelope = new JsonObject { ["id"] = id, ["type"] = type, ["environment"] = "LIVE", ["createdAt"] = "2026-11-15T07:59:58Z", ["data"] = data };
        foreach (var (name, value) in extra ?? new JsonObject()) envelope[name] = value?.DeepClone();
        var raw = Encoding.UTF8.GetBytes(envelope.ToJsonString());
        var time = Now.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var prefix = Encoding.UTF8.GetBytes(id + "." + time + ".");
        var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(WebhookSecret), prefix.Concat(raw).ToArray())).ToLowerInvariant();
        return (raw, new Dictionary<string, string>
        {
            ["X-Lunixi-Signature"] = "v2=" + signature,
            ["X-Lunixi-Signature-Timestamp"] = time,
            ["X-Lunixi-Event-Id"] = id,
        });
    }

    public static PhysicalPosWebhookEvent Parse(string type, JsonObject data, string id = "evt_1", JsonObject? extra = null)
    {
        var (raw, headers) = Webhook(type, data, id, extra);
        return PhysicalPosClient.ParseWebhookEvent(raw, headers, WebhookSecret, new TestClock(Now));
    }

    public static JsonElement Element(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
