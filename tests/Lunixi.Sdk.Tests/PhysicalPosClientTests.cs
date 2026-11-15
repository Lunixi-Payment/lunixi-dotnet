using System.Net;
using System.Text.Json.Nodes;
using static Lunixi.Sdk.Tests.PhysicalPosTestSupport;

namespace Lunixi.Sdk.Tests;

public class PhysicalPosClientTests
{
    [Fact]
    public void ConstantsMatchTheReferenceValues()
    {
        Assert.Equal(new[] { "SALE", "PREAUTH_OPEN", "REFUND_UNMATCHED" }, PhysicalPosPaymentOperations.Root);
        Assert.Equal(new[] { "VOID", "REFUND", "PREAUTH_CLOSE", "PREAUTH_CANCEL" }, PhysicalPosPaymentOperations.Reversal);
        Assert.Equal(new[] { "NOT_STARTED", "IN_PROGRESS", "APPROVED", "PARTIALLY_APPROVED", "DECLINED", "CANCELLED", "EXPIRED", "FAILED", "UNKNOWN", "MANUAL_REVIEW" },
            PhysicalPosPaymentStatuses.All);
        Assert.Equal(new[] { "NONE", "PARTIALLY_REFUNDED", "REFUNDED", "VOIDED", "PREAUTH_CLOSED", "PREAUTH_CANCELLED", "PREAUTH_EXPIRED" }, PhysicalPosReversalStatuses.All);
        Assert.Equal("physical_pos.*", PhysicalPosEvents.Pattern);
        Assert.Equal(4, PhysicalPosEvents.Settlement.Count);
        Assert.Equal((8, 200, 100, 2_147_483_647L), (PhysicalPosValues.IdempotencyKeyMinLength, PhysicalPosValues.IdempotencyKeyMaxLength, PhysicalPosValues.MaxLimit, PhysicalPosValues.RowVersionMax));
        Assert.Equal(("physical_pos.payment.card_data_rejected", "physical_pos.payment.void_after_settlement"),
            (PhysicalPosCardDataRejectedException.ErrorCode, PhysicalPosVoidAfterSettlementException.ErrorCode));
        Assert.True(Guid.TryParse(PhysicalPosClient.NewIdempotencyKey(), out _));
    }

    [Fact]
    public async Task CreatePaymentSendsTheKeyAsGivenAndReadsTheAcceptedPayment()
    {
        var (client, requests) = RecordingClient.Create(_ => Payment());
        using (client)
        {
            var sale = Sale();
            sale["items"] = new JsonArray(new JsonObject
            {
                ["lineNo"] = 1,
                ["name"] = "Simit 8691234567012",
                ["quantityMilli"] = 2000,
                ["unitCode"] = "C62",
                ["unitPriceMinor"] = "2250",
                ["taxGroupCode"] = "KDV1",
                ["lineTotalMinor"] = "4500",
                ["barcode"] = "",
            });
            sale["instrument"] = null;
            var result = await client.PhysicalPos.CreatePaymentAsync(sale, Key);

            var sent = Assert.Single(requests);
            Assert.Equal(("POST", "/api/v1/physical-pos/payments"), (sent.Method, sent.PathAndQuery));
            Assert.True(sent.Signed);
            Assert.Equal(Key, sent.IdempotencyKey);
            Assert.Equal("{\"operation\":\"SALE\",\"target\":{\"deviceId\":\"dev_1\"},\"amountMinor\":\"4500\",\"currency\":\"TRY\",\"instrument\":null," +
                "\"merchantReference\":\"ORDER-1001\",\"allowDuplicateReference\":false,\"items\":[{\"lineNo\":1,\"name\":\"Simit 8691234567012\"," +
                "\"quantityMilli\":2000,\"unitCode\":\"C62\",\"unitPriceMinor\":\"2250\",\"taxGroupCode\":\"KDV1\",\"lineTotalMinor\":\"4500\"}]}", sent.Body);
            Assert.Equal((202, true, false, Key), (result.StatusCode, result.Accepted, result.Replayed, result.IdempotencyKey));
            Assert.Equal("pay_1", result.Payment!.Value.GetProperty("paymentId").GetString());
            Assert.Equal("pay_1", result.Entity!.Value.GetProperty("paymentId").GetString());
        }
    }

    [Fact]
    public async Task TheSameKeyAndBodyComeBackReplayedWith200()
    {
        var (client, _) = RecordingClient.Create(_ => Payment(HttpStatusCode.OK, replayed: true));
        using (client)
        {
            var result = await client.PhysicalPos.CreatePaymentAsync(Sale(), Key);
            Assert.Equal((200, false, true), (result.StatusCode, result.Accepted, result.Replayed));
        }
    }

    [Fact]
    public async Task EveryKeyedWriteReportsNewOrReplayed()
    {
        var (client, requests) = Scripted(0,
            () => Payment(), () => Payment(HttpStatusCode.OK, replayed: true),
            () => Payment(HttpStatusCode.OK, replayed: true),
            () => FakeHandler.Json("{\"data\":{\"run\":{\"runId\":\"run_1\"},\"replayed\":true}}"),
            () => FakeHandler.Json("{\"data\":{\"store\":{\"id\":\"st_1\"},\"replayed\":true}}"));
        using (client)
        {
            var reversal = new { operation = "VOID", currency = "TRY" };
            var first = await client.PhysicalPos.CreateReversalAsync("pay_1", reversal, "pos-void-00001");
            var again = await client.PhysicalPos.CreateReversalAsync("pay_1", reversal, "pos-void-00001");
            var cancel = await client.PhysicalPos.CancelPaymentAsync("pay_1", "pos-cancel-0001");
            var run = await client.PhysicalPos.StartSettlementAsync("dev_1", "eod-dev_1-0001");
            var store = await client.PhysicalPosManagement.CreateStoreAsync(new { code = "S1", name = "Store" }, "store-create-0001");

            Assert.Equal((202, false), (first.StatusCode, first.Replayed));
            Assert.Equal((200, true), (again.StatusCode, again.Replayed));
            Assert.Equal((200, true, "pos-cancel-0001"), (cancel.StatusCode, cancel.Replayed, cancel.IdempotencyKey));
            Assert.Equal((200, true, "run_1"), (run.StatusCode, run.Replayed, run.Entity!.Value.GetProperty("runId").GetString()));
            Assert.Equal((200, true, "st_1"), (store.StatusCode, store.Replayed, store.Entity!.Value.GetProperty("id").GetString()));
            Assert.Equal(new[] { "pos-void-00001", "pos-void-00001", "pos-cancel-0001", "eod-dev_1-0001", "store-create-0001" }, requests.Select(r => r.IdempotencyKey));
            Assert.Equal(requests[0].Body, requests[1].Body);
        }
    }

    [Fact]
    public async Task PaymentStartAndReversalRequireTheCallersKey()
    {
        var (client, requests) = RecordingClient.Create(_ => Payment());
        using (client)
        {
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PhysicalPos.CreatePaymentAsync(Sale(), null!));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PhysicalPos.CreatePaymentAsync(Sale(), " " + Key));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PhysicalPos.CreatePaymentAsync(Sale(), new string('k', 201)));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PhysicalPos.CreateReversalAsync("pay_1", new { operation = "VOID", currency = "TRY" }, null!));
            Assert.Empty(requests);
        }
    }

    [Fact]
    public async Task BodiesAreCheckedBeforeSending()
    {
        var (client, requests) = RecordingClient.Create(_ => Payment());
        using (client)
        {
            async Task<Exception> Refused(Action<JsonObject> change)
            {
                var request = Sale(); change(request);
                return (await Record.ExceptionAsync(() => client.PhysicalPos.CreatePaymentAsync(request, Key)))!;
            }
            Assert.IsType<LunixiConfigurationException>(await Refused(r => r["colour"] = "red"));
            Assert.IsType<LunixiConfigurationException>(await Refused(r => r.Remove("target")));
            Assert.IsType<LunixiConfigurationException>(await Refused(r => r["amountMinor"] = 4500));
            Assert.IsType<LunixiConfigurationException>(await Refused(r => r["amountMinor"] = "04500"));
            Assert.IsType<LunixiConfigurationException>(await Refused(r => r["amountMinor"] = "0"));
            Assert.IsType<LunixiConfigurationException>(await Refused(r => r["installmentCount"] = "3"));
            Assert.IsType<LunixiConfigurationException>(await Refused(r => r["installmentCount"] = 1.5));
            Assert.IsType<LunixiConfigurationException>(await Refused(r => r["allowDuplicateReference"] = "false"));
            Assert.IsType<LunixiConfigurationException>(await Refused(r => r["currency"] = ""));
            Assert.IsType<LunixiConfigurationException>(await Refused(r => r["target"] = new JsonObject { ["storeId"] = "s1" }));
            Assert.IsType<LunixiConfigurationException>(await Refused(r => r["items"] = new JsonArray()));
            Assert.IsType<LunixiConfigurationException>(await Refused(r => r["items"] = new JsonArray(new JsonObject
            {
                ["lineNo"] = 1,
                ["name"] = "Tea",
                ["quantityMilli"] = 1000,
                ["unitCode"] = "C62",
                ["unitPriceMinor"] = "1500",
                ["lineTotalMinor"] = "1500",
            })));
            Assert.IsType<LunixiConfigurationException>(await Refused(r => r["price"] = "4500"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PhysicalPos.CreatePaymentAsync(null!, Key));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PhysicalPos.CreatePaymentAsync("SALE", Key));
            Assert.Empty(requests);
        }
    }

    [Fact]
    public async Task CardDataIsRefusedLocallyWithTheFieldPathOnly()
    {
        var (client, requests) = RecordingClient.Create(_ => Payment());
        using (client)
        {
            async Task<PhysicalPosCardDataRejectedException> Refused(Action<JsonObject> change)
            {
                var request = Sale(); change(request);
                return await Assert.ThrowsAsync<PhysicalPosCardDataRejectedException>(() => client.PhysicalPos.CreatePaymentAsync(request, Key));
            }
            var nested = await Refused(r => r["target"]!["cardNumber"] = "4111111111111111");
            Assert.Equal(("target.cardNumber", 0, PhysicalPosCardDataRejectedException.ErrorCode), (nested.Field, nested.StatusCode, nested.Code));
            Assert.DoesNotContain("4111", nested.Message);
            Assert.Equal("cvv", (await Refused(r => r["cvv"] = "123")).Field);
            Assert.Equal("merchantReference", (await Refused(r => r["merchantReference"] = "ref 4111 1111 1111 1111")).Field);
            Assert.Equal("items[0].name", (await Refused(r => r["items"] = new JsonArray(Item("Card 4111111111111111")))).Field);
            Assert.Equal("items[0].taxGroupCode", (await Refused(r => r["items"] = new JsonArray(Item("Tea", "8691234567012")))).Field);
            Assert.Equal("merchantReference", (await Assert.ThrowsAsync<PhysicalPosCardDataRejectedException>(() =>
                client.PhysicalPos.CreateReversalAsync("pay_1", new { operation = "REFUND", amountMinor = "100", currency = "TRY", merchantReference = "4111111111111111" }, Key))).Field);
            Assert.IsAssignableFrom<LunixiValidationException>(nested);
            Assert.Empty(requests);
        }

        static JsonObject Item(string name, string taxGroupCode = "KDV10") => new()
        {
            ["lineNo"] = 1,
            ["name"] = name,
            ["quantityMilli"] = 1000,
            ["unitCode"] = "C62",
            ["unitPriceMinor"] = "1500",
            ["taxGroupCode"] = taxGroupCode,
            ["lineTotalMinor"] = "1500",
        };
    }

    [Fact]
    public async Task ReversalsAndCancelUseTheirRoutes()
    {
        var (client, requests) = RecordingClient.Create(_ => Payment());
        using (client)
        {
            await client.PhysicalPos.CreateReversalAsync("pay_1", new
            {
                operation = PhysicalPosPaymentOperations.Refund,
                currency = "TRY",
                items = new[] { new { lineNo = 1, quantityMilli = 1000 } },
            }, "pos-refund-0001");
            await client.PhysicalPos.CreateReversalAsync("pay_1", new { operation = "VOID", currency = "TRY", target = new { externalRef = "till-4" } }, "pos-void-00001");
            var cancel = await client.PhysicalPos.CancelPaymentAsync("pay 1");

            Assert.Equal(("POST", "/api/v1/physical-pos/payments/pay_1/reversals", "pos-refund-0001"), (requests[0].Method, requests[0].PathAndQuery, requests[0].IdempotencyKey));
            Assert.Equal("{\"operation\":\"REFUND\",\"currency\":\"TRY\",\"items\":[{\"lineNo\":1,\"quantityMilli\":1000}]}", requests[0].Body);
            Assert.Equal("{\"operation\":\"VOID\",\"currency\":\"TRY\",\"target\":{\"externalRef\":\"till-4\"}}", requests[1].Body);
            Assert.Equal("/api/v1/physical-pos/payments/pay%201/cancel", requests[2].PathAndQuery);
            Assert.Equal("{}", requests[2].Body);
            Assert.True(Guid.TryParse(requests[2].IdempotencyKey, out _));
            Assert.Equal(requests[2].IdempotencyKey, cancel.IdempotencyKey);
            Assert.All(requests, request => Assert.True(request.Signed));
        }
    }

    [Fact]
    public async Task AnAmbiguousAttemptIsRetriedWithTheSameKeyAndBody()
    {
        var (client, requests) = Scripted(2, NetworkFailure, () => Payment(HttpStatusCode.OK, replayed: true));
        using (client)
        {
            var result = await client.PhysicalPos.CreatePaymentAsync(Sale(), Key);
            Assert.True(result.Replayed);
            Assert.Equal(2, requests.Count);
            Assert.All(requests, request => Assert.Equal((Key, requests[0].Body), (request.IdempotencyKey, request.Body)));
        }
    }

    [Fact]
    public async Task NoAnswerOnEveryAttemptLeavesTheOutcomeUnknown()
    {
        var (client, requests) = Scripted(2, NetworkFailure);
        using (client)
        {
            var error = await Assert.ThrowsAsync<PhysicalPosOutcomeUnknownException>(() => client.PhysicalPos.CreatePaymentAsync(Sale(), Key));
            Assert.Equal((Key, 3, 0), (error.IdempotencyKey, error.Attempts, error.StatusCode));
            Assert.IsType<LunixiTransportException>(error.InnerException);
            Assert.Equal(3, requests.Count);
        }
    }

    [Fact]
    public async Task AnyAmbiguousAttemptWinsOverALaterDefiniteAnswer()
    {
        var (client, requests) = Scripted(2, () => Error(HttpStatusCode.BadGateway, "physical_pos.upstream.error"),
            () => Error(HttpStatusCode.Conflict, "physical_pos.idempotency_mismatch"));
        using (client)
        {
            var error = await Assert.ThrowsAsync<PhysicalPosOutcomeUnknownException>(() => client.PhysicalPos.CreateReversalAsync(
                "pay_1", new { operation = "VOID", currency = "TRY" }, Key));
            Assert.Equal((409, "physical_pos.idempotency_mismatch", 2), (error.StatusCode, error.Code, error.Attempts));
            Assert.Equal(2, requests.Count);
        }
    }

    [Fact]
    public async Task AnAnswerWithoutAPaymentIsAmbiguous()
    {
        var (client, requests) = Scripted(1, () => FakeHandler.Json("{\"data\":{}}", HttpStatusCode.Accepted),
            () => FakeHandler.Json("{\"data\":{\"payment\":{\"paymentId\":\"pay_1\",\"amountMinor\":4500}}}", HttpStatusCode.Accepted));
        using (client)
        {
            var error = await Assert.ThrowsAsync<PhysicalPosOutcomeUnknownException>(() => client.PhysicalPos.CreatePaymentAsync(Sale(), Key));
            Assert.Equal((202, 2), (error.StatusCode, error.Attempts));
            Assert.Equal(2, requests.Count);
        }
    }

    [Fact]
    public async Task NotStartedServerErrorsAreRetriedAndStayDefinite()
    {
        var notStarted = () => Error(HttpStatusCode.ServiceUnavailable, "physical_pos.presence.unavailable");
        var (client, requests) = Scripted(2, notStarted, () => Payment());
        using (client)
        {
            Assert.True((await client.PhysicalPos.CreatePaymentAsync(Sale(), Key)).Accepted);
            Assert.Equal(2, requests.Count);
        }

        var (failing, attempts) = Scripted(2, notStarted);
        using (failing)
        {
            var error = await Assert.ThrowsAsync<LunixiApiException>(() => failing.PhysicalPos.CreatePaymentAsync(Sale(), Key));
            Assert.IsNotType<PhysicalPosOutcomeUnknownException>(error);
            Assert.Equal((503, "physical_pos.presence.unavailable"), (error.StatusCode, error.Code));
            Assert.Equal(3, attempts.Count);
        }
    }

    [Fact]
    public async Task ClientErrorsStopAtOnceAndAreTyped()
    {
        var (client, requests) = Scripted(2, () => Error((HttpStatusCode)429, "RATE_LIMITED", "{\"retryAfterSeconds\":7}"));
        using (client)
        {
            var limited = await Assert.ThrowsAsync<PhysicalPosRateLimitException>(() => client.PhysicalPos.CreatePaymentAsync(Sale(), Key));
            Assert.Equal((7d, Key), (limited.RetryAfterSeconds, limited.IdempotencyKey));
            Assert.IsAssignableFrom<LunixiRateLimitException>(limited);
            Assert.Single(requests);
        }

        var (voidClient, _) = Scripted(2, () => Error(HttpStatusCode.Conflict, PhysicalPosVoidAfterSettlementException.ErrorCode, "{\"allowedOperation\":\"REFUND\"}"));
        using (voidClient)
        {
            var error = await Assert.ThrowsAsync<PhysicalPosVoidAfterSettlementException>(() =>
                voidClient.PhysicalPos.CreateReversalAsync("pay_1", new { operation = "VOID", currency = "TRY" }, Key));
            Assert.Equal(("REFUND", 409, Key), (error.AllowedOperation, error.StatusCode, error.IdempotencyKey));
        }

        var (cardClient, _) = Scripted(0, () => Error(HttpStatusCode.BadRequest, PhysicalPosCardDataRejectedException.ErrorCode, "{\"field\":\"merchantReference\"}"));
        using (cardClient)
        {
            var error = await Assert.ThrowsAsync<PhysicalPosCardDataRejectedException>(() => cardClient.PhysicalPos.CreatePaymentAsync(Sale(), Key));
            Assert.Equal(("merchantReference", 400), (error.Field, error.StatusCode));
        }

        var (badClient, badRequests) = Scripted(2, () => Error(HttpStatusCode.BadRequest, "physical_pos.input.invalid"));
        using (badClient)
        {
            await Assert.ThrowsAsync<LunixiValidationException>(() => badClient.PhysicalPos.CreatePaymentAsync(Sale(), Key));
            Assert.Single(badRequests);
        }
    }

    [Fact]
    public async Task ReadsReturnTheRecordAndPagesFollowTheEnvelope()
    {
        var (client, requests) = RecordingClient.Create(request => request.RequestUri!.AbsolutePath.EndsWith("/payments", StringComparison.Ordinal)
            ? FakeHandler.Json("{\"data\":{\"items\":[{\"paymentId\":\"pay_2\",\"amountMinor\":\"100\"}]},\"pageInfo\":{\"hasMore\":true,\"nextCursor\":\"c2\"}}")
            : FakeHandler.Json(PaymentBody("pay_1", status: "APPROVED")));
        using (client)
        {
            var payment = await client.PhysicalPos.GetPaymentAsync("pay_1");
            Assert.Equal("APPROVED", payment.GetProperty("paymentStatus").GetString());
            var page = await client.PhysicalPos.ListPaymentsAsync(new Dictionary<string, object?>
            {
                ["limit"] = 50,
                ["paymentStatus"] = "APPROVED",
                ["deviceId"] = "",
                ["cursor"] = null,
                ["createdFrom"] = new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.FromHours(3)),
                ["merchantReference"] = "ORDER 1001",
            });
            Assert.Equal(("pay_2", true, "c2", (long?)null), (page.Items[0].GetProperty("paymentId").GetString(), page.HasMore, page.NextCursor, page.TotalCount));
            Assert.Equal("/api/v1/physical-pos/payments/pay_1", requests[0].PathAndQuery);
            Assert.Equal("/api/v1/physical-pos/payments?limit=50&paymentStatus=APPROVED&createdFrom=2026-11-15T07%3A00%3A00.000Z&merchantReference=ORDER%201001", requests[1].PathAndQuery);
            Assert.All(requests, request => Assert.True(request.Signed));
        }
    }

    [Fact]
    public async Task FiltersAreCheckedBeforeSending()
    {
        var (client, requests) = RecordingClient.Create();
        using (client)
        {
            Task<PhysicalPosPage> List(string name, object? value) => client.PhysicalPos.ListTransactionsAsync(new Dictionary<string, object?> { [name] = value });
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => List("status", "APPROVED"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => List("limit", "10"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => List("limit", 0));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => List("limit", 101));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => List("amountFromMinor", "0"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => List("amountFromMinor", 100));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => List("amountToMinor", null));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => List("deviceId", 7));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PhysicalPos.ListSettlementRunsAsync(new Dictionary<string, object?> { ["includeTotal"] = "true" }));
            Assert.Equal("merchantReference", (await Assert.ThrowsAsync<PhysicalPosCardDataRejectedException>(() => List("merchantReference", "4111-1111-1111-1111"))).Field);
            Assert.Equal("pan", (await Assert.ThrowsAsync<PhysicalPosCardDataRejectedException>(() => List("pan", "x"))).Field);
            Assert.Empty(requests);

            await List("amountFromMinor", "1000");
            await client.PhysicalPos.ListSettlementRunsAsync(new Dictionary<string, object?> { ["includeTotal"] = true, ["runStatus"] = "FAILED" });
            await client.PhysicalPos.GetSalesSummaryAsync(new Dictionary<string, object?> { ["dateFrom"] = new DateOnly(2026, 11, 1), ["dateTo"] = "2026-11-15", ["groupBy"] = "DAY" });
            await client.PhysicalPos.GetTaxSummaryAsync(new Dictionary<string, object?> { ["dateFrom"] = new DateTimeOffset(2026, 11, 2, 23, 30, 0, TimeSpan.FromHours(3)) });
            Assert.Equal(new[]
            {
                "/api/v1/physical-pos/transactions?amountFromMinor=1000",
                "/api/v1/physical-pos/settlement-runs?includeTotal=true&runStatus=FAILED",
                "/api/v1/physical-pos/reports/sales-summary?dateFrom=2026-11-01&dateTo=2026-11-15&groupBy=DAY",
                "/api/v1/physical-pos/reports/tax-summary?dateFrom=2026-11-02",
            }, requests.Select(r => r.PathAndQuery));
        }
    }

    [Fact]
    public async Task IteratorsFollowTheCursor()
    {
        var (client, requests) = RecordingClient.Create(request => request.RequestUri!.Query.Contains("cursor=c2", StringComparison.Ordinal)
            ? FakeHandler.Json("{\"data\":{\"items\":[{\"runId\":\"r3\"}]},\"pageInfo\":{\"hasMore\":false}}")
            : FakeHandler.Json("{\"data\":{\"items\":[{\"runId\":\"r1\"},{\"runId\":\"r2\"}]},\"pageInfo\":{\"hasMore\":true,\"nextCursor\":\"c2\",\"totalCount\":3}}"));
        using (client)
        {
            var runs = new List<string>();
            await foreach (var run in client.PhysicalPos.IterateSettlementRunsAsync(new Dictionary<string, object?> { ["deviceId"] = "dev_1" }))
                runs.Add(run.GetProperty("runId").GetString()!);
            Assert.Equal(new[] { "r1", "r2", "r3" }, runs);
            Assert.Equal(new[] { "/api/v1/physical-pos/settlement-runs?deviceId=dev_1", "/api/v1/physical-pos/settlement-runs?cursor=c2&deviceId=dev_1" },
                requests.Select(r => r.PathAndQuery));
        }
    }

    [Fact]
    public async Task StartSettlementSendsAKeyAndReadsTheRun()
    {
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json("{\"data\":{\"run\":{\"runId\":\"run_1\"},\"replayed\":false}}", HttpStatusCode.Accepted));
        using (client)
        {
            var generated = await client.PhysicalPos.StartSettlementAsync("dev_1");
            var given = await client.PhysicalPos.StartSettlementAsync("dev_1", "eod-dev_1-2026-11-15");
            Assert.Equal(("POST", "/api/v1/physical-pos/devices/dev_1/settlement", "{}"), (requests[0].Method, requests[0].PathAndQuery, requests[0].Body));
            Assert.True(Guid.TryParse(requests[0].IdempotencyKey, out _));
            Assert.Equal(requests[0].IdempotencyKey, generated.IdempotencyKey);
            Assert.Equal("eod-dev_1-2026-11-15", requests[1].IdempotencyKey);
            Assert.Equal(("run_1", 202), (given.Entity!.Value.GetProperty("runId").GetString(), given.StatusCode));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PhysicalPos.StartSettlementAsync("dev_1", ""));
            Assert.Equal(2, requests.Count);
        }
    }

    [Fact]
    public async Task InvalidResponseMoneyAndTypedReadErrorsAreReported()
    {
        var (client, _) = RecordingClient.Create(request => request.RequestUri!.AbsolutePath.EndsWith("/pay_1", StringComparison.Ordinal)
            ? FakeHandler.Json("{\"data\":{\"payment\":{\"paymentId\":\"pay_1\",\"amountMinor\":4500}}}")
            : Error((HttpStatusCode)429, "RATE_LIMITED", "{\"retryAfterSeconds\":\"3\"}"));
        using (client)
        {
            await Assert.ThrowsAsync<LunixiTransportException>(() => client.PhysicalPos.GetPaymentAsync("pay_1"));
            var limited = await Assert.ThrowsAsync<PhysicalPosRateLimitException>(() => client.PhysicalPos.ListPaymentsAsync());
            Assert.Equal(3d, limited.RetryAfterSeconds);
            Assert.Null(limited.IdempotencyKey);
        }
    }
}
