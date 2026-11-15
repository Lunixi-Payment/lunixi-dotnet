using static Lunixi.Sdk.Tests.PhysicalPosTestSupport;

namespace Lunixi.Sdk.Tests;

public class SanctionClientTests
{
    [Fact]
    public async Task ScreeningsAreUnsignedAndFollowTheTable()
    {
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json(
            "{\"status\":\"success\",\"data\":{\"screeningRequestId\":\"sr_1\",\"matches\":[],\"coverageStatus\":\"COMPLETE\"}}"));
        using (client)
        {
            var result = await client.Sanction.ScreenNameAsync(new
            {
                name = "Jane Doe",
                subjectType = SanctionSubjectTypes.Person,
                mode = SanctionScreenModes.SanctionsPep,
                riskDomains = new[] { SanctionRiskDomains.Sanctions, SanctionRiskDomains.Pep },
                identifiers = new[] { new { type = "passport", value = "X1" } },
            });
            await client.Sanction.ScreenTransactionAsync(new { amount = "1000", accounts = new[] { new { accountType = "IBAN", value = "TR00" } } });
            await client.Sanction.GetIndirectExposureAsync("ent 1", 2, ["e1", " e2 "]);
            await client.Sanction.ListMonitoredEntitiesAsync(new Dictionary<string, object?> { ["activeOnly"] = false, ["limit"] = 10, ["includeTotal"] = true });

            Assert.Equal("sr_1", result.GetProperty("screeningRequestId").GetString());
            Assert.True(SanctionClient.IsClearScreening(result));
            Assert.Equal(new[]
            {
                "POST /api/v1/sanction/screening/name",
                "POST /api/v1/sanction/screening/transaction",
                "GET /api/v1/sanction/intelligence/indirect-exposure/ent%201?maxDepth=2&entityIds=e1%2Ce2",
                "GET /api/v1/sanction/monitoring/entities?activeOnly=false&limit=10&includeTotal=true",
            }, requests.Select(r => r.Method + " " + r.PathAndQuery));
            Assert.All(requests, request => Assert.False(request.Signed));
            Assert.Equal(
                "{\"name\":\"Jane Doe\",\"subjectType\":\"person\",\"mode\":\"sanctions_pep\",\"riskDomains\":[\"sanctions\",\"pep\"],\"identifiers\":[{\"type\":\"passport\",\"value\":\"X1\"}]}",
                requests[0].Body);
        }
    }

    [Fact]
    public async Task InvalidScreeningsFailBeforeSending()
    {
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json());
        using (client)
        {
            var sanction = client.Sanction;
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => sanction.ScreenNameAsync(new { subjectType = "person" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => sanction.ScreenNameAsync(new { name = "A", nationality = "TR" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => sanction.ScreenNameAsync(new { name = "A", mode = "everything" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => sanction.ScreenNameAsync(new { name = "A", riskDomains = new[] { "sanctions", "crime" } }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => sanction.ScreenNameAsync(new { name = "A", identifiers = new[] { new { type = "passport" } } }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => sanction.ScreenWalletAsync(new { walletAddress = "0xabc", mode = SanctionScreenModes.FullRisk }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => sanction.ScreenTransactionAsync(new { amount = "1000", parties = Array.Empty<object>() }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => sanction.ScreenTransactionAsync(new { parties = new[] { new { name = "A", iban = "TR00" } } }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => sanction.ScreenPublicProcurementBanAsync(new { onlyActive = true }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => sanction.RegisterMonitoringAsync(new { externalCustomerId = "c1", profile = new { name = "A" }, profileJson = "{}" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => sanction.RegisterMonitoringAsync(new { externalCustomerId = "c1", riskDomainScope = new[] { "weather" } }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => sanction.ListScreeningHistoryAsync(new Dictionary<string, object?> { ["status"] = "open" }));
            Assert.Empty(requests);
        }
    }

    [Fact]
    public async Task MonitoringProfileIsSentAsProfileJson()
    {
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json("{\"status\":\"success\",\"data\":{\"monitoredEntityId\":\"me_1\"}}"));
        using (client)
        {
            var registered = await client.Sanction.RegisterMonitoringAsync(new { externalCustomerId = "c1", subjectType = "person", profile = new { name = "Jane", country = "TR" } });
            Assert.Equal("me_1", registered.GetProperty("monitoredEntityId").GetString());
            Assert.Equal("{\"externalCustomerId\":\"c1\",\"subjectType\":\"person\",\"profileJson\":\"{\\\"name\\\":\\\"Jane\\\",\\\"country\\\":\\\"TR\\\"}\"}", requests[0].Body);
        }
    }

    [Fact]
    public async Task AScreeningThatDidNotCompleteThrows()
    {
        var (client, _) = RecordingClient.Create(_ => FakeHandler.Json("{\"status\":\"failure\",\"code\":\"sanction.screen_name.failed\",\"data\":{\"matches\":[]}}"));
        using (client)
        {
            var error = await Assert.ThrowsAsync<SanctionScreeningException>(() => client.Sanction.ScreenNameAsync(new { name = "Jane" }));
            Assert.Equal("sanction.screen_name.failed", error.Code);
            Assert.IsAssignableFrom<LunixiApiException>(error);
            await Assert.ThrowsAsync<SanctionScreeningException>(() => client.Sanction.GetDatasetFreshnessAsync());
        }
    }

    [Theory]
    [InlineData("{\"matches\":[],\"coverageStatus\":\"COMPLETE\"}", true)]
    [InlineData("{\"matches\":[],\"coverageStatus\":\"PARTIAL\"}", false)]
    [InlineData("{\"matches\":[],\"coverageStatus\":\"DEGRADED\"}", false)]
    [InlineData("{\"matches\":[],\"coverageStatus\":null}", false)]
    [InlineData("{\"matches\":[{\"entityId\":\"e1\"}],\"coverageStatus\":\"COMPLETE\"}", false)]
    [InlineData("{\"hits\":[],\"publicationHits\":[{\"riskCategory\":\"sanctions\"}],\"coverageStatus\":\"COMPLETE\"}", false)]
    [InlineData("{\"isSanctioned\":true}", false)]
    [InlineData("{\"matched\":true,\"coverageStatus\":\"COMPLETE\"}", false)]
    [InlineData("{\"caseRequired\":true}", false)]
    [InlineData("{\"decision\":\"BLOCK\"}", false)]
    [InlineData("{\"decision\":\"allow\",\"hits\":[]}", true)]
    [InlineData("{\"hits\":[]}", true)]
    [InlineData("[]", false)]
    public void ClearReadingFollowsTheCoverageRule(string json, bool clear)
        => Assert.Equal(clear, SanctionClient.IsClearScreening(Element(json)));

    [Fact]
    public void ValueSetsMatchTheRouteTable()
    {
        Assert.Equal(19, SanctionRoutes.All.Count);
        Assert.True(SanctionRoutes.All.Values.Where(route => route.Method == "POST").All(route => route.HasBody));
        Assert.Equal(new[] { "parties", "accounts" }, SanctionRoutes.All["screenTransaction"].AnyOf);
        foreach (var key in SanctionRoutes.ValueSets.Keys.Concat(SanctionRoutes.NestedFields.Keys))
        {
            var (route, field) = (key.Split('.')[0], key.Split('.')[1]);
            Assert.Contains(field, SanctionRoutes.All[route].Optional);
        }
    }
}
