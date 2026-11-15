using System.Net;
using System.Text.Json.Nodes;
using static Lunixi.Sdk.Tests.PhysicalPosTestSupport;

namespace Lunixi.Sdk.Tests;

public class BankConnectClientTests
{
    private const string TransferAnswer = "{\"data\":{\"transfer\":{\"id\":\"tr_1\",\"status\":\"SUBMITTED\",\"amountMinor\":\"150075\"},\"stepUp\":null,\"replayed\":false}}";

    private static object Transfer() => new
    {
        accountId = "acc_1",
        receiverIban = "TR330006100519786457841326",
        receiverName = " Ayşe Yılmaz ",
        amountMinor = "150075",
        currency = "try",
        paymentPurpose = "",
        description = "Invoice 1001",
    };

    [Fact]
    public async Task ReadsAreSignedAndFiltersAreEncoded()
    {
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json("{\"data\":{\"items\":[{\"transactionId\":\"t1\",\"amountMinor\":\"500\"}]},\"pageInfo\":{\"hasMore\":true,\"nextCursor\":\"c2\",\"totalCount\":null}}"));
        using (client)
        {
            var bank = client.BankConnect;
            var page = await bank.ListTransactionsAsync(new Dictionary<string, object?>
            {
                ["accountId"] = new[] { "acc_1", "acc_2" },
                ["bookedFrom"] = DateTimeOffset.Parse("2026-11-01T00:00:00+03:00"),
                ["bookedTo"] = "2026-11-15T00:00:00Z",
                ["direction"] = BankTransactionDirections.Credit,
                ["minAmountMinor"] = "0",
                ["maxAmountMinor"] = "100000",
                ["canonicalType"] = new[] { BankTransactionTypes.TransferIn, BankTransactionTypes.PosSettlement },
                ["limit"] = 50,
            });
            await bank.ListConnectionsAsync(new Dictionary<string, object?> { ["status"] = new[] { BankConnectionStatuses.Active, BankConnectionStatuses.Degraded } });
            await bank.ListAccountsAsync(new Dictionary<string, object?> { ["currency"] = "eur", ["status"] = BankAccountStatuses.Active });
            await bank.GetAccountAsync("acc_1");
            await bank.RequestSyncAsync("acc_1");

            Assert.Equal(("t1", true, "c2"), (page.Items[0].GetProperty("transactionId").GetString(), page.HasMore, page.NextCursor));
            Assert.Null(page.TotalCount);
            Assert.Equal(new[]
            {
                "GET /api/v1/bank-connect/transactions?limit=50&accountId=acc_1%2Cacc_2&bookedFrom=2026-10-31T21%3A00%3A00.000Z&bookedTo=2026-11-15T00%3A00%3A00Z&direction=CREDIT&minAmountMinor=0&maxAmountMinor=100000&canonicalType=TRANSFER_IN%2CPOS_SETTLEMENT",
                "GET /api/v1/bank-connect/connections?status=ACTIVE%2CDEGRADED",
                "GET /api/v1/bank-connect/accounts?currency=EUR&status=ACTIVE",
                "GET /api/v1/bank-connect/accounts/acc_1",
                "POST /api/v1/bank-connect/accounts/acc_1/sync",
            }, requests.Select(r => r.Method + " " + r.PathAndQuery));
            Assert.All(requests, request => Assert.True(request.Signed));
            Assert.Null(requests[4].Body);
            Assert.Null(requests[4].IdempotencyKey);
        }
    }

    [Fact]
    public async Task FilterRulesFailBeforeSending()
    {
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json());
        using (client)
        {
            var bank = client.BankConnect;
            Task Transactions(Dictionary<string, object?> filters) => bank.ListTransactionsAsync(filters);
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => bank.ListBanksAsync(new Dictionary<string, object?> { ["limit"] = 101 }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => bank.ListBanksAsync(new Dictionary<string, object?> { ["includeTotal"] = true }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => bank.ListBalancesAsync(new Dictionary<string, object?> { ["accountId"] = Enumerable.Range(0, 101).Select(i => "acc_" + i).ToArray() }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => bank.ListBalancesAsync(new Dictionary<string, object?> { ["accountId"] = "acc_1,acc_2" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => bank.ListAccountsAsync(new Dictionary<string, object?> { ["status"] = BankConnectionStatuses.Degraded }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => Transactions(new() { ["minAmountMinor"] = 100 }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => Transactions(new() { ["minAmountMinor"] = "10.5" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => Transactions(new() { ["minAmountMinor"] = "500", ["maxAmountMinor"] = "100" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => Transactions(new() { ["bookedTo"] = "2026-11-15T00:00:00Z" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => Transactions(new() { ["bookedFrom"] = "2026-11-15T00:00:00Z", ["bookedTo"] = "2026-11-01T00:00:00Z" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => Transactions(new() { ["bookedFrom"] = "2026-01-01T00:00:00Z", ["bookedTo"] = "2026-04-04T00:00:00Z" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => Transactions(new() { ["canonicalType"] = "SALARY" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => Transactions(new() { ["environment"] = "LIVE" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => bank.GetTransactionAsync(""));
            Assert.Empty(requests);

            await Transactions(new() { ["bookedFrom"] = "2026-01-01T00:00:00Z", ["bookedTo"] = "2026-04-03T00:00:00Z", ["limit"] = null });
            Assert.Single(requests);
        }
    }

    [Fact]
    public async Task TransferCreateGeneratesAKeyAndChecksTheBody()
    {
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json(TransferAnswer, HttpStatusCode.Created));
        using (client)
        {
            var created = await client.BankConnect.Transfers.CreateAsync(Transfer());
            var own = await client.BankConnect.Transfers.CreateAsync(Transfer(), "payout-1001-attempt");

            Assert.True(Guid.TryParse(created.IdempotencyKey, out _));
            Assert.Equal(created.IdempotencyKey, requests[0].IdempotencyKey);
            Assert.Equal(("payout-1001-attempt", "payout-1001-attempt"), (own.IdempotencyKey, requests[1].IdempotencyKey));
            Assert.Equal((201, false, "tr_1"), (created.StatusCode, created.Replayed, created.Entity!.Value.GetProperty("id").GetString()));
            Assert.Equal(("POST", "/api/v1/bank-connect/transfers"), (requests[0].Method, requests[0].PathAndQuery));
            Assert.True(requests[0].Signed);
            Assert.Equal(
                "{\"accountId\":\"acc_1\",\"receiverIban\":\"TR330006100519786457841326\",\"receiverName\":\"Ayşe Yılmaz\",\"amountMinor\":\"150075\",\"currency\":\"TRY\",\"description\":\"Invoice 1001\"}",
                requests[0].Body);

            var transfers = client.BankConnect.Transfers;
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => transfers.CreateAsync(Transfer(), "short"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => transfers.CreateAsync(Transfer(), "has a space in it"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => transfers.CreateAsync(new { accountId = "acc_1", receiverIban = "TR33", receiverName = "Ayşe", amountMinor = 150075 }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => transfers.CreateAsync(new { accountId = "acc_1", receiverIban = "TR33", receiverName = "Ayşe", amountMinor = "0" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => transfers.CreateAsync(new { accountId = "acc_1", receiverIban = "TR33", receiverName = "A", amountMinor = "1" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => transfers.CreateAsync(new { accountId = "acc_1", receiverIban = "TR33", receiverName = "Ayşe", amountMinor = "1", paymentPurpose = "has space" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => transfers.CreateAsync(new { accountId = "acc_1", receiverIban = "TR33", receiverName = "Ayşe", amountMinor = "1", description = new string('x', 141) }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => transfers.CreateAsync(new { accountId = "acc_1", receiverIban = "TR33", receiverName = "Ayşe", amountMinor = "1", environment = "LIVE" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => transfers.CreateAsync(new { receiverIban = "TR33", receiverName = "Ayşe", amountMinor = "1" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => transfers.ListAsync(new Dictionary<string, object?> { ["status"] = "DONE" }));
            Assert.Equal(2, requests.Count);
        }
    }

    [Fact]
    public async Task TransferCreateRetriesA5xxWithTheSameKey()
    {
        var (client, requests) = Scripted(2,
            () => Error(HttpStatusCode.ServiceUnavailable, "bank_connect.auth.ip_allowlist_unavailable"),
            NetworkFailure,
            () => FakeHandler.Json(TransferAnswer, HttpStatusCode.Created));
        using (client)
        {
            var created = await client.BankConnect.Transfers.CreateAsync(Transfer());
            Assert.Equal(3, requests.Count);
            Assert.All(requests, request => Assert.Equal(created.IdempotencyKey, request.IdempotencyKey));
            Assert.Single(requests.Select(request => request.Body).Distinct());
        }
    }

    [Fact]
    public async Task FxQuoteTakesExactlyOneAmountAndOrdersAreKeyed()
    {
        var (client, requests) = RecordingClient.Create(request => request.RequestUri!.AbsolutePath.EndsWith("/orders", StringComparison.Ordinal)
            ? FakeHandler.Json("{\"data\":{\"order\":{\"id\":\"fx_1\",\"status\":\"SUBMITTED\"},\"replayed\":true}}", HttpStatusCode.Created)
            : FakeHandler.Json("{\"data\":{\"id\":\"q_1\",\"sourceAmountMinor\":\"10000\",\"targetAmountMinor\":\"290\"}}", HttpStatusCode.Created));
        using (client)
        {
            var fx = client.BankConnect.Fx;
            await fx.CreateQuoteAsync(new { sourceAccountId = "acc_try", targetAccountId = "acc_eur", sourceAmountMinor = "10000" });
            var order = await fx.CreateOrderAsync(new { quoteId = "q_1" }, "fx-order-0000001");
            await fx.ListOrdersAsync(new Dictionary<string, object?> { ["status"] = new[] { BankFxOrderStatuses.Completed, BankFxOrderStatuses.Failed } });
            await fx.ListRatesAsync("con_1");

            Assert.True(order.Replayed);
            Assert.Equal("fx_1", order.Entity!.Value.GetProperty("id").GetString());
            Assert.Equal(new[]
            {
                "POST /api/v1/bank-connect/fx/quotes",
                "POST /api/v1/bank-connect/fx/orders",
                "GET /api/v1/bank-connect/fx/orders?status=COMPLETED%2CFAILED",
                "GET /api/v1/bank-connect/fx/rates?connectionId=con_1",
            }, requests.Select(r => r.Method + " " + r.PathAndQuery));
            Assert.Null(requests[0].IdempotencyKey);
            Assert.Equal("fx-order-0000001", requests[1].IdempotencyKey);

            await Assert.ThrowsAsync<LunixiConfigurationException>(() => fx.CreateQuoteAsync(new { sourceAccountId = "a", targetAccountId = "b" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => fx.CreateQuoteAsync(new { sourceAccountId = "a", targetAccountId = "b", sourceAmountMinor = "1", targetAmountMinor = "1" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => fx.CreateQuoteAsync(new { sourceAccountId = "a", targetAccountId = "a", sourceAmountMinor = "1" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => fx.CreateOrderAsync(new { quoteId = "q_1", fxSessionId = "s" }));
            Assert.Equal(4, requests.Count);
        }
    }

    [Fact]
    public void TransactionWebhookIsRead()
    {
        var data = new JsonObject
        {
            ["transactionId"] = "t1",
            ["accountId"] = "acc_1",
            ["connectionId"] = "con_1",
            ["bankEftCode"] = "0062",
            ["bankName"] = "Bank",
            ["direction"] = "CREDIT",
            ["amountMinor"] = "150075",
            ["currency"] = "TRY",
            ["bookedAt"] = "2026-11-15T07:00:00Z",
            ["valueAt"] = null,
            ["receivedAt"] = "2026-11-15T07:01:00Z",
            ["canonicalType"] = "TRANSFER_IN",
            ["description"] = "Invoice",
            ["counterpartyNameMasked"] = "A*** Y***",
            ["counterpartyIbanMasked"] = null,
            ["receiptNo"] = null,
        };
        var received = BankAccountTransactionReceivedData.FromWebhook(Verify(BankConnectEvents.TransactionReceived, data));
        Assert.Equal(("evt_1", "t1", "150075", "CREDIT", "LIVE"), (received.EventId, received.TransactionId, received.AmountMinor, received.Direction, received.Environment));
        Assert.Null(received.ValueAt);
        Assert.Equal("A*** Y***", received.CounterpartyNameMasked);

        data["amountMinor"] = 150075;
        Assert.Throws<LunixiWebhookException>(() => BankAccountTransactionReceivedData.FromWebhook(Verify(BankConnectEvents.TransactionReceived, data)));
        Assert.Throws<LunixiWebhookException>(() => BankAccountTransactionReceivedData.FromWebhook(Verify(BankTransferEvents.Completed, data)));
    }

    [Fact]
    public void TransferWebhookIsReadAndPaymentTransferEventsAreRefused()
    {
        var data = new JsonObject
        {
            ["transferId"] = "tr_1",
            ["connectionId"] = "con_1",
            ["accountId"] = "acc_1",
            ["status"] = BankTransferStatuses.Failed,
            ["rail"] = null,
            ["amountMinor"] = "150075",
            ["currency"] = "TRY",
            ["failureCode"] = "bank_connect.transfer.insufficient_balance",
            ["idempotencyKey"] = "payout-1001-attempt",
            ["createdAt"] = "2026-11-15T07:00:00Z",
            ["failedAt"] = "2026-11-15T07:02:00Z",
        };
        var failed = BankTransferWebhookData.FromWebhook(Verify(BankTransferEvents.Failed, data));
        Assert.Equal((BankTransferEvents.Failed, "tr_1", BankTransferStatuses.Failed, "payout-1001-attempt"), (failed.Type, failed.TransferId, failed.Status, failed.IdempotencyKey));
        Assert.Null(failed.Rail);
        Assert.Equal("bank_connect.transfer.insufficient_balance", failed.FailureCode);
        Assert.Throws<LunixiWebhookException>(() => BankTransferWebhookData.FromWebhook(Verify(PaymentBankTransferEvents.Underpaid, data)));
    }

    private static LunixiWebhook Verify(string type, JsonObject data)
    {
        var (raw, headers) = Webhook(type, (JsonObject)data.DeepClone());
        return new WebhookVerifier(WebhookSecret, new TestClock(Now)).Verify(raw, headers);
    }
}
