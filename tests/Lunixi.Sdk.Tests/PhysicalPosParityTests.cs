using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lunixi.Sdk.Tests;

/// <summary>
/// Cross-SDK parity: the shared fixture <c>physical-pos-parity.json</c> (byte-identical copies in the Node, PHP, Java, Python
/// and .NET SDKs) carries the gateway's card-data rules, card-number value cases, key and path rules, the closed field and
/// filter lists, the "not started" codes and the webhook cases.
/// </summary>
public class PhysicalPosParityTests
{
    private static readonly JsonElement Fixture = Load();

    private static JsonElement Load()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/physical-pos-parity.json")));
        return document.RootElement.Clone();
    }

    private static IEnumerable<JsonElement> Rows(string name)
    {
        var rows = Fixture.GetProperty(name).EnumerateArray().ToArray();
        Assert.NotEmpty(rows);
        return rows;
    }

    private static string[] Strings(JsonElement list) => list.EnumerateArray().Select(e => e.GetString()!).ToArray();

    [Fact]
    public void CardDataKeyCasesMatchTheGatewayTokenRule()
    {
        foreach (var row in Rows("cardDataKeyCases"))
        {
            var key = row.GetProperty("key").GetString()!;
            Assert.True(row.GetProperty("cardData").GetBoolean() == PhysicalPosCardData.IsCardDataKey(key), key);
        }
    }

    [Fact]
    public void CardDataPathCasesMatchTheGatewayGuard()
    {
        foreach (var row in Rows("cardDataPathCases"))
        {
            var expected = row.GetProperty("field").ValueKind == JsonValueKind.Null ? null : row.GetProperty("field").GetString();
            Assert.Equal(expected, PhysicalPosCardData.FindCardDataField(row.GetProperty("body")));
        }
    }

    [Fact]
    public void PanValueCasesMatchTheServiceScan()
    {
        foreach (var row in Rows("panValueCases"))
        {
            var value = row.GetProperty("value").GetString()!;
            var mode = row.GetProperty("mode").GetString();
            Assert.True(row.GetProperty("pan").GetBoolean() == PhysicalPosCardData.ContainsPanLikeValue(value, mode == "ITEM_TEXT"),
                $"mode {mode}, length {value.Length}");
        }
    }

    [Fact]
    public void CardDataTermListsMatchTheFixture()
    {
        var rule = Fixture.GetProperty("cardDataKeyRule");
        Assert.Equal(Strings(rule.GetProperty("numberTerms")), PhysicalPosCardData.NumberTerms);
        Assert.Equal(Strings(rule.GetProperty("secretTerms")), PhysicalPosCardData.SecretTerms);
        Assert.Equal(Strings(rule.GetProperty("suffixTerms")), PhysicalPosCardData.SuffixTerms);
        Assert.Equal(Strings(rule.GetProperty("wholeKeyTerms")), PhysicalPosCardData.WholeKeyTerms);
        Assert.Equal(Strings(rule.GetProperty("maskTokens")), PhysicalPosCardData.MaskTokens);
        foreach (var term in PhysicalPosCardData.NumberTerms.Concat(PhysicalPosCardData.SecretTerms).Concat(PhysicalPosCardData.SuffixTerms))
            Assert.True(PhysicalPosCardData.IsCardDataKey(term), term);
        foreach (var term in PhysicalPosCardData.WholeKeyTerms)
        {
            Assert.True(PhysicalPosCardData.IsCardDataKey(term), term);
            Assert.False(PhysicalPosCardData.IsCardDataKey(term + "Url"), term + "Url");
        }
        foreach (var term in PhysicalPosCardData.NumberTerms) Assert.False(PhysicalPosCardData.IsCardDataKey("masked_" + term), "masked_" + term);
        foreach (var term in PhysicalPosCardData.SecretTerms) Assert.True(PhysicalPosCardData.IsCardDataKey("masked_" + term), "masked_" + term);
    }

    [Fact]
    public async Task IdempotencyKeyCasesAreCheckedExactlyAsGiven()
    {
        foreach (var row in Rows("idempotencyKeyCases"))
        {
            var key = row.GetProperty("key").GetString()!;
            var (client, requests) = RecordingClient.Create(_ => PhysicalPosTestSupport.Payment());
            using (client)
            {
                if (row.GetProperty("valid").GetBoolean())
                {
                    await client.PhysicalPos.CreatePaymentAsync(PhysicalPosTestSupport.Sale(), key);
                    await client.PhysicalPos.CancelPaymentAsync("pay_1", key);
                    Assert.All(requests, request => Assert.Equal(key, request.IdempotencyKey));
                }
                else
                {
                    await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PhysicalPos.CreatePaymentAsync(PhysicalPosTestSupport.Sale(), key));
                    await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PhysicalPos.CancelPaymentAsync("pay_1", key));
                    await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PhysicalPosManagement.CreateStoreAsync(new { code = "S1", name = "Store" }, key));
                    Assert.Empty(requests);
                }
            }
        }
    }

    [Fact]
    public async Task PathIdCasesAreEncodedOrRefused()
    {
        foreach (var row in Rows("pathIdCases"))
        {
            var id = row.GetProperty("id").GetString()!;
            var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json("{\"data\":{\"payment\":{\"paymentId\":\"p\"}}}"));
            using (client)
            {
                if (row.GetProperty("valid").GetBoolean())
                {
                    var encoded = row.TryGetProperty("encoded", out var value) ? value.GetString()! : id;
                    await client.PhysicalPos.GetPaymentAsync(id);
                    Assert.Equal("/api/v1/physical-pos/payments/" + encoded, Assert.Single(requests).PathAndQuery);
                }
                else
                {
                    await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PhysicalPos.GetPaymentAsync(id));
                    await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PhysicalPosManagement.GetDeviceAsync(id));
                    Assert.Empty(requests);
                }
            }
        }
    }

    [Fact]
    public async Task ForbiddenFieldsAreRefusedBeforeSending()
    {
        var (client, requests) = RecordingClient.Create(_ => PhysicalPosTestSupport.Payment());
        using (client)
        {
            foreach (var field in Strings(Fixture.GetProperty("forbiddenFields")))
            {
                var request = PhysicalPosTestSupport.Sale();
                request[field] = "x";
                var error = await Record.ExceptionAsync(() => client.PhysicalPos.CreatePaymentAsync(request, PhysicalPosTestSupport.Key));
                Assert.True(error is LunixiConfigurationException or PhysicalPosCardDataRejectedException, field);
            }
            Assert.Empty(requests);
        }
    }

    [Fact]
    public void RequestFieldListsMatchTheFixture()
    {
        var shapes = new Dictionary<string, IReadOnlyList<string>>(PhysicalPosFields.Management, StringComparer.Ordinal)
        {
            ["createPayment"] = PhysicalPosFields.CreatePayment,
            ["paymentTarget"] = PhysicalPosFields.PaymentTarget,
            ["saleItem"] = PhysicalPosFields.SaleItem,
            ["createReversal"] = PhysicalPosFields.CreateReversal,
            ["refundItem"] = PhysicalPosFields.RefundItem,
        };
        var expected = Fixture.GetProperty("requestFields").EnumerateObject().ToDictionary(p => p.Name, p => Strings(p.Value));
        Assert.Equal(expected.Keys.Order(), shapes.Keys.Order());
        foreach (var (name, fields) in expected) Assert.True(fields.Order().SequenceEqual(shapes[name].Order()), name);
    }

    [Fact]
    public void FilterListsMatchTheFixture()
    {
        var expected = Fixture.GetProperty("filters").EnumerateObject().ToDictionary(p => p.Name, p => Strings(p.Value));
        Assert.Equal(expected.Keys.Order(), PhysicalPosFields.Filters.Keys.Order());
        foreach (var (route, filters) in expected) Assert.True(filters.Order().SequenceEqual(PhysicalPosFields.Filters[route].Order()), route);
    }

    [Fact]
    public void EventTypesAndNotStartedCodesMatchTheFixture()
    {
        Assert.Equal(Strings(Fixture.GetProperty("eventTypes")), PhysicalPosEvents.All);
        Assert.Equal(Strings(Fixture.GetProperty("notStartedCodes")).Order(), PhysicalPosValues.NotStartedCodes.Order());
    }

    [Fact]
    public void WebhookOriginalCasesMatchTheFixture()
    {
        foreach (var row in Rows("webhookOriginalCases"))
        {
            var webhook = PhysicalPosTestSupport.Parse(PhysicalPosEvents.PaymentApproved, DataOf(row));
            var expected = row.GetProperty("original");
            var actual = webhook.Original;
            if (expected.ValueKind == JsonValueKind.Null) Assert.True(actual is null, row.GetProperty("name").GetString());
            else Assert.Equal(new PhysicalPosOriginalPayment(expected.GetProperty("paymentId").GetString()!, expected.GetProperty("origin").GetString()!), actual);
        }
    }

    [Fact]
    public void WebhookCashierCasesMatchTheFixture()
    {
        foreach (var row in Rows("webhookCashierCases"))
        {
            var webhook = PhysicalPosTestSupport.Parse(PhysicalPosEvents.PaymentApproved, DataOf(row));
            var expected = row.GetProperty("cashier");
            if (expected.ValueKind == JsonValueKind.Null) Assert.True(webhook.Cashier is null, row.GetProperty("name").GetString());
            else Assert.Equal(new PhysicalPosCashier(expected.GetProperty("cashierId").GetString()!, expected.GetProperty("cashierCode").GetString()!), webhook.Cashier);
        }
    }

    [Fact]
    public void WebhookCategoryCasesMatchTheFixture()
    {
        foreach (var row in Rows("webhookCategoryCases"))
        {
            var type = row.GetProperty("type").GetString()!;
            var webhook = PhysicalPosTestSupport.Parse(type, new JsonObject { ["paymentId"] = "pay_1" });
            Assert.Equal(row.GetProperty("category").GetString(), webhook.Category);
            Assert.Equal(type.StartsWith("physical_pos.settlement.", StringComparison.Ordinal), webhook.IsSettlementEvent);
        }
    }

    /// <summary>The case's <c>data</c> plus a payment id, so an otherwise empty payload is still an event payload.</summary>
    private static JsonObject DataOf(JsonElement row)
    {
        var data = JsonNode.Parse(row.GetProperty("data").GetRawText())!.AsObject();
        data["paymentId"] = "pay_1";
        return data;
    }
}
