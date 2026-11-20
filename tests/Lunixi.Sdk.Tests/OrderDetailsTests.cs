using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lunixi.Sdk.Tests;

/// <summary>Synthetic order details for request fixtures (no real person).</summary>
internal static class TestOrder
{
    public static JsonElement Buyer { get; } = JsonSerializer.SerializeToElement(new
    {
        name = "Ada",
        surname = "Yilmaz",
        identityNumber = "11111111111",
        email = "ada@example.com",
        gsmNumber = "+905350000000",
        city = "Istanbul",
        country = "Turkey",
        zipCode = "34000",
        ip = "203.0.113.10",
    });

    public static JsonElement Address { get; } = JsonSerializer.SerializeToElement(new
    {
        contactName = "Ada Yilmaz",
        address = "Example Street 1",
        city = "Istanbul",
        country = "Turkey",
        zipCode = "34000",
    });

    public static BasketItem[] Basket(string price) => [new("SKU-1", "Gift card", price, "VIRTUAL") { Category1 = "Digital" }];

    public static JsonElement BasketJson(string price)
        => JsonSerializer.SerializeToElement(new[] { new { id = "SKU-1", name = "Gift card", category1 = "Digital", itemType = "VIRTUAL", price } });

    public static CreateIntentRequest Intent(string amount, string currency, string orderId, IReadOnlyList<int>? enabledInstallments = null)
        => new(amount, currency, orderId) { Buyer = Buyer, BillingAddress = Address, BasketItems = Basket(amount), EnabledInstallments = enabledInstallments };

    /// <summary>The order details as JSON nodes, for raw dictionary bodies.</summary>
    public static Dictionary<string, object?> Fields(string price) => new()
    {
        ["buyer"] = Buyer,
        ["billingAddress"] = Address,
        ["basketItems"] = BasketJson(price),
    };
}

public class OrderDetailsTests
{
    [Fact]
    public void ConstantsMatchTheGatewayRule()
    {
        Assert.Equal(new[] { "buyer", "billingAddress", "basketItems" }, OrderDetails.RequiredFields);
        Assert.Equal(new[] { "name", "surname", "identityNumber", "email", "gsmNumber", "city", "country", "zipCode", "ip" }, OrderDetails.BuyerRequiredFields);
        Assert.Equal(new[] { "ip" }, OrderDetails.CheckoutIntentBuyerOptionalFields);
        Assert.Equal(new[] { "contactName", "address", "city", "country", "zipCode" }, OrderDetails.AddressRequiredFields);
        Assert.Equal(new[] { "id", "name", "category1", "itemType", "price" }, OrderDetails.BasketItemRequiredFields);
        Assert.Equal(("payment.order.field_required", "payment.order.field_invalid"), (OrderFieldErrorCodes.FieldRequired, OrderFieldErrorCodes.FieldInvalid));
        Assert.Equal(new[] { "required", "required_when_physical", "invalid_type", "invalid_format", "too_long", "too_few_items" },
            new[] { OrderFieldReasons.Required, OrderFieldReasons.RequiredWhenPhysical, OrderFieldReasons.InvalidType, OrderFieldReasons.InvalidFormat, OrderFieldReasons.TooLong, OrderFieldReasons.TooFewItems });
        Assert.Equal(("subscription.customer.field_required", "subscription.customer.field_invalid"),
            (SubscriptionCustomerErrorCodes.FieldRequired, SubscriptionCustomerErrorCodes.FieldInvalid));
    }

    [Fact]
    public void MissingFieldsNamesTheGatewayPaths()
    {
        Assert.Equal(new[] { "buyer", "billingAddress", "basketItems" }, OrderDetails.MissingFields(new { amount = "100" }));
        Assert.Empty(OrderDetails.MissingFields(TestOrder.Fields("100")));

        var body = TestOrder.Fields("100");
        body["buyer"] = new { name = "Ada", surname = " ", email = "ada@example.com" };
        body["billingAddress"] = new { contactName = "Ada Yilmaz", address = "Example Street 1", city = "Istanbul", country = "Turkey" };
        body["basketItems"] = new object[] { new { id = "SKU-1", name = "Lamp", itemType = "PHYSICAL", price = "100" }, "line" };
        Assert.Equal(new[]
        {
            "buyer.surname", "buyer.identityNumber", "buyer.gsmNumber", "buyer.city", "buyer.country", "buyer.zipCode", "buyer.ip",
            "billingAddress.zipCode", "basketItems.0.category1", "basketItems.1", "shippingAddress",
        }, OrderDetails.MissingFields(body));
    }

    [Fact]
    public void ShippingIsRequiredForPhysicalItemsAndCheckedWhenGiven()
    {
        var physical = TestOrder.Fields("100");
        physical["basketItems"] = JsonSerializer.SerializeToElement(new[] { new { id = "SKU-1", name = "Lamp", category1 = "Home", itemType = "PHYSICAL", price = "100" } });
        Assert.Equal(new[] { "shippingAddress" }, OrderDetails.MissingFields(physical));
        physical["shippingAddress"] = TestOrder.Address;
        Assert.Empty(OrderDetails.MissingFields(physical));

        var virtualWithShipping = TestOrder.Fields("100");
        virtualWithShipping["shippingAddress"] = new { contactName = "Ada Yilmaz" };
        Assert.Equal(new[] { "shippingAddress.address", "shippingAddress.city", "shippingAddress.country", "shippingAddress.zipCode" },
            OrderDetails.MissingFields(virtualWithShipping));
    }

    [Fact]
    public void BasketAndIpRulesFollowTheEndpoint()
    {
        var noIp = TestOrder.Fields("100");
        noIp["buyer"] = JsonSerializer.SerializeToElement(new
        {
            name = "Ada",
            surname = "Yilmaz",
            identityNumber = "11111111111",
            email = "ada@example.com",
            gsmNumber = "+905350000000",
            city = "Istanbul",
            country = "Turkey",
            zipCode = "34000",
        });
        Assert.Equal(new[] { "buyer.ip" }, OrderDetails.MissingFields(noIp));
        Assert.Empty(OrderDetails.MissingFields(noIp, checkoutIntent: true));
        Assert.Equal(new[] { "basketItems" }, OrderDetails.MissingFields(new { buyer = TestOrder.Buyer, billingAddress = TestOrder.Address, basketItems = Array.Empty<object>() }));
        Assert.Empty(OrderDetails.MissingFields(new { buyer = TestOrder.Buyer, billingAddress = TestOrder.Address }, basket: false));
    }

    [Fact]
    public async Task PaymentEndpointsRefuseIncompleteOrdersBeforeSending()
    {
        var (client, requests) = RecordingClient.Create();
        using (client)
        {
            var intent = await Assert.ThrowsAsync<LunixiConfigurationException>(() =>
                client.Payments.CreateIntentAsync(new CreateIntentRequest("100", "TRY", "ORDER-1") { BasketItems = TestOrder.Basket("100") }, "checkout:attempt-1"));
            Assert.Equal("A payment intent requires the order details: buyer, billingAddress.", intent.Message);

            var marketplace = await Assert.ThrowsAsync<LunixiConfigurationException>(() =>
                client.Payments.CreateMarketplaceIntentAsync(new CreateIntentRequest("100", "TRY", "ORDER-1") { Buyer = TestOrder.Buyer, BillingAddress = TestOrder.Address },
                    [new MarketplaceSeller("dealer-a", "100")], "marketplace:attempt-1"));
            Assert.Contains("basketItems", marketplace.Message);

            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Marketplace.CreateCheckoutIntentAsync(
                new CreateIntentRequest("100", "TRY", "ORDER-1"), [new MarketplaceSeller("dealer-a", "100")]));

            var direct = await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Payments.DirectMethodPaymentAsync(
                new DirectMethodPaymentRequest("100", "TRY", "ORDER-1").WithMethodFields(PaymentMethods.Wallet, "wallet-1")
                    .WithBuyer(TestOrder.Buyer).WithBillingAddress(TestOrder.Address), "direct:attempt-0001"));
            Assert.Equal("A direct method payment requires the order details: basketItems.", direct.Message);

            var card = await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Payments.StoreCardAsync(
                new StoreCardRequest(new { cardHolderName = "Ada Yilmaz" }, "user-1", new Uri("https://shop.example/cards/return")) { Buyer = TestOrder.Buyer },
                "store-card:user-1:0001"));
            Assert.Equal("Storing a card requires the order details: billingAddress.", card.Message);

            var topup = await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Wallet.Topups.StartCardAsync(
                new { creditWalletAccountId = "wa-1", amount = "100", currency = "TRY", buyer = TestOrder.Buyer }, "topup:attempt-0001"));
            Assert.Equal("A wallet card top-up requires the order details: billingAddress.", topup.Message);
            Assert.Empty(requests);
        }
        var split = Assert.Throws<LunixiConfigurationException>(() => new SplitPaymentRequest(new
        {
            totalAmount = "100",
            currency = "TRY",
            ownerRef = "owner",
            legs = new[] { new { cardToken = "card-1", amount = "100" } },
            buyer = TestOrder.Buyer,
            basketItems = TestOrder.BasketJson("100"),
        }));
        Assert.Equal("A split payment requires the order details: billingAddress.", split.Message);
    }

    [Fact]
    public async Task CheckoutIntentSendsTheOrderDetailsWithoutRequiringTheIp()
    {
        var (client, requests) = RecordingClient.Create();
        using (client)
        {
            var buyer = JsonNode.Parse(TestOrder.Buyer.GetRawText())!.AsObject();
            buyer.Remove("ip");
            var request = TestOrder.Intent("100", "TRY", "ORDER-1");
            await client.Payments.CreateIntentAsync(new CreateIntentRequest("100", "TRY", "ORDER-1")
            {
                Buyer = JsonSerializer.SerializeToElement(buyer),
                BillingAddress = request.BillingAddress,
                BasketItems = request.BasketItems,
            }, "checkout:attempt-1");
            var sent = JsonDocument.Parse(Assert.Single(requests).Body!).RootElement;
            Assert.False(sent.GetProperty("buyer").TryGetProperty("ip", out _));
            Assert.Equal("Istanbul", sent.GetProperty("billingAddress").GetProperty("city").GetString());
            Assert.Equal("Digital", sent.GetProperty("basketItems")[0].GetProperty("category1").GetString());
        }
    }

    [Fact]
    public async Task WalletTopupAndCardStorageSendTheBuyerAndBillingAddress()
    {
        var (client, requests) = RecordingClient.Create();
        using (client)
        {
            await client.Wallet.Topups.StartCardAsync(new
            {
                creditWalletAccountId = "wa-1",
                amount = "100",
                currency = "TRY",
                buyer = TestOrder.Buyer,
                billingAddress = TestOrder.Address,
            }, "topup:attempt-0001");
            await client.Payments.StoreCardAsync(new StoreCardRequest(new { cardHolderName = "Ada Yilmaz" }, "user-1", new Uri("https://shop.example/cards/return"))
            {
                Buyer = TestOrder.Buyer,
                BillingAddress = TestOrder.Address,
            }, "store-card:user-1:0001");
            Assert.Equal(2, requests.Count);
            foreach (var request in requests)
            {
                var sent = JsonDocument.Parse(request.Body!).RootElement;
                Assert.Equal("ada@example.com", sent.GetProperty("buyer").GetProperty("email").GetString());
                Assert.Equal("34000", sent.GetProperty("billingAddress").GetProperty("zipCode").GetString());
            }
        }
    }

    [Fact]
    public void SplitBodyAcceptsTheOrderDetails()
    {
        Assert.Equal(new[] { "buyer", "billingAddress", "shippingAddress", "basketItems" }, SplitPaymentFields.Payment.Skip(7));
        var split = new SplitPaymentRequest(new
        {
            totalAmount = "100",
            currency = "TRY",
            ownerRef = "owner",
            legs = new[] { new { cardToken = "card-1", amount = "100" } },
            buyer = TestOrder.Buyer,
            billingAddress = TestOrder.Address,
            shippingAddress = TestOrder.Address,
            basketItems = TestOrder.BasketJson("100"),
        });
        Assert.Equal("Ada", split.Payload.GetProperty("buyer").GetProperty("name").GetString());
    }
}
