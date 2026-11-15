using System.Text.Json;

namespace Lunixi.Sdk.Tests;

/// <summary>Contract source: the PHP SDK's identity client and the Node SDK's table. Names are fictional.</summary>
public class IdentityClientTests
{
    [Fact]
    public async Task AnalyzeSendsTheSignalBodyBearerOnly()
    {
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json(
            "{\"success\":true,\"data\":{\"traceId\":\"tr_1\",\"analysisStatus\":\"completed\",\"riskFlags\":[\"same_device_cluster\"]}}"));
        using (client)
        {
            var analysis = await client.Identity.AnalyzeSyncAsync(new
            {
                subjectType = "user",
                subjectId = "42",
                device = new { deviceId = "fp_1", ipAddress = "203.0.113.7" },
                account = new { accountId = "42" },
            });
            await client.Identity.AnalyzeAsync(new { paymentId = "pay_1" });

            Assert.Equal("tr_1", analysis.GetProperty("traceId").GetString());
            Assert.Equal(new[] { "/api/v1/identity/live-sessions/analyze-sync", "/api/v1/identity/live-sessions/analyze" }, requests.Select(r => r.PathAndQuery));
            Assert.All(requests, request => Assert.False(request.Signed));
            Assert.Equal("{\"subjectType\":\"user\",\"subjectId\":\"42\",\"device\":{\"deviceId\":\"fp_1\",\"ipAddress\":\"203.0.113.7\"},\"account\":{\"accountId\":\"42\"}}", requests[0].Body);
            Assert.Equal("{\"paymentId\":\"pay_1\"}", requests[1].Body);
        }
    }

    [Fact]
    public async Task InvalidAnalysesFailBeforeSending()
    {
        var (client, requests) = RecordingClient.Create();
        using (client)
        {
            var pair = await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Identity.AnalyzeSyncAsync(new { subjectId = "42" }));
            Assert.Contains("subjectType and subjectId", pair.Message);
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Identity.AnalyzeSyncAsync(new { verdict = "allow" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Identity.AnalyzeAsync(new { device = "fp_1" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() =>
                client.Identity.ListSessionsAsync(new Dictionary<string, object?> { ["email"] = "ada@example.com" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Identity.AccountLinksAsync(" "));
            Assert.Empty(requests);
        }
    }

    [Fact]
    public async Task AccountLinksTellWhetherTheDeviceIsShared()
    {
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json(
            "{\"success\":true,\"data\":{\"totalLinkedSessions\":3,\"linkedAccounts\":[\"h_a\"],\"sessions\":[{\"linkReasons\":[\"same_fingerprint\"]},{\"linkReasons\":[\"same_fingerprint\",\"same_ip_asn\"]}]}}"));
        using (client)
        {
            var links = await client.Identity.AccountLinksAsync("tr_1");
            Assert.Equal("/api/v1/identity/live-sessions/tr_1/account-links", Assert.Single(requests).PathAndQuery);
            Assert.True(IdentitySignals.IsDeviceShared(links));
            Assert.False(IdentitySignals.IsDeviceShared(links, 3));
            Assert.Equal(new[] { "same_fingerprint", "same_ip_asn" }, IdentitySignals.LinkReasons(links));
            Assert.False(IdentitySignals.IsDeviceShared(JsonDocument.Parse("{\"linkedAccounts\":[]}").RootElement));
        }
    }

    [Fact]
    public async Task SessionReadsAndLists()
    {
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json("{\"success\":true,\"data\":{\"items\":[{\"traceId\":\"tr_1\"}]}}"));
        using (client)
        {
            await client.Identity.GetSessionAsync("tr_1");
            await client.Identity.RelatedSessionsAsync("tr_1", 5);
            await client.Identity.TimelineAsync("tr_1");
            await client.Identity.TopSourcesAsync(new Dictionary<string, object?> { ["days"] = 7, ["limit"] = 10 });
            var page = await client.Identity.ListSessionsAsync(new Dictionary<string, object?> { ["deviceId"] = "fp_1" });

            Assert.Equal(new[]
            {
                "/api/v1/identity/live-sessions/tr_1",
                "/api/v1/identity/live-sessions/tr_1/related-sessions?limit=5",
                "/api/v1/identity/live-sessions/tr_1/timeline",
                "/api/v1/identity/live-sessions/top-sources?days=7&limit=10",
                "/api/v1/identity/live-sessions?deviceId=fp_1",
            }, requests.Select(r => r.PathAndQuery));
            Assert.All(requests, request => Assert.Equal("GET", request.Method));
            Assert.Equal("tr_1", Assert.Single(page.Items).GetProperty("traceId").GetString());
        }
    }
}
