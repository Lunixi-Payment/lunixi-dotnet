using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace Lunixi.Sdk.Tests;

public class WalletClientTests
{
    [Fact]
    public async Task RoutesAreSignedAndFollowTheTable()
    {
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json("{\"data\":{\"id\":\"eu_1\"}}"));
        using (client)
        {
            var wallet = client.Wallet;
            var created = await wallet.EndUsers.CreateAsync(new { programId = "pr_1", accountType = WalletAccountTypes.Personal, phone = "+905551112233" }, "end-user-create-0001");
            await wallet.EndUsers.GetByExternalIdAsync("cust 7", "pr_1");
            await wallet.EndUsers.FreezeAsync("eu_1", "end-user-freeze-0001");
            await wallet.EndUsers.UpdateKycLevelAsync("eu_1", new { kycLevel = WalletKycLevels.Substantial, sourceRef = "kyc_1" }, "kyc-level-update-01");
            await wallet.Transfers.InitiateW2WAsync(new { endUserId = "eu_1", sourceWalletAccountId = "wa_1", amount = "12550", currency = "TRY", destinationWalletNo = "100200" }, "w2w-initiate-000001");
            await wallet.Transfers.CompleteW2WAsync("op_1", new { endUserId = "eu_1", integrityHash = "h", nonce = "n", otpCode = "123456" }, "w2w-complete-000001");
            await wallet.Topups.RefundAsync("op_2", new { reason = "duplicate", requesterPrincipal = "alice", approverPrincipal = "bob" });
            await wallet.Qr.RevokeAsync("qr_1");
            await wallet.BulkPayouts.ApproveAsync("bp_1", approve: false, reason: "wrong batch");
            await wallet.PingAsync();

            Assert.Equal("eu_1", created.GetProperty("id").GetString());
            Assert.Equal(new[]
            {
                "POST /api/v1/wallet/end-users",
                "GET /api/v1/wallet/end-users/by-external/cust%207?programId=pr_1",
                "POST /api/v1/wallet/end-users/eu_1/freeze",
                "PUT /api/v1/wallet/end-users/eu_1/kyc-level",
                "POST /api/v1/wallet/transfers/w2w",
                "POST /api/v1/wallet/transfers/w2w/op_1/complete",
                "POST /api/v1/wallet/topups/op_2/refund",
                "POST /api/v1/wallet/qr/qr_1/revoke",
                "POST /api/v1/wallet/bulk-payouts/bp_1/approve",
                "GET /api/v1/wallet/ping",
            }, requests.Select(r => r.Method + " " + r.PathAndQuery));
            Assert.All(requests, request => Assert.True(request.Signed));
            Assert.Equal("{\"programId\":\"pr_1\",\"accountType\":\"PERSONAL\",\"phone\":\"+905551112233\"}", requests[0].Body);
            Assert.Equal("end-user-create-0001", requests[0].IdempotencyKey);
            Assert.Null(requests[1].IdempotencyKey);
            Assert.Equal("{}", requests[2].Body);
            Assert.Equal("{\"endUserId\":\"eu_1\",\"integrityHash\":\"h\",\"nonce\":\"n\",\"otpCode\":\"123456\",\"operationId\":\"op_1\"}", requests[5].Body);
            Assert.Null(requests[6].IdempotencyKey);
            Assert.Null(requests[7].Body);
            Assert.Equal("{\"approve\":false,\"reason\":\"wrong batch\"}", requests[8].Body);
        }
    }

    [Fact]
    public async Task NestedRowsAreCheckedElementByElement()
    {
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json());
        using (client)
        {
            await client.Wallet.BulkPayouts.CreateAsync(new
            {
                programId = "pr_1",
                sourceWalletAccountId = "wa_1",
                currency = "TRY",
                items = new object[] { new { targetType = WalletBulkTargetTypes.Iban, targetRef = "TR00", amount = "5000", beneficiaryName = "A" } },
            }, "bulk-create-000001");
            await client.Wallet.Corporate.ImportEmployeesAsync("ca_1", [new { phone = "+905551112233", displayName = "B" }], "corporate-import-01");
            Assert.Equal(2, requests.Count);

            var bad = new[]
            {
                new { targetType = "WALLET", targetRef = "100", amount = (object)5000 },
            };
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Wallet.BulkPayouts.CreateAsync(
                new { programId = "pr_1", sourceWalletAccountId = "wa_1", currency = "TRY", items = bad }, "bulk-create-000002"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Wallet.BulkPayouts.CreateAsync(
                new { programId = "pr_1", sourceWalletAccountId = "wa_1", currency = "TRY", items = new object[] { new { targetType = "WALLET", targetRef = "100", amount = "1", iban = "x" } } },
                "bulk-create-000003"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Wallet.BulkPayouts.CreateAsync(
                new { programId = "pr_1", sourceWalletAccountId = "wa_1", currency = "TRY", items = Array.Empty<object>() }, "bulk-create-000004"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Wallet.Corporate.ImportEmployeesAsync("ca_1", [new { displayName = "no phone" }], "corporate-import-02"));
            Assert.Equal(2, requests.Count);
        }
    }

    [Fact]
    public async Task InvalidCallsFailBeforeSending()
    {
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json());
        using (client)
        {
            var wallet = client.Wallet;
            var quote = new JsonObject { ["endUserId"] = "eu_1", ["sourceWalletAccountId"] = "wa_1", ["amount"] = "100", ["currency"] = "TRY" };

            await Assert.ThrowsAsync<LunixiConfigurationException>(() => wallet.Transfers.QuoteW2WAsync(new { endUserId = "eu_1", sourceWalletAccountId = "wa_1", amount = "100", currency = "TRY", businessAccountId = "b" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => wallet.Transfers.QuoteW2WAsync(new { endUserId = "eu_1", amount = "100", currency = "TRY" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => wallet.Transfers.QuoteW2WAsync(new { endUserId = "eu_1", sourceWalletAccountId = "wa_1", amount = 100, currency = "TRY" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => wallet.Transfers.QuoteW2WAsync(new { endUserId = "eu_1", sourceWalletAccountId = "wa_1", amount = "125.50", currency = "TRY" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => wallet.Transfers.QuoteW2WAsync(new { endUserId = "eu_1", sourceWalletAccountId = "wa_1", amount = "0", currency = "TRY" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => wallet.Transfers.InitiateW2WAsync(quote, ""));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => wallet.Transfers.CompleteW2WAsync("op_1", new { endUserId = "eu_1", operationId = "op_2", integrityHash = "h", nonce = "n" }, "w2w-complete-000002"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => wallet.EndUsers.ListAsync(new Dictionary<string, object?> { ["environment"] = "LIVE" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => wallet.EndUsers.GetAsync(" "));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => wallet.Qr.ImageAsync("qr_1", "jpg"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => wallet.Qr.ImageAsync("qr_1", WalletQrImageFormats.Png, 21));
            Assert.Empty(requests);
        }
    }

    [Fact]
    public async Task ListsReadPagesAndIterateWithTheCursor()
    {
        var pages = new Queue<string>(new[]
        {
            "{\"data\":{\"operations\":[{\"id\":\"op_1\"}],\"nextCursor\":\"c2\"},\"pageInfo\":{\"hasMore\":true,\"nextCursor\":\"c2\"}}",
            "{\"data\":{\"operations\":[{\"id\":\"op_2\"}],\"nextCursor\":null},\"pageInfo\":{\"hasMore\":false,\"nextCursor\":null}}",
        });
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json(pages.Dequeue()));
        using (client)
        {
            var ids = new List<string>();
            await foreach (var operation in client.Wallet.Operations.IterateAsync(new Dictionary<string, object?> { ["endUserId"] = "eu_1", ["limit"] = 1 }))
                ids.Add(operation.GetProperty("id").GetString()!);

            Assert.Equal(new[] { "op_1", "op_2" }, ids);
            Assert.Equal("/api/v1/wallet/operations?endUserId=eu_1&limit=1", requests[0].PathAndQuery);
            Assert.Equal("/api/v1/wallet/operations?endUserId=eu_1&limit=1&cursor=c2", requests[1].PathAndQuery);
        }
    }

    [Fact]
    public async Task QrImageIsReturnedAsBytes()
    {
        var svg = Encoding.UTF8.GetBytes("<svg/>");
        var (client, requests) = RecordingClient.Create(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(svg) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/svg+xml");
            return response;
        });
        using (client)
        {
            var image = await client.Wallet.Qr.ImageAsync("qr_1", WalletQrImageFormats.Svg, 4);
            Assert.Equal("image/svg+xml", image.ContentType);
            Assert.Equal(svg, image.ToArray());
            Assert.Equal("/api/v1/wallet/qr/qr_1/image?format=svg&scale=4", requests[0].PathAndQuery);
            Assert.True(requests[0].Signed);
        }
    }

    [Fact]
    public void TheRouteTableKeepsTheNodeOrderAndKeyRules()
    {
        Assert.Equal(60, WalletRoutes.All.Count);
        Assert.Equal("ping", WalletRoutes.All.Keys.First());
        Assert.Equal("retryFailedBulkPayout", WalletRoutes.All.Keys.Last());
        Assert.True(WalletRoutes.All["completeBankWithdrawal"].IdempotencyKey);
        Assert.False(WalletRoutes.All["refundTopup"].IdempotencyKey);
        Assert.True(WalletRoutes.All["getQrImage"].Binary);
        Assert.Equal(new[] { "reason", "requesterPrincipal", "approverPrincipal" }, WalletRoutes.All["refundTopup"].Required);
    }
}
