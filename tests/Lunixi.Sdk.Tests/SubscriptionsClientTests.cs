using System.Text.Json;

namespace Lunixi.Sdk.Tests;

/// <summary>Contract source: the PHP SDK's subscription clients and the Node SDK's table (same routes, key rules and wire values). Names are fictional.</summary>
public class SubscriptionsClientTests
{
    private const string Subscription = "{\"data\":{\"id\":\"sub_1\",\"status\":\"ACTIVE\",\"priceSnapshotAmount\":\"9900\",\"priceSnapshotCurrency\":\"TRY\"}}";

    [Fact]
    public void TheTableIsBearerOnlyAndKeepsTheApiPaths()
    {
        foreach (var (name, route) in SubscriptionRoutes.All)
        {
            Assert.False(route.StepUp, name);
            Assert.Matches("^/api/(v1|panel/v1)/subscriptions/", route.Path);
        }
        Assert.Equal(LunixiRoute.KeyRequired, SubscriptionRoutes.All["create"].IdempotencyKeyRule);
        Assert.Equal("/api/panel/v1/subscriptions/portal/links", SubscriptionRoutes.All["portal.createPortalLink"].Path);
        Assert.Equal(23, SubscriptionEvents.All.Count);
        Assert.True(SubscriptionStatuses.IsActive(SubscriptionStatuses.PastDue));
        Assert.False(SubscriptionStatuses.IsActive("PAUSED"));
        Assert.True(SubscriptionStatuses.IsTerminal(JsonDocument.Parse("{\"status\":\"CANCELED\"}").RootElement));
        Assert.Equal("https://portal.lunixi.com/?token=cp_xyz", SubscriptionPortalLinks.HostedUrl(JsonDocument.Parse("{\"token\":\"cp_xyz\"}").RootElement));
    }

    [Fact]
    public async Task SubscriptionCallsCarryTheKeyInHeaderAndBody()
    {
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json(Subscription));
        using (client)
        {
            var subscriptions = client.Subscriptions;
            var created = await subscriptions.CreateAsync(new
            {
                customerId = "cus_1",
                planId = "plan_1",
                paymentCardId = "card_1",
                collectionMethod = SubscriptionCollectionMethods.AutoCharge,
            }, "idem-sub-0001");
            await subscriptions.CancelAsync("sub_1", atPeriodEnd: true, reason: "too expensive");
            await subscriptions.PauseAsync("sub_1");
            await subscriptions.PauseAsync("sub_1", new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero));
            await subscriptions.ResumeAsync("sub_1");
            await subscriptions.ChangePlanAsync("sub_1", new { newPlanId = "plan_2", behavior = "next_period" }, "idem-cp-00001");

            Assert.Equal("9900", created.GetProperty("priceSnapshotAmount").GetString());
            Assert.Equal(new[]
            {
                "POST /api/v1/subscriptions/subscriptions",
                "POST /api/v1/subscriptions/subscriptions/sub_1/cancel",
                "POST /api/v1/subscriptions/subscriptions/sub_1/pause",
                "POST /api/v1/subscriptions/subscriptions/sub_1/pause",
                "POST /api/v1/subscriptions/subscriptions/sub_1/resume",
                "POST /api/v1/subscriptions/subscriptions/sub_1/change-plan",
            }, requests.Select(r => r.Method + " " + r.PathAndQuery));
            Assert.All(requests, request => Assert.False(request.Signed));
            Assert.Equal(new string?[] { "idem-sub-0001", null, null, null, null, "idem-cp-00001" }, requests.Select(r => r.IdempotencyKey));
            Assert.Equal("{\"customerId\":\"cus_1\",\"planId\":\"plan_1\",\"paymentCardId\":\"card_1\",\"collectionMethod\":\"AUTO_CHARGE\",\"idempotencyKey\":\"idem-sub-0001\"}", requests[0].Body);
            Assert.Equal("{\"cancelAtPeriodEnd\":true,\"reason\":\"too expensive\"}", requests[1].Body);
            Assert.Null(requests[2].Body);
            Assert.Equal("{\"resumeAt\":\"2026-12-01T00:00:00.000Z\"}", requests[3].Body);
            Assert.Null(requests[4].Body);
            Assert.Equal("{\"newPlanId\":\"plan_2\",\"behavior\":\"NEXT_PERIOD\",\"idempotencyKey\":\"idem-cp-00001\"}", requests[5].Body);
        }
    }

    [Fact]
    public async Task SubResourcesUseTheirRoutes()
    {
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json("{\"data\":{\"id\":\"x_1\",\"price\":\"0\"}}"));
        using (client)
        {
            var subscriptions = client.Subscriptions;
            await subscriptions.Plans.CreateAsync(new
            {
                productId = "prod_1",
                code = "free",
                name = "Free",
                price = "0",
                currency = "try",
                billing = new { frequency = "monthly" },
            }, "idem-plan-0001");
            await subscriptions.Customers.UpsertAsync(new
            {
                externalId = "u-42",
                name = "Ada",
                surname = "Yilmaz",
                email = "ada@example.com",
                gsmNumber = "+905350000000",
                identityNumber = "11111111111",
                billingAddress = "Example Street 1",
                billingCity = "Istanbul",
                billingCountry = "Turkey",
                billingZipCode = "34000",
            }, "idem-cus-0001");
            await subscriptions.Customers.GetByExternalIdAsync("u-42");
            await subscriptions.Customers.AdjustCreditAsync("cus_1", new { amountMinor = "500", currency = "TRY", type = "GRANT" });
            await subscriptions.Cards.ListAsync("cus_1", pageSize: 10);
            await subscriptions.Cards.SetDefaultAsync("card_1");
            await subscriptions.Cards.DeactivateAsync("card_1");
            await subscriptions.Invoices.VoidAsync("inv_1", "duplicate");
            await subscriptions.Invoices.RetryChargeAsync("inv_1", "idem-retry-001");
            await subscriptions.Portal.CreatePortalLinkAsync("cus_1", new { allowedPlanIds = new[] { "plan_1" } });
            await subscriptions.Portal.RevokeLinkAsync("lnk_1");
            await subscriptions.Products.ListAsync(new Dictionary<string, object?> { ["active"] = true, ["limit"] = 5 });
            await subscriptions.Products.UpsertTranslationAsync("prod_1", "en", new { name = "Pro" });
            await subscriptions.Features.ListAsync(new Dictionary<string, object?> { ["productId"] = "prod_1", ["active"] = false });
            await subscriptions.Features.DeleteAsync("feat_1");
            await subscriptions.Addons.DeactivateAsync("addon_1");
            await subscriptions.Usage.RecordAsync(new { subscriptionId = "sub_1", featureKey = "api_calls", quantity = 3 });
            await subscriptions.Usage.CheckEntitlementAsync(new { subscriptionId = "sub_1", featureKey = "api_calls" });

            Assert.Equal(new[]
            {
                "POST /api/v1/subscriptions/plans",
                "PUT /api/v1/subscriptions/customers",
                "GET /api/v1/subscriptions/customers/by-external/u-42",
                "POST /api/v1/subscriptions/customers/cus_1/credit-balance/adjust",
                "GET /api/v1/subscriptions/cards?customerId=cus_1&includeInactive=false&pageSize=10",
                "POST /api/v1/subscriptions/cards/card_1/set-default",
                "DELETE /api/v1/subscriptions/cards/card_1",
                "POST /api/v1/subscriptions/invoices/inv_1/void",
                "POST /api/v1/subscriptions/invoices/inv_1/retry-charge",
                "POST /api/panel/v1/subscriptions/portal/links",
                "DELETE /api/panel/v1/subscriptions/portal/links/lnk_1",
                "GET /api/v1/subscriptions/products?limit=5&active=true",
                "PATCH /api/v1/subscriptions/products/prod_1/translations/en",
                "GET /api/v1/subscriptions/features?productId=prod_1&active=false",
                "DELETE /api/v1/subscriptions/features/feat_1",
                "DELETE /api/v1/subscriptions/addons/addon_1",
                "POST /api/v1/subscriptions/usage/record",
                "POST /api/v1/subscriptions/usage/check-entitlement",
            }, requests.Select(r => r.Method + " " + r.PathAndQuery));
            Assert.All(requests, request => Assert.False(request.Signed));
            Assert.Equal("idem-plan-0001", requests[0].IdempotencyKey);
            Assert.Equal("{\"productId\":\"prod_1\",\"code\":\"free\",\"name\":\"Free\",\"price\":\"0\",\"currency\":\"TRY\",\"billing\":{\"frequency\":\"MONTHLY\",\"interval\":1}}", requests[0].Body);
            Assert.Equal("idem-cus-0001", requests[1].IdempotencyKey);
            Assert.Null(requests[6].Body);
            Assert.Equal("{\"reason\":\"duplicate\"}", requests[7].Body);
            Assert.Equal("idem-retry-001", requests[8].IdempotencyKey);
            Assert.Null(requests[8].Body);
            Assert.Equal("{\"allowedPlanIds\":[\"plan_1\"],\"customerId\":\"cus_1\"}", requests[9].Body);
            Assert.Null(requests[10].Body);
            Assert.Equal("{\"name\":\"Pro\"}", requests[12].Body);
        }
    }

    [Fact]
    public async Task InvalidRequestsFailBeforeSending()
    {
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json(Subscription));
        using (client)
        {
            var subscriptions = client.Subscriptions;
            var plan = new Dictionary<string, object?>
            {
                ["productId"] = "prod_1",
                ["code"] = "pro",
                ["name"] = "Pro",
                ["price"] = "9900",
                ["currency"] = "TRY",
                ["billing"] = new Dictionary<string, object?> { ["frequency"] = "MONTHLY" },
            };
            Dictionary<string, object?> Plan(string field, object? value) => new(plan) { [field] = value };

            await Assert.ThrowsAsync<LunixiConfigurationException>(() => subscriptions.CreateAsync(new { customerId = "cus_1", planId = "plan_1" }));
            var unknown = await Assert.ThrowsAsync<LunixiConfigurationException>(() =>
                subscriptions.CreateAsync(new { customerId = "cus_1", planId = "plan_1", amount = "1" }, "idem-sub-0002"));
            Assert.Contains("does not accept amount", unknown.Message);
            await Assert.ThrowsAsync<LunixiConfigurationException>(() =>
                subscriptions.CreateAsync(new { customerId = "cus_1", planId = "plan_1", collectionMethod = "CASH" }, "idem-sub-0002"));
            var differ = await Assert.ThrowsAsync<LunixiConfigurationException>(() =>
                subscriptions.CreateAsync(new { customerId = "cus_1", planId = "plan_1", idempotencyKey = "idem-sub-aaaa" }, "idem-sub-bbbb"));
            Assert.Contains("differ", differ.Message);
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => subscriptions.ChangePlanAsync("sub_1", new { newPlanId = "plan_2", behavior = "LATER" }));

            var number = await Assert.ThrowsAsync<LunixiConfigurationException>(() => subscriptions.Plans.CreateAsync(Plan("price", 9900)));
            Assert.Contains("price must be a canonical string", number.Message);
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => subscriptions.Plans.CreateAsync(Plan("price", null)));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => subscriptions.Plans.CreateAsync(Plan("currency", "XXX")));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() =>
                subscriptions.Plans.CreateAsync(Plan("billing", new Dictionary<string, object?> { ["frequency"] = "HOURLY" })));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() =>
                subscriptions.Plans.CreateAsync(Plan("billing", new Dictionary<string, object?> { ["frequency"] = "DAILY", ["interval"] = 0 })));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() =>
                subscriptions.Plans.CreateAsync(Plan("billing", new Dictionary<string, object?> { ["frequency"] = "DAILY", ["every"] = 2 })));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => subscriptions.Plans.CreateAsync(Plan("trialPeriodDays", -1)));

            var card = await Assert.ThrowsAsync<LunixiConfigurationException>(() =>
                subscriptions.Cards.AddAsync(new { customerId = "cus_1", paymentMethodToken = "tok_abc", cardNumber = "4111111111111111" }));
            Assert.Contains("cardNumber", card.Message);
            Assert.DoesNotContain("4111", card.Message);
            await Assert.ThrowsAsync<LunixiConfigurationException>(() =>
                subscriptions.Cards.AddAsync(new { customerId = "cus_1", paymentMethodToken = "tok_abc", verificationMode = "ALWAYS" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => subscriptions.Cards.ListAsync(" "));

            var customer = await Assert.ThrowsAsync<LunixiConfigurationException>(() =>
                subscriptions.Customers.UpsertAsync(new { externalId = "u-42", email = "ada@example.com", name = "Ada" }));
            Assert.Contains("surname", customer.Message);
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => subscriptions.Customers.UpsertAsync(new
            {
                externalId = "u-42",
                name = "Ada",
                surname = "Yilmaz",
                email = "ada@example.com",
                gsmNumber = "+905350000000",
                identityNumber = "11111111111",
                billingAddress = "Example Street 1",
                billingCity = "Istanbul",
                billingCountry = "Turkey",
                billingZipCode = "34000",
                locale = "tr",
            }));

            await Assert.ThrowsAsync<LunixiConfigurationException>(() => subscriptions.Plans.ListAsync(new Dictionary<string, object?> { ["archived"] = true }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() =>
                subscriptions.Customers.AdjustCreditAsync("cus_1", new { amountMinor = 5, currency = "TRY" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => subscriptions.Plans.UpdateAsync("plan_1", null!));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => subscriptions.Portal.CreateSdkSessionAsync("cus_1", "https://shop.example", "EMBED"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => subscriptions.GetAsync(" "));
            Assert.Empty(requests);
        }
    }

    [Fact]
    public async Task ListsReadTheCursorEnvelopeAndIterateWithPageToken()
    {
        var pages = new Queue<string>(new[]
        {
            "{\"success\":true,\"data\":[{\"id\":\"plan_1\",\"price\":\"9900\",\"currency\":\"TRY\"}],\"nextPageToken\":\"tok_next\",\"pageSize\":20}",
            "{\"success\":true,\"data\":[{\"id\":\"plan_1\",\"price\":\"9900\"}],\"nextPageToken\":\"tok_next\"}",
            "{\"success\":true,\"data\":[{\"id\":\"plan_2\",\"price\":\"0\"}]}",
        });
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json(pages.Dequeue()));
        using (client)
        {
            var first = await client.Subscriptions.Plans.ListAsync(new Dictionary<string, object?> { ["includeArchived"] = false, ["productId"] = "prod_1" });
            Assert.Equal("9900", Assert.Single(first.Items).GetProperty("price").GetString());
            Assert.Equal("tok_next", first.NextCursor);
            Assert.True(first.HasMore);
            Assert.Equal(20, first.PageSize);

            var ids = new List<string>();
            await foreach (var row in client.Subscriptions.Plans.IterateAsync(new Dictionary<string, object?> { ["pageSize"] = 1 }))
                ids.Add(row.GetProperty("id").GetString()!);

            Assert.Equal(new[] { "plan_1", "plan_2" }, ids);
            Assert.Equal("/api/v1/subscriptions/plans?productId=prod_1&includeArchived=false", requests[0].PathAndQuery);
            Assert.Equal("/api/v1/subscriptions/plans?pageSize=1", requests[1].PathAndQuery);
            Assert.Equal("/api/v1/subscriptions/plans?pageSize=1&pageToken=tok_next", requests[2].PathAndQuery);
        }
    }

    [Fact]
    public async Task AnswersWithNumericMoneyAreRefused()
    {
        var (client, _) = RecordingClient.Create(_ => FakeHandler.Json("{\"data\":{\"id\":\"inv_1\",\"totalAmount\":9900}}"));
        using (client)
        {
            var error = await Assert.ThrowsAsync<LunixiTransportException>(() => client.Subscriptions.Invoices.GetAsync("inv_1"));
            Assert.Contains("totalAmount must be a canonical string", error.Message);
        }
    }

    [Fact]
    public async Task PortalSdkSessionReadsTheProofAtTheEnvelopeRoot()
    {
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json(
            "{\"success\":true,\"code\":\"OK\",\"portalToken\":\"cp_abc\",\"sessionProof\":\"hmac_xyz\",\"nonce\":\"nonce_1\",\"expiresAt\":\"2026-06-16T03:00:00Z\"}"));
        using (client)
        {
            var session = await client.Subscriptions.Portal.CreateSdkSessionAsync("cus_1", "https://shop.example", SubscriptionPortalSdkModes.Hybrid);
            Assert.Equal("cp_abc", session.GetProperty("portalToken").GetString());
            Assert.Equal("hmac_xyz", session.GetProperty("sessionProof").GetString());
            Assert.Equal("nonce_1", session.GetProperty("proofNonce").GetString());
            Assert.Equal("2026-06-16T03:00:00Z", session.GetProperty("proofExpiresAt").GetString());
            var request = Assert.Single(requests);
            Assert.Equal("/api/v1/subscriptions/portal/sdk-session", request.PathAndQuery);
            Assert.Equal("{\"customerId\":\"cus_1\",\"origin\":\"https://shop.example\",\"sdkMode\":\"HYBRID\"}", request.Body);
        }
    }
}
