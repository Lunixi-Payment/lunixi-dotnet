using System.Text.Json;

namespace Lunixi.Sdk.Tests;

/// <summary>Contract source: the PHP SDK's marketplace client and the Node SDK's table. Card data and names are synthetic.</summary>
public class MarketplaceClientTests
{
    private const string CardNumber = "5400000000000004";
    private const string IntentAnswer = "{\"success\":true,\"paymentId\":\"pi_1\",\"token\":\"tok_1\",\"checkoutFormContent\":\"<script></script>\"}";

    private static MarketplaceSeller[] Legs() =>
    [
        new("d_alice", "60000"),
        new("d_bob", "40000") { Scenario = JsonDocument.Parse("{\"type\":\"DYNAMIC_RATE\",\"ratePercent\":\"5\"}").RootElement },
    ];

    private static Dictionary<string, object?> ChargeRequest(string amount = "100000") => new()
    {
        ["amount"] = amount,
        ["currency"] = "TRY",
        ["orderId"] = "ORD-MP-1",
        ["card"] = new { cardHolderName = "Synthetic Holder", cardNumber = CardNumber, expireMonth = "12", expireYear = "28", cvcNumber = "987" },
        ["buyer"] = TestOrder.Buyer,
        ["billingAddress"] = TestOrder.Address,
        ["basketItems"] = TestOrder.Basket(amount),
    };

    [Fact]
    public void ChargesAreSignedAndDealerRoutesBearerOnly()
    {
        foreach (var route in MarketplaceRoutes.All.Values)
        {
            var charge = route.Path.StartsWith("/api/v1/payments/marketplace/", StringComparison.Ordinal);
            Assert.Equal(charge, route.StepUp);
            Assert.Equal(charge ? LunixiRoute.KeyOptional : LunixiRoute.KeyNone, route.IdempotencyKeyRule);
            if (!charge) Assert.StartsWith("/api/v1/marketplace/", route.Path);
        }
    }

    [Fact]
    public async Task CheckoutIntentSendsTheSplitSignedWithTheKey()
    {
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json(IntentAnswer, System.Net.HttpStatusCode.Created));
        using (client)
        {
            var response = await client.Marketplace.CreateCheckoutIntentAsync(TestOrder.Intent("100000", "TRY", "WC-7"), Legs(),
                MarketplaceSellerPolicies.AllOrNothing, "idem-mp-00001");
            Assert.Equal("tok_1", response.Envelope.GetProperty("token").GetString());
            var request = Assert.Single(requests);
            Assert.Equal(("POST", "/api/v1/payments/marketplace/intents"), (request.Method, request.PathAndQuery));
            Assert.True(request.Signed);
            Assert.Equal("idem-mp-00001", request.IdempotencyKey);
            Assert.StartsWith("{\"amount\":\"100000\",\"currency\":\"TRY\",\"orderId\":\"WC-7\",\"buyer\":{", request.Body);
            Assert.EndsWith(",\"sellers\":[{\"subDealerId\":\"d_alice\",\"amountMinor\":\"60000\"}," +
                "{\"subDealerId\":\"d_bob\",\"amountMinor\":\"40000\",\"scenario\":{\"type\":\"DYNAMIC_RATE\",\"ratePercent\":\"5\"}}],\"multiSellerPolicy\":\"ALL_OR_NOTHING\"}", request.Body);
        }
    }

    [Fact]
    public async Task CheckoutIntentWithoutATokenThrows()
    {
        var (client, _) = RecordingClient.Create(_ => FakeHandler.Json("{\"success\":false,\"code\":\"marketplace.payment.seller_not_eligible\",\"message\":\"seller not eligible\"}"));
        using (client)
        {
            var error = await Assert.ThrowsAsync<MarketplaceCheckoutException>(() =>
                client.Marketplace.CreateCheckoutIntentAsync(TestOrder.Intent("1000", "TRY", "o1"), [new MarketplaceSeller("d_1", "1000")]));
            Assert.Equal("marketplace.payment.seller_not_eligible", error.Code);
            Assert.DoesNotContain("seller not eligible", error.Message);
        }
    }

    [Fact]
    public async Task SellerLegsAreCheckedBeforeSending()
    {
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json(IntentAnswer));
        using (client)
        {
            var marketplace = client.Marketplace;
            var intent = TestOrder.Intent("1000", "TRY", "o1");
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => marketplace.CreateCheckoutIntentAsync(intent, []));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => marketplace.CreateCheckoutIntentAsync(intent, [new MarketplaceSeller("d_1", "999")]));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() =>
                marketplace.CreateCheckoutIntentAsync(intent, [new MarketplaceSeller("d_1", "600"), new MarketplaceSeller("d_2", "401")]));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => marketplace.CreateCheckoutIntentAsync(intent, [new MarketplaceSeller("d_1", "1000")], "SOME"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => marketplace.CreateCheckoutIntentAsync(
                new { amount = "1000", currency = "TRY", orderId = "o1", multiSellerPolicy = "PARTIAL_ALLOWED" }, [new MarketplaceSeller("d_1", "1000")]));
            Assert.Throws<LunixiConfigurationException>(() => new MarketplaceSeller("d_1", "0"));
            Assert.Throws<LunixiConfigurationException>(() => new MarketplaceSeller("d_1", "10.5"));
            Assert.Empty(requests);
        }
    }

    [Fact]
    public async Task CardChargesGoToTheirRouteWithTheLegs()
    {
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json("{\"status\":\"success\",\"data\":{\"paymentId\":\"pay_1\",\"status\":\"SUCCEEDED\"}}"));
        using (client)
        {
            await client.Marketplace.ChargeMarketplaceCard2dAsync(ChargeRequest(), Legs(), MarketplaceSellerPolicies.PartialAllowed, "idem-2d-000001");
            var threeDs = ChargeRequest();
            threeDs["callbackUrl"] = "https://shop.example/3ds";
            await client.Marketplace.ChargeMarketplaceCard3dAsync(threeDs, Legs());

            Assert.Equal(new[] { "/api/v1/payments/marketplace/2d", "/api/v1/payments/marketplace/3d" }, requests.Select(r => r.PathAndQuery));
            Assert.All(requests, request => Assert.True(request.Signed));
            Assert.Equal(new string?[] { "idem-2d-000001", null }, requests.Select(r => r.IdempotencyKey));
            using var body = JsonDocument.Parse(requests[0].Body!);
            Assert.Equal("PARTIAL_ALLOWED", body.RootElement.GetProperty("multiSellerPolicy").GetString());
            Assert.Equal(2, body.RootElement.GetProperty("sellers").GetArrayLength());
            Assert.Equal(CardNumber, body.RootElement.GetProperty("card").GetProperty("cardNumber").GetString());
            using var second = JsonDocument.Parse(requests[1].Body!);
            Assert.Equal("ALL_OR_NOTHING", second.RootElement.GetProperty("multiSellerPolicy").GetString());
        }
    }

    [Fact]
    public async Task InvalidChargesAreRefusedWithoutEchoingTheCard()
    {
        var (client, requests) = RecordingClient.Create();
        using (client)
        {
            var noBuyer = ChargeRequest();
            noBuyer.Remove("buyer");
            var withPrice = ChargeRequest();
            withPrice["price"] = "100000";
            var stringPreauth = ChargeRequest();
            stringPreauth["preauth"] = "false";
            var numericAmount = ChargeRequest();
            numericAmount["amount"] = 100000;
            var noBasket = ChargeRequest();
            noBasket["basketItems"] = Array.Empty<BasketItem>();

            var errors = new List<LunixiConfigurationException>
            {
                await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Marketplace.ChargeMarketplaceCard2dAsync(noBuyer, Legs())),
                await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Marketplace.ChargeMarketplaceCard2dAsync(withPrice, Legs())),
                await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Marketplace.ChargeMarketplaceCard2dAsync(stringPreauth, Legs())),
                await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Marketplace.ChargeMarketplaceCardAsync(numericAmount)),
                await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Marketplace.ChargeMarketplaceCard3dAsync(noBasket, Legs())),
                await Assert.ThrowsAsync<LunixiConfigurationException>(() =>
                    client.Marketplace.ChargeMarketplaceCard2dAsync(ChargeRequest(), [new MarketplaceSeller("d_alice", "99999")])),
            };
            foreach (var error in errors)
            {
                Assert.DoesNotContain(CardNumber, error.Message);
                Assert.DoesNotContain("987", error.Message);
            }
            Assert.Contains("buyer", errors[0].Message);
            Assert.Contains("price", errors[1].Message);
            Assert.Contains("preauth", errors[2].Message);
            Assert.Empty(requests);
        }
    }

    [Fact]
    public async Task DealerLifecycleRoutesAreBearerOnly()
    {
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json("{\"success\":true,\"data\":{\"id\":\"d_1\",\"legalName\":\"Acme\",\"status\":\"DRAFT\"}}"));
        using (client)
        {
            var marketplace = client.Marketplace;
            var dealer = await marketplace.CreateDealerAsync(new { legalName = "Acme", taxId = "123", externalRef = "42" });
            await marketplace.BulkCreateDealersAsync([new { legalName = "Beta" }, new { legalName = "Gamma", dealerType = MarketplaceDealerTypes.Individual }]);
            await marketplace.UpdateDealerStatusAsync("d_1", MarketplaceDealerStatuses.Suspended);
            await marketplace.ActivateDealerAsync("d_1");
            await marketplace.SetDealerTermsAsync("d_1", new { commissionBps = 250 });
            await marketplace.SubmitDealerApplicationAsync("d_1");

            Assert.Equal("d_1", dealer.GetProperty("id").GetString());
            Assert.Equal(new[]
            {
                "POST /api/v1/marketplace/dealers",
                "POST /api/v1/marketplace/dealers/bulk",
                "PUT /api/v1/marketplace/dealers/d_1/status",
                "POST /api/v1/marketplace/dealers/d_1/activate",
                "POST /api/v1/marketplace/dealers/d_1/terms",
                "POST /api/v1/marketplace/dealers/d_1/submit",
            }, requests.Select(r => r.Method + " " + r.PathAndQuery));
            Assert.All(requests, request => Assert.False(request.Signed));
            Assert.Equal("{\"legalName\":\"Acme\",\"taxId\":\"123\",\"externalRef\":\"42\"}", requests[0].Body);
            Assert.Equal("{\"dealers\":[{\"legalName\":\"Beta\"},{\"legalName\":\"Gamma\",\"dealerType\":\"INDIVIDUAL\"}]}", requests[1].Body);
            Assert.Equal("{\"status\":\"SUSPENDED\"}", requests[2].Body);
            Assert.Null(requests[3].Body);
            Assert.Equal("{}", requests[5].Body);

            await Assert.ThrowsAsync<LunixiConfigurationException>(() => marketplace.UpdateDealerStatusAsync("d_1", "PAUSED"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => marketplace.CreateDealerAsync(new { legalName = "Acme", dealerType = "COMPANY" }));
            var missing = await Assert.ThrowsAsync<LunixiConfigurationException>(() => marketplace.CreateDealerAsync(new { taxId = "123" }));
            Assert.Contains("requires legalName", missing.Message);
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => marketplace.BulkCreateDealersAsync([]));
            Assert.Equal(6, requests.Count);
        }
    }

    [Fact]
    public async Task ListsAndTheOnboardingLink()
    {
        var answers = new Queue<string>(new[]
        {
            "{\"status\":\"success\",\"data\":{\"items\":[{\"id\":\"d_1\",\"status\":\"ACTIVE\"},{\"id\":\"d_2\",\"status\":\"DRAFT\"}],\"total\":2,\"pageInfo\":{\"hasMore\":false,\"nextCursor\":null,\"totalCount\":2}}}",
            "{\"success\":true,\"token\":\"mp_xyz\"}",
        });
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json(answers.Dequeue()));
        using (client)
        {
            var page = await client.Marketplace.ListDealersAsync(new Dictionary<string, object?> { ["status"] = MarketplaceDealerStatuses.Active });
            Assert.Equal(2, page.Items.Count);
            Assert.Equal(2, page.TotalCount);
            Assert.False(page.HasMore);

            var link = await client.Marketplace.OnboardingTokenAsync("d_1");
            Assert.Equal("mp_xyz", link.GetProperty("token").GetString());
            Assert.Equal("https://altbayi.lunixi.com/m/mp_xyz", MarketplaceDealerOnboarding.Url(link));
            Assert.Equal("https://onboard.example/m/mp_xyz", MarketplaceDealerOnboarding.Url(
                JsonDocument.Parse("{\"token\":\"mp_xyz\",\"url\":\"https://onboard.example/m/mp_xyz\"}").RootElement));
            Assert.Equal(new[] { "GET /api/v1/marketplace/dealers?status=ACTIVE", "POST /api/v1/marketplace/dealers/d_1/onboarding-token" },
                requests.Select(r => r.Method + " " + r.PathAndQuery));
            Assert.Null(requests[1].Body);
        }
    }

    [Fact]
    public async Task PayoutsCommissionsAndTheAuditLog()
    {
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json("{\"success\":true,\"data\":{\"items\":[]}}"));
        using (client)
        {
            var marketplace = client.Marketplace;
            await marketplace.CreatePayoutAsync(new { subDealerId = "d_1", amountMinor = "5000", currency = "TRY" });
            await marketplace.ResolveCommissionAsync("d_1", "ELECTRONICS", "100000");
            await marketplace.BulkMarkPayoutsPaidAsync(["po_1", "po_2"], new { reference = "R-1" });
            await marketplace.AuditLogAsync(new Dictionary<string, object?> { ["limit"] = 20 });
            await marketplace.VerifyAuditLogAsync();

            Assert.Equal(new[]
            {
                "POST /api/v1/marketplace/payouts",
                "GET /api/v1/marketplace/commission/resolve?subDealerId=d_1&categoryCode=ELECTRONICS&mtdVolumeMinor=100000",
                "POST /api/v1/marketplace/payouts/bulk-mark-paid",
                "GET /api/v1/marketplace/audit-log?limit=20",
                "GET /api/v1/marketplace/audit-log/verify",
            }, requests.Select(r => r.Method + " " + r.PathAndQuery));
            Assert.Equal("{\"reference\":\"R-1\",\"payoutIds\":[\"po_1\",\"po_2\"]}", requests[2].Body);

            await Assert.ThrowsAsync<LunixiConfigurationException>(() => marketplace.CreatePayoutAsync(new { subDealerId = "d_1", amountMinor = 5000 }));
            var volume = await Assert.ThrowsAsync<LunixiConfigurationException>(() => marketplace.ResolveCommissionAsync("d_1", mtdVolumeMinor: "1000.50"));
            Assert.Contains("mtdVolumeMinor", volume.Message);
            var filter = await Assert.ThrowsAsync<LunixiConfigurationException>(() =>
                marketplace.CommissionRulesAsync(new Dictionary<string, object?> { ["includeArchived"] = true }));
            Assert.Contains("does not accept the filter", filter.Message);
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => marketplace.BulkMarkPayoutsPaidAsync([" "]));
            Assert.Equal(5, requests.Count);
        }
    }

    [Fact]
    public async Task AnAnswerWithNumericMoneyIsRefused()
    {
        var (client, _) = RecordingClient.Create(_ => FakeHandler.Json("{\"data\":{\"items\":[{\"id\":\"po_1\",\"payoutAmountMinor\":5000}]}}"));
        using (client)
            await Assert.ThrowsAsync<LunixiTransportException>(() => client.Marketplace.PayoutLedgerAsync());
    }
}
