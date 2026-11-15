using System.Text;
using System.Text.Json.Nodes;
using static Lunixi.Sdk.Tests.PhysicalPosTestSupport;

namespace Lunixi.Sdk.Tests;

public class PhysicalPosWebhookTests
{
    private static JsonObject PaymentData() => new()
    {
        ["paymentId"] = "pay_2",
        ["rootPaymentId"] = "pay_1",
        ["operation"] = "REFUND",
        ["paymentStatus"] = "APPROVED",
        ["amountMinor"] = "1500",
        ["approvedAmountMinor"] = null,
        ["currency"] = "TRY",
        ["historySeq"] = 4,
        ["original"] = new JsonObject { ["paymentId"] = "pay_1", ["origin"] = "DEVICE_DIRECT" },
    };

    [Fact]
    public void APaymentEventIsVerifiedAndRead()
    {
        var webhook = Parse(PhysicalPosEvents.PaymentApproved, PaymentData());
        Assert.Equal(("evt_1", "physical_pos.payment.approved", PhysicalPosWebhookEvent.CategoryPayment), (webhook.Id, webhook.Type, webhook.Category));
        Assert.Equal(("LIVE", "2026-11-15T07:59:58Z", (long?)4), (webhook.Environment, webhook.CreatedAt, webhook.HistorySeq));
        Assert.Equal(new PhysicalPosOriginalPayment("pay_1", "DEVICE_DIRECT"), webhook.Original);
        Assert.Null(webhook.Cashier);
        Assert.False(webhook.IsDeviceSale || webhook.IsSettlementEvent);
        Assert.Equal("1500", webhook.Data.GetProperty("amountMinor").GetString());
        Assert.DoesNotContain("pay_", webhook.ToString());
    }

    [Fact]
    public void DeviceSaleWithCashierAndSettlementEventsAreRead()
    {
        var data = new JsonObject
        {
            ["paymentId"] = "pay_3",
            ["origin"] = "DEVICE_DIRECT",
            ["deviceOccurredAt"] = "2026-11-15T07:50:00Z",
            ["amountMinor"] = "900",
            ["historySeq"] = 1,
            ["cashier"] = new JsonObject { ["cashierId"] = "cs_1", ["cashierCode"] = "K01", ["displayName"] = "Till 1" },
        };
        var sale = Parse(PhysicalPosEvents.PaymentDeviceSale, data);
        Assert.True(sale.IsDeviceSale);
        Assert.Equal(new PhysicalPosCashier("cs_1", "K01"), sale.Cashier);
        Assert.Null(sale.Original);

        var settlement = Parse(PhysicalPosEvents.SettlementCompleted, new JsonObject
        {
            ["deviceId"] = "dev_1",
            ["totals"] = new JsonObject { ["reported"] = new JsonObject { ["saleMinor"] = "10000" }, ["difference"] = new JsonObject { ["saleMinor"] = "-250" } },
        }, "evt_eod_1");
        Assert.Equal((PhysicalPosWebhookEvent.CategorySettlement, true, (long?)null), (settlement.Category, settlement.IsSettlementEvent, settlement.HistorySeq));
        Assert.Equal("evt_eod_1", settlement.Id);
    }

    [Fact]
    public void APayloadJsonEnvelopeIsReadTheSameWay()
    {
        var extra = new JsonObject { ["payloadJson"] = "{\"paymentId\":\"pay_4\",\"amountMinor\":\"100\"}" };
        var webhook = Parse(PhysicalPosEvents.PaymentDeclined, new JsonObject(), extra: extra);
        Assert.Equal("pay_4", webhook.Data.GetProperty("paymentId").GetString());

        var broken = new JsonObject { ["payloadJson"] = "{not json" };
        Assert.Throws<LunixiWebhookException>(() => Parse(PhysicalPosEvents.PaymentDeclined, new JsonObject(), extra: broken));
        Assert.Throws<LunixiWebhookException>(() => Parse(PhysicalPosEvents.PaymentDeclined, new JsonObject()));
    }

    [Fact]
    public void MoneyIsCheckedAfterTheSignatureAndOtherEventsAreRefused()
    {
        var data = PaymentData();
        data["amountMinor"] = 1500;
        Assert.Throws<LunixiWebhookException>(() => Parse(PhysicalPosEvents.PaymentApproved, data));
        var negative = PaymentData();
        negative["amountMinor"] = "-1500";
        Assert.Throws<LunixiWebhookException>(() => Parse(PhysicalPosEvents.PaymentApproved, negative));
        Assert.Throws<LunixiWebhookException>(() => Parse("payment.captured", PaymentData()));

        var (raw, headers) = Webhook(PhysicalPosEvents.PaymentApproved, PaymentData());
        var tampered = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(raw).Replace("\"1500\"", "\"9500\"", StringComparison.Ordinal));
        Assert.Throws<LunixiWebhookException>(() => PhysicalPosClient.ParseWebhookEvent(tampered, headers, WebhookSecret, new TestClock(Now)));
        Assert.Throws<LunixiWebhookException>(() => PhysicalPosClient.ParseWebhookEvent(raw, headers, "whsec_another_secret_000000000", new TestClock(Now)));

        var verifier = new WebhookVerifier(WebhookSecret, new TestClock(Now));
        Assert.Equal("evt_1", PhysicalPosClient.ParseWebhookEvent(raw, headers, verifier).Id);
        Assert.Equal("evt_1", PhysicalPosClient.ParseWebhookEvent(verifier.Verify(raw, headers)).Id);
    }
}
