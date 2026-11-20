namespace Lunixi.Sdk.Tests;

public class CardAndMarketplaceTests
{
    private static readonly object Card = new { cardHolderName = "Synthetic Holder", cardNumber = "4543871234567890", expireMonth = "12", expireYear = "2030", cvc = "123" };

    [Fact]
    public async Task BinInfoPostsTheDigitsWithTheBearerTokenOnly()
    {
        var (client, requests) = RecordingClient.Create();
        using (client)
        {
            await client.Payments.BinInfoAsync("45438712");
            var request = Assert.Single(requests);
            Assert.Equal("POST", request.Method);
            Assert.Equal("/api/v1/bin/info", request.PathAndQuery);
            Assert.Equal("{\"binOrPan\":\"45438712\"}", request.Body);
            Assert.False(request.Signed);
            Assert.Equal("Bearer token", request.Headers["Authorization"]);
        }
    }

    [Theory]
    [InlineData("45438")]
    [InlineData("4543 8712")]
    [InlineData("45438712345678901234")]
    [InlineData("")]
    public async Task BinInfoRejectsInvalidDigitsWithoutEchoingThem(string binOrPan)
    {
        var (client, requests) = RecordingClient.Create();
        using (client)
        {
            var error = await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Payments.BinInfoAsync(binOrPan));
            if (binOrPan.Length > 0) Assert.DoesNotContain(binOrPan, error.Message);
            Assert.Empty(requests);
        }
    }

    [Fact]
    public async Task InstallmentOptionsSendsTheEnabledInstallmentsInAscendingOrder()
    {
        var (client, requests) = RecordingClient.Create();
        using (client)
        {
            await client.Payments.InstallmentOptionsAsync(new InstallmentOptionsRequest("10000", "try")
            {
                BinOrPan = "454387",
                Installment = 3,
                EnabledInstallments = [3, 1, 2],
            });
            var request = Assert.Single(requests);
            Assert.Equal("/api/v1/bin/installments", request.PathAndQuery);
            Assert.Equal("{\"amount\":\"10000\",\"currency\":\"TRY\",\"binOrPan\":\"454387\",\"installment\":3,\"enabledInstallments\":[1,2,3]}", request.Body);
            Assert.False(request.Signed);
        }
    }

    public static TheoryData<int[]> InvalidInstallmentLists => new() { Array.Empty<int>(), new[] { 2, 3 }, new[] { 1, 1, 2 }, new[] { 1, 37 }, new[] { 0, 1 } };

    [Theory]
    [MemberData(nameof(InvalidInstallmentLists))]
    public async Task InstallmentOptionsFollowsTheIntentInstallmentRule(int[] counts)
    {
        var (client, requests) = RecordingClient.Create();
        using (client)
        {
            await Assert.ThrowsAsync<LunixiConfigurationException>(() =>
                client.Payments.InstallmentOptionsAsync(new InstallmentOptionsRequest("10000", "TRY") { EnabledInstallments = counts }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() =>
                client.Payments.InstallmentOptionsAsync(new InstallmentOptionsRequest("10000", "TRY") { BinOrPan = "12" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() =>
                client.Payments.InstallmentOptionsAsync(new InstallmentOptionsRequest("10000", "TRY") { Installment = 37 }));
            Assert.Empty(requests);
        }
        Assert.Throws<LunixiConfigurationException>(() => new InstallmentOptionsRequest("100.5", "TRY"));
    }

    [Fact]
    public async Task CardTaxonomyIsARead()
    {
        var (client, requests) = RecordingClient.Create();
        using (client)
        {
            await client.Payments.CardTaxonomyAsync();
            var request = Assert.Single(requests);
            Assert.Equal(("GET", "/api/v1/bin/card-taxonomy", (string?)null), (request.Method, request.PathAndQuery, request.Body));
        }
    }

    [Fact]
    public async Task StoreCardIsSignedAndRequiresTheIdempotencyKey()
    {
        var (client, requests) = RecordingClient.Create();
        using (client)
        {
            var store = new StoreCardRequest(Card, "user-1", new Uri("https://shop.example/cards/return"))
            {
                Currency = "try", Buyer = TestOrder.Buyer, BillingAddress = TestOrder.Address,
            };
            Assert.DoesNotContain("4543871234567890", store.ToString());
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Payments.StoreCardAsync(store, null!));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Payments.StoreCardAsync(store, "short"));
            Assert.Empty(requests);

            await client.Payments.StoreCardAsync(store, "store-card:user-1:0001");
            var request = Assert.Single(requests);
            Assert.Equal(("POST", "/api/v1/payments/cards"), (request.Method, request.PathAndQuery));
            Assert.True(request.Signed);
            Assert.Equal("store-card:user-1:0001", request.IdempotencyKey);
            Assert.StartsWith("{\"card\":{\"cardHolderName\":", request.Body);
            Assert.Contains(",\"cardUserKey\":\"user-1\",\"callbackUrl\":\"https://shop.example/cards/return\",\"currency\":\"TRY\",\"buyer\":{\"name\":\"Ada\"", request.Body);
            Assert.EndsWith(",\"billingAddress\":{\"contactName\":\"Ada Yilmaz\",\"address\":\"Example Street 1\",\"city\":\"Istanbul\",\"country\":\"Turkey\",\"zipCode\":\"34000\"}}", request.Body);
        }
        Assert.Throws<LunixiConfigurationException>(() => new StoreCardRequest(Card, "user-1", new Uri("http://shop.example/return")));
        Assert.Throws<LunixiConfigurationException>(() => new StoreCardRequest(new { }, "user-1", new Uri("https://shop.example/return")));
        Assert.Throws<LunixiConfigurationException>(() => new StoreCardRequest(Card, " ", new Uri("https://shop.example/return")));
    }

    [Fact]
    public async Task StoredCardReadsAndDeactivation()
    {
        var (client, requests) = RecordingClient.Create();
        using (client)
        {
            await client.Payments.ListStoredCardsAsync("user 1");
            await client.Payments.GetStoredCardAsync("card/token");
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Payments.DeactivateStoredCardAsync("card-token", ""));
            await client.Payments.DeactivateStoredCardAsync("card-token", "deactivate:card-token");

            Assert.Equal(3, requests.Count);
            Assert.Equal(("GET", "/api/v1/payments/cards?cardUserKey=user%201", false), (requests[0].Method, requests[0].PathAndQuery, requests[0].Signed));
            Assert.Equal(("GET", "/api/v1/payments/cards/card%2Ftoken", false), (requests[1].Method, requests[1].PathAndQuery, requests[1].Signed));
            Assert.Equal(("DELETE", "/api/v1/payments/cards/card-token", true), (requests[2].Method, requests[2].PathAndQuery, requests[2].Signed));
            Assert.Equal("deactivate:card-token", requests[2].IdempotencyKey);
            Assert.Null(requests[2].Body);
        }
    }

    [Fact]
    public async Task MarketplaceIntentCarriesSellersWhoseSharesAddUpToTheAmount()
    {
        var (client, requests) = RecordingClient.Create();
        using (client)
        {
            var intent = TestOrder.Intent("15000", "TRY", "ORDER-7");
            MarketplaceSeller[] sellers = [new("dealer-a", "10000"), new("dealer-b", "5000")];
            await client.Payments.CreateMarketplaceIntentAsync(intent, sellers, "marketplace:order-7", MarketplaceSellerPolicies.AllOrNothing);
            var request = Assert.Single(requests);
            Assert.Equal(("POST", "/api/v1/payments/marketplace/intents"), (request.Method, request.PathAndQuery));
            Assert.True(request.Signed);
            Assert.Equal("marketplace:order-7", request.IdempotencyKey);
            Assert.StartsWith("{\"amount\":\"15000\",\"currency\":\"TRY\",\"orderId\":\"ORDER-7\",\"buyer\":{", request.Body);
            Assert.Contains(",\"basketItems\":[{\"id\":\"SKU-1\"", request.Body);
            Assert.EndsWith(",\"sellers\":[{\"subDealerId\":\"dealer-a\",\"amountMinor\":\"10000\"},{\"subDealerId\":\"dealer-b\",\"amountMinor\":\"5000\"}],\"multiSellerPolicy\":\"ALL_OR_NOTHING\"}", request.Body);
        }
    }

    [Fact]
    public async Task MarketplaceIntentRejectsAMismatchedTotalBeforeSending()
    {
        var (client, requests) = RecordingClient.Create();
        using (client)
        {
            var intent = TestOrder.Intent("15000", "TRY", "ORDER-7");
            await Assert.ThrowsAsync<LunixiConfigurationException>(() =>
                client.Payments.CreateMarketplaceIntentAsync(intent, [new("dealer-a", "10000"), new("dealer-b", "4999")], "marketplace:order-7"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() =>
                client.Payments.CreateMarketplaceIntentAsync(intent, [new("dealer-a", "15001")], "marketplace:order-7"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() =>
                client.Payments.CreateMarketplaceIntentAsync(intent, [], "marketplace:order-7"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() =>
                client.Payments.CreateMarketplaceIntentAsync(intent, [new("dealer-a", "15000")], "marketplace:order-7", "SOME"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() =>
                client.Payments.CreateMarketplaceIntentAsync(intent, [new("dealer-a", "15000")], null!));
            Assert.Empty(requests);
        }
        Assert.Throws<LunixiConfigurationException>(() => new MarketplaceSeller("dealer-a", "0"));
        Assert.Throws<LunixiConfigurationException>(() => new MarketplaceSeller("dealer-a", "10.5"));
    }

    [Fact]
    public async Task AnalyticsAreReadsWithReportFilters()
    {
        var (client, requests) = RecordingClient.Create();
        using (client)
        {
            await client.Payments.FailoverRecoveryAnalyticsAsync(new Dictionary<string, string> { ["from"] = "2026-11-01", ["to"] = "2026-11-15" });
            await client.Payments.ProviderCredentialAnalyticsAsync("cred/1");
            await Assert.ThrowsAsync<LunixiConfigurationException>(() =>
                client.Payments.FailoverRecoveryAnalyticsAsync(new Dictionary<string, string> { ["from&x"] = "1" }));
            Assert.Equal(2, requests.Count);
            Assert.Equal("/api/v1/payments/analytics/failover-recovery?from=2026-11-01&to=2026-11-15", requests[0].PathAndQuery);
            Assert.Equal("/api/v1/payments/analytics/provider-credentials/cred%2F1", requests[1].PathAndQuery);
            Assert.All(requests, r => Assert.Equal("GET", r.Method));
        }
    }

    [Fact]
    public async Task FraudEventBatchIsASignedArray()
    {
        var (client, requests) = RecordingClient.Create();
        using (client)
        {
            object[] events = [new { eventId = "evt-1", subjectType = "CUSTOMER", eventType = "login", occurredAt = "2026-11-15T10:00:00Z" }];
            await client.Fraud.RecordEventBatchAsync(events, "fraud-batch:0001");
            var request = Assert.Single(requests);
            Assert.Equal(("POST", "/api/v1/fraud/events/transactions/batch"), (request.Method, request.PathAndQuery));
            Assert.True(request.Signed);
            Assert.Equal("fraud-batch:0001", request.IdempotencyKey);
            Assert.Equal("[{\"eventId\":\"evt-1\",\"subjectType\":\"CUSTOMER\",\"eventType\":\"login\",\"occurredAt\":\"2026-11-15T10:00:00Z\"}]", request.Body);

            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Fraud.RecordEventBatchAsync([]));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Fraud.RecordEventBatchAsync(["not-an-object"]));
            var tooMany = Enumerable.Range(0, FraudClient.EventBatchLimit + 1).Select(i => (object)new { eventId = "evt-" + i }).ToArray();
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Fraud.RecordEventBatchAsync(tooMany));
            Assert.Single(requests);
        }
    }

    [Fact]
    public async Task LatestDecisionForPaymentReadsTheFirstDecisionLog()
    {
        var answers = new Queue<string>(["{\"data\":[{\"traceId\":\"trace-2\"},{\"traceId\":\"trace-1\"}],\"pageInfo\":{\"hasMore\":true}}", "{\"data\":[],\"pageInfo\":{}}"]);
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json(answers.Dequeue()));
        using (client)
        {
            var latest = await client.Fraud.LatestDecisionForPaymentAsync("pay-1");
            Assert.Equal("trace-2", latest!.Value.GetProperty("traceId").GetString());
            Assert.Null(await client.Fraud.LatestDecisionForPaymentAsync("pay-2"));
            Assert.Equal("/api/v1/fraud/decision-logs?paymentId=pay-1&limit=1", requests[0].PathAndQuery);
            Assert.Equal("GET", requests[0].Method);
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Fraud.LatestDecisionForPaymentAsync(" "));
            Assert.Equal(2, requests.Count);
        }
    }
}
