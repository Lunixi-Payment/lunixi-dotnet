using System.Net;
using System.Text.Json.Nodes;
using static Lunixi.Sdk.Tests.PhysicalPosTestSupport;

namespace Lunixi.Sdk.Tests;

public class PhysicalPosManagementTests
{
    [Fact]
    public async Task KeyedCreatesSendAKeyAndReadTheRecord()
    {
        var (client, requests) = RecordingClient.Create(request => FakeHandler.Json(
            "{\"data\":{\"store\":{\"id\":\"st_1\"},\"register\":{\"id\":\"rg_1\"},\"replayed\":false}}", HttpStatusCode.Created));
        using (client)
        {
            var store = await client.PhysicalPosManagement.CreateStoreAsync(new { name = "Kadıköy", code = "KDK", city = "", timezone = "Europe/Istanbul" });
            var register = await client.PhysicalPosManagement.CreateRegisterAsync("st_1", new { code = "R1", name = "Till 1" }, "register-create-0001");

            Assert.Equal(("POST", "/api/v1/physical-pos/stores"), (requests[0].Method, requests[0].PathAndQuery));
            Assert.Equal("{\"code\":\"KDK\",\"name\":\"Kadıköy\",\"timezone\":\"Europe/Istanbul\"}", requests[0].Body);
            Assert.True(Guid.TryParse(requests[0].IdempotencyKey, out _));
            Assert.Equal((201, false, requests[0].IdempotencyKey), (store.StatusCode, store.Replayed, store.IdempotencyKey));
            Assert.Equal("st_1", store.Entity!.Value.GetProperty("id").GetString());
            Assert.Equal(("/api/v1/physical-pos/stores/st_1/registers", "register-create-0001"), (requests[1].PathAndQuery, requests[1].IdempotencyKey));
            Assert.Equal("rg_1", register.Entity!.Value.GetProperty("id").GetString());
            Assert.All(requests, request => Assert.True(request.Signed));
        }
    }

    [Fact]
    public async Task UpdatesCarryTheRowVersionAndExplicitClears()
    {
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json("{\"data\":{\"store\":{\"id\":\"st_1\",\"rowVersion\":4}}}"));
        using (client)
        {
            var store = await client.PhysicalPosManagement.UpdateStoreAsync("st_1", new JsonObject { ["rowVersion"] = 3, ["city"] = null, ["timezone"] = null, ["name"] = "Moda" });
            Assert.Equal(4, store.GetProperty("rowVersion").GetInt32());
            Assert.Equal(("PATCH", "/api/v1/physical-pos/stores/st_1"), (requests[0].Method, requests[0].PathAndQuery));
            Assert.Equal("{\"rowVersion\":3,\"name\":\"Moda\",\"city\":null}", requests[0].Body);
            Assert.Null(requests[0].IdempotencyKey);

            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PhysicalPosManagement.UpdateStoreAsync("st_1", new { rowVersion = 0, name = "x" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PhysicalPosManagement.UpdateStoreAsync("st_1", new { rowVersion = "3", name = "x" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PhysicalPosManagement.UpdateStoreAsync("st_1", new { rowVersion = 2_147_483_648L }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PhysicalPosManagement.UpdateStoreAsync("st_1", new { name = "x" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.PhysicalPosManagement.ArchiveStoreAsync("st_1", new { rowVersion = 3 }));
            await Assert.ThrowsAsync<PhysicalPosCardDataRejectedException>(() => client.PhysicalPosManagement.UpdateDeviceAsync("dev_1", new { rowVersion = 3, cardNumber = "x" }));
            Assert.Single(requests);
        }
    }

    [Fact]
    public async Task StoreRegisterAndTerminalRoutes()
    {
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json("{\"data\":{}}"));
        using (client)
        {
            var change = new { rowVersion = 2, reasonCode = "MERCHANT_REQUEST" };
            var management = client.PhysicalPosManagement;
            await management.GetStoreAsync("st_1");
            await management.ArchiveStoreAsync("st_1", change);
            await management.RestoreStoreAsync("st_1", change);
            await management.UpdateRegisterAsync("rg_1", new { rowVersion = 2, name = "Till 2" });
            await management.ArchiveRegisterAsync("rg_1", change);
            await management.RestoreRegisterAsync("rg_1", change);
            await management.CreateTerminalAsync(new { code = "T1", displayName = "Front", vendorCode = "VENDOR_A" }, "terminal-create-01");
            await management.GetTerminalAsync("tm_1");
            await management.UpdateTerminalAsync("tm_1", new { rowVersion = 2, displayName = "Back" });
            await management.CloseTerminalAsync("tm_1", change);
            await management.AddTerminalAcquirerAsync("tm_1", new { vendorAcquirerRef = "acq", merchantNumber = "m1", terminalNumber = "t1", currencies = new[] { "TRY" } }, "acquirer-add-0001");
            await management.UpdateTerminalAcquirerAsync("ta_1", new JsonObject { ["rowVersion"] = 2, ["label"] = null, ["currencies"] = new JsonArray("TRY", "EUR") });
            await management.AddTerminalProviderAsync("tm_1", new { vendorAccountId = "va_1", merchantCode = "MC", terminalCode = "TC" }, "provider-add-0001");
            await management.UpdateTerminalProviderAsync("tp_1", new { rowVersion = 2, status = "DISABLED" });
            await management.ChangeTerminalDeviceAsync("tm_1", new { rowVersion = 2, deviceId = "dev_1", reasonCode = "DEVICE_REPLACED" });
            await management.CloseTerminalDeviceLinkAsync("tm_1", change, "link-close-000001");

            Assert.Equal(new[]
            {
                "GET /api/v1/physical-pos/stores/st_1",
                "POST /api/v1/physical-pos/stores/st_1/archive",
                "POST /api/v1/physical-pos/stores/st_1/restore",
                "PATCH /api/v1/physical-pos/registers/rg_1",
                "POST /api/v1/physical-pos/registers/rg_1/archive",
                "POST /api/v1/physical-pos/registers/rg_1/restore",
                "POST /api/v1/physical-pos/terminals",
                "GET /api/v1/physical-pos/terminals/tm_1",
                "PATCH /api/v1/physical-pos/terminals/tm_1",
                "POST /api/v1/physical-pos/terminals/tm_1/close",
                "POST /api/v1/physical-pos/terminals/tm_1/acquirers",
                "PATCH /api/v1/physical-pos/terminal-acquirers/ta_1",
                "POST /api/v1/physical-pos/terminals/tm_1/providers",
                "PATCH /api/v1/physical-pos/terminal-providers/tp_1",
                "POST /api/v1/physical-pos/terminals/tm_1/device-link",
                "POST /api/v1/physical-pos/terminals/tm_1/device-link/close",
            }, requests.Select(r => r.Method + " " + r.PathAndQuery));
            Assert.Equal("{\"rowVersion\":2,\"reasonCode\":\"MERCHANT_REQUEST\"}", requests[1].Body);
            Assert.Equal("{\"vendorAcquirerRef\":\"acq\",\"merchantNumber\":\"m1\",\"terminalNumber\":\"t1\",\"currencies\":[\"TRY\"]}", requests[10].Body);
            Assert.Equal("{\"rowVersion\":2,\"label\":null,\"currencies\":[\"TRY\",\"EUR\"]}", requests[11].Body);
            Assert.Equal(new[] { null, null, null, null, null, null, "terminal-create-01", null, null, null, "acquirer-add-0001", null, "provider-add-0001", null, null, "link-close-000001" },
                requests.Select(r => r.IdempotencyKey));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => management.AddTerminalAcquirerAsync("tm_1",
                new { vendorAcquirerRef = "acq", merchantNumber = "m1", terminalNumber = "t1", currencies = new[] { "" } }));
            Assert.All(requests, request => Assert.True(request.Signed));
        }
    }

    [Fact]
    public async Task DeviceAndIdentityRoutes()
    {
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json("{\"data\":{\"device\":{\"deviceId\":\"dev_1\"},\"closedAssignment\":{\"id\":\"as_1\"}}}"));
        using (client)
        {
            var management = client.PhysicalPosManagement;
            var created = await management.CreateDeviceAsync(new
            {
                vendorCode = "VENDOR_A",
                deviceModelId = "model_1",
                serialNumber = "SN-001",
                integrationChannel = "CLOUD",
                storeId = "st_1",
            });
            var device = await management.GetDeviceAsync("dev_1");
            await management.UpdateDeviceAsync("dev_1", new JsonObject { ["rowVersion"] = 5, ["appMode"] = null });
            await management.AssignDeviceAsync("dev_1", new { rowVersion = 5, storeId = "st_2", reasonCode = "RELOCATION" });
            var closed = await management.CloseDeviceAssignmentAsync("dev_1", new { rowVersion = 6, reasonCode = "STORAGE" });
            await management.ChangeDeviceLifecycleAsync("dev_1", new { rowVersion = 7, target = "SUSPENDED", reasonCode = "MAINTENANCE" });
            await management.RetireDeviceAsync("dev_1", new { rowVersion = 8, reasonCode = "END_OF_LIFE" }, "device-retire-0001");
            await management.RevokeDeviceKeyAsync("dev_1", "key/1", new { rowVersion = 1, reasonCode = "KEY_COMPROMISED" });
            await management.RevertDeviceEnrollmentAsync("dev_1", "key_1", new { reasonCode = "NOT_MY_DEVICE" });
            await management.RevokeActivationCodeAsync("dev_1", new { reasonCode = "MERCHANT_REVOKED" });

            Assert.Equal("dev_1", device.GetProperty("deviceId").GetString());
            Assert.Equal("dev_1", created.Entity!.Value.GetProperty("deviceId").GetString());
            Assert.Equal("as_1", closed.Property("closedAssignment")!.Value.GetProperty("id").GetString());
            Assert.Equal(new[]
            {
                "POST /api/v1/physical-pos/devices",
                "GET /api/v1/physical-pos/devices/dev_1",
                "PATCH /api/v1/physical-pos/devices/dev_1",
                "POST /api/v1/physical-pos/devices/dev_1/assignment",
                "POST /api/v1/physical-pos/devices/dev_1/assignment/close",
                "POST /api/v1/physical-pos/devices/dev_1/lifecycle",
                "POST /api/v1/physical-pos/devices/dev_1/retire",
                "POST /api/v1/physical-pos/devices/dev_1/keys/key%2F1/revoke",
                "POST /api/v1/physical-pos/devices/dev_1/keys/key_1/revert-enrollment",
                "POST /api/v1/physical-pos/devices/dev_1/activation-code/revoke",
            }, requests.Select(r => r.Method + " " + r.PathAndQuery));
            Assert.Equal("{\"vendorCode\":\"VENDOR_A\",\"deviceModelId\":\"model_1\",\"serialNumber\":\"SN-001\",\"storeId\":\"st_1\",\"integrationChannel\":\"CLOUD\"}", requests[0].Body);
            Assert.Equal("{\"rowVersion\":5,\"appMode\":null}", requests[2].Body);
            Assert.Equal("{\"rowVersion\":5,\"storeId\":\"st_2\",\"reasonCode\":\"RELOCATION\"}", requests[3].Body);
            Assert.True(Guid.TryParse(requests[4].IdempotencyKey, out _));
            Assert.Equal("device-retire-0001", requests[6].IdempotencyKey);
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => management.CreateDeviceAsync(new { vendorCode = "V", deviceModelId = "m", serialNumber = "s" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => management.RevokeDeviceKeyAsync("dev_1", "..", new { rowVersion = 1, reasonCode = "OTHER" }));
            Assert.Equal(10, requests.Count);
        }
    }

    [Fact]
    public async Task SettlementPolicyLevelsReadThePolicy()
    {
        var (client, requests) = RecordingClient.Create(request => request.RequestUri!.AbsolutePath.EndsWith("/effective", StringComparison.Ordinal)
            ? FakeHandler.Json("{\"data\":{\"effective\":{\"sourceScopeType\":\"STORE\"}}}")
            : FakeHandler.Json("{\"data\":{\"policy\":{\"mode\":\"SCHEDULED\",\"rowVersion\":3}}}"));
        using (client)
        {
            var management = client.PhysicalPosManagement;
            var policy = await management.SetAccountSettlementPolicyAsync(new { mode = "SCHEDULED", scheduleTime = "23:30", scheduleDays = new[] { "MON", "TUE" }, retryWindowMinutes = 60, expectedRowVersion = 2 });
            await management.GetAccountSettlementPolicyAsync();
            await management.ClearAccountSettlementPolicyAsync(3);
            await management.SetStoreSettlementPolicyAsync("st_1", new { mode = "MANUAL" });
            await management.GetStoreSettlementPolicyAsync("st_1");
            await management.ClearStoreSettlementPolicyAsync("st_1");
            await management.SetDeviceSettlementPolicyAsync("dev_1", new { mode = "DEVICE_AUTO" });
            await management.GetDeviceSettlementPolicyAsync("dev_1");
            await management.ClearDeviceSettlementPolicyAsync("dev_1", 4);
            var effective = await management.GetEffectiveSettlementPolicyAsync("dev_1");

            Assert.Equal(3, policy.GetProperty("rowVersion").GetInt32());
            Assert.Equal("STORE", effective.GetProperty("sourceScopeType").GetString());
            Assert.Equal(new[]
            {
                "PUT /api/v1/physical-pos/settlement-policy",
                "GET /api/v1/physical-pos/settlement-policy",
                "DELETE /api/v1/physical-pos/settlement-policy?expectedRowVersion=3",
                "PUT /api/v1/physical-pos/stores/st_1/settlement-policy",
                "GET /api/v1/physical-pos/stores/st_1/settlement-policy",
                "DELETE /api/v1/physical-pos/stores/st_1/settlement-policy",
                "PUT /api/v1/physical-pos/devices/dev_1/settlement-policy",
                "GET /api/v1/physical-pos/devices/dev_1/settlement-policy",
                "DELETE /api/v1/physical-pos/devices/dev_1/settlement-policy?expectedRowVersion=4",
                "GET /api/v1/physical-pos/devices/dev_1/settlement-policy/effective",
            }, requests.Select(r => r.Method + " " + r.PathAndQuery));
            Assert.Equal("{\"mode\":\"SCHEDULED\",\"scheduleTime\":\"23:30\",\"scheduleDays\":[\"MON\",\"TUE\"],\"retryWindowMinutes\":60,\"expectedRowVersion\":2}", requests[0].Body);
            Assert.Null(requests[2].Body);
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => management.ClearAccountSettlementPolicyAsync(0));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => management.SetAccountSettlementPolicyAsync(new { scheduleTime = "23:30" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => management.SetAccountSettlementPolicyAsync(new { mode = "MANUAL", retryWindowMinutes = "60" }));
            Assert.Equal(10, requests.Count);
        }
    }

    [Fact]
    public async Task ListsCatalogAndOverview()
    {
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json(
            "{\"data\":{\"items\":[{\"id\":\"x\"}],\"overview\":{\"storeCount\":2}},\"pageInfo\":{\"hasMore\":false,\"totalCount\":1}}"));
        using (client)
        {
            var management = client.PhysicalPosManagement;
            var overview = await management.GetOverviewAsync();
            await management.ListVendorsAsync();
            await management.ListDeviceModelsAsync(new Dictionary<string, object?> { ["vendorCode"] = "VENDOR_A", ["deviceType"] = "SMART_POS" });
            await management.ListAcquirersAsync();
            var stores = await management.ListStoresAsync(new Dictionary<string, object?> { ["includeTotal"] = true, ["q"] = "Moda" });
            await management.ListDevicesAsync(new Dictionary<string, object?> { ["lifecycleStatus"] = "ACTIVE", ["limit"] = 25 });
            await management.ListStoreAuditAsync("st_1", new Dictionary<string, object?> { ["limit"] = 10 });
            await management.ListTerminalLinksAsync("tm_1");
            await management.ListDeviceKeysAsync("dev_1");
            var ids = new List<string>();
            await foreach (var row in management.IterateTerminalProvidersAsync("tm_1")) ids.Add(row.GetProperty("id").GetString()!);

            Assert.Equal(2, overview.GetProperty("storeCount").GetInt32());
            Assert.Equal((1L, false), (stores.TotalCount, stores.HasMore));
            Assert.Equal(new[] { "x" }, ids);
            Assert.Equal(new[]
            {
                "/api/v1/physical-pos/overview",
                "/api/v1/physical-pos/catalog/vendors",
                "/api/v1/physical-pos/catalog/device-models?vendorCode=VENDOR_A&deviceType=SMART_POS",
                "/api/v1/physical-pos/catalog/acquirers",
                "/api/v1/physical-pos/stores?includeTotal=true&q=Moda",
                "/api/v1/physical-pos/devices?limit=25&lifecycleStatus=ACTIVE",
                "/api/v1/physical-pos/stores/st_1/audit?limit=10",
                "/api/v1/physical-pos/terminals/tm_1/links",
                "/api/v1/physical-pos/devices/dev_1/keys",
                "/api/v1/physical-pos/terminals/tm_1/providers",
            }, requests.Select(r => r.PathAndQuery));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => management.ListStoreAuditAsync("st_1", new Dictionary<string, object?> { ["status"] = "ACTIVE" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => management.ListDeviceModelsAsync(new Dictionary<string, object?> { ["limit"] = 10 }));
            await Assert.ThrowsAsync<PhysicalPosCardDataRejectedException>(() => management.ListStoresAsync(new Dictionary<string, object?> { ["q"] = "4111111111111111" }));
            Assert.Equal(10, requests.Count);
        }
    }

    [Fact]
    public async Task ManagementErrorsAreTyped()
    {
        var (client, _) = Scripted(0, () => Error((HttpStatusCode)429, "RATE_LIMITED", "{\"retryAfterSeconds\":2}"));
        using (client)
        {
            var limited = await Assert.ThrowsAsync<PhysicalPosRateLimitException>(() => client.PhysicalPosManagement.CreateStoreAsync(new { code = "S", name = "Store" }, "store-create-0001"));
            Assert.Equal((2d, "store-create-0001"), (limited.RetryAfterSeconds, limited.IdempotencyKey));
        }
    }
}
