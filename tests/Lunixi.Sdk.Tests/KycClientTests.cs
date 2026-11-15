namespace Lunixi.Sdk.Tests;

/// <summary>Contract source: the PHP SDK's KYC client and the Node SDK's table. Names are fictional.</summary>
public class KycClientTests
{
    [Fact]
    public void RoutesAreBearerOnlyUnderTheKycPrefix()
    {
        foreach (var (name, route) in KycRoutes.All)
        {
            Assert.False(route.StepUp, name);
            Assert.StartsWith("/api/v1/kyc/", route.Path);
            Assert.DoesNotContain("/sdk/init", route.Path);
        }
        Assert.Equal(25, KycEvents.All.Count);
        Assert.Equal(17, KycSessionStatuses.All.Count);
        Assert.Equal("possession_biometric_binding", KycAssuranceDomains.Biometric);
        Assert.True(KycSessionStatuses.IsTerminal("manual_review"));
        Assert.False(KycSessionStatuses.IsTerminal(KycSessionStatuses.Processing));
        Assert.True(KycAssuranceLevels.Meets(KycAssuranceLevels.High, KycAssuranceLevels.Substantial));
        Assert.False(KycAssuranceLevels.Meets(KycAssuranceLevels.NotPerformed, KycAssuranceLevels.Low));
    }

    [Fact]
    public async Task CreateSessionMapsTheIdentityAndCarriesTheKeyInHeaderAndBody()
    {
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json("{\"data\":{\"session\":{\"sessionId\":\"sess_1\",\"status\":\"CREATED\"}}}"));
        using (client)
        {
            var result = await client.Kyc.CreateSessionAsync(new
            {
                subjectId = "42",
                sessionType = "kyc",
                externalCustomerId = "42",
                jurisdiction = "TR",
                person = new { firstName = "Ada" },
                metadata = new { orderId = "o-1" },
            }, "idem-kyc-00001");

            Assert.Equal("sess_1", result.GetProperty("session").GetProperty("sessionId").GetString());
            var request = Assert.Single(requests);
            Assert.Equal(("POST", "/api/v1/kyc/sessions"), (request.Method, request.PathAndQuery));
            Assert.False(request.Signed);
            Assert.Equal("idem-kyc-00001", request.IdempotencyKey);
            Assert.Equal("{\"subjectId\":\"42\",\"sessionType\":\"KYC\",\"externalCustomerId\":\"42\",\"jurisdiction\":\"TR\",\"idempotencyKey\":\"idem-kyc-00001\"," +
                "\"customer\":{\"customerType\":\"PERSON\",\"firstName\":\"Ada\"},\"evidenceContextJson\":\"{\\\"orderId\\\":\\\"o-1\\\"}\"}", request.Body);
        }
    }

    [Fact]
    public async Task InvalidSessionsFailBeforeSending()
    {
        var (client, requests) = RecordingClient.Create();
        using (client)
        {
            var kyc = client.Kyc;
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => kyc.CreateSessionAsync(new { subjectId = "42", sessionType = "AML" }));
            var two = await Assert.ThrowsAsync<LunixiConfigurationException>(() =>
                kyc.CreateSessionAsync(new { subjectId = "42", sessionType = KycSessionTypes.Kyb, person = new { }, company = new { } }));
            Assert.Contains("one of person, company or customer", two.Message);
            var ttl = await Assert.ThrowsAsync<LunixiConfigurationException>(() =>
                kyc.CreateSessionAsync(new { subjectId = "42", sessionType = KycSessionTypes.Kyc, ttlMinutes = 2000 }));
            Assert.Contains("ttlMinutes", ttl.Message);
            var unknown = await Assert.ThrowsAsync<LunixiConfigurationException>(() =>
                kyc.CreateSessionAsync(new { subjectId = "42", sessionType = KycSessionTypes.Kyc, status = "APPROVED" }));
            Assert.Contains("does not accept status", unknown.Message);
            await Assert.ThrowsAsync<LunixiConfigurationException>(() =>
                kyc.EvidenceRequestAsync(new { subjectId = "42", sessionType = KycSessionTypes.Evidence, metadata = new { a = 1 }, evidenceContextJson = "{}" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => kyc.SetJurisdictionAsync("sess_1", " "));
            Assert.Empty(requests);
        }
    }

    [Fact]
    public async Task ListSessionsTakesOnlyLimitAndCursor()
    {
        var pages = new Queue<string>(new[]
        {
            "{\"status\":\"success\",\"data\":{\"items\":[{\"sessionId\":\"s1\"},{\"sessionId\":\"s2\"}],\"nextCursor\":\"tok2\"}}",
            "{\"status\":\"success\",\"data\":{\"items\":[{\"sessionId\":\"s3\"}],\"nextCursor\":\"stale\",\"pageInfo\":{\"hasMore\":false,\"nextCursor\":null}}}",
        });
        var (client, requests) = RecordingClient.Create(_ => FakeHandler.Json(pages.Dequeue()));
        using (client)
        {
            var page = await client.Kyc.ListSessionsAsync(new Dictionary<string, object?> { ["pageSize"] = 2, ["pageToken"] = "abc" });
            Assert.Equal(2, page.Items.Count);
            Assert.Equal("tok2", page.NextCursor);
            Assert.True(page.HasMore);

            var last = await client.Kyc.ListSessionsAsync();
            Assert.Null(last.NextCursor);
            Assert.False(last.HasMore);

            var refused = await Assert.ThrowsAsync<LunixiConfigurationException>(() =>
                client.Kyc.ListSessionsAsync(new Dictionary<string, object?> { ["status"] = "APPROVED" }));
            Assert.Contains("does not accept the filter(s) status", refused.Message);
            Assert.Equal(new[] { "/api/v1/kyc/sessions?limit=2&cursor=abc", "/api/v1/kyc/sessions" }, requests.Select(r => r.PathAndQuery));
        }
    }

    [Fact]
    public async Task SessionOperationsAndAdminRoutes()
    {
        var (client, requests) = RecordingClient.Create();
        using (client)
        {
            var kyc = client.Kyc;
            await kyc.SetPrimaryDocumentAsync("sess_1", "PASSPORT");
            await kyc.SetJurisdictionAsync("sess_1", "TR");
            await kyc.SubmitAsync("sess_1");
            await kyc.ManualReviewAsync("sess_1");
            await kyc.ManualReviewAsync("sess_1", reasonCode: "DOC_BLUR");
            await kyc.MerchantPolicyAsync();
            await kyc.RotateWebhookSecretAsync();
            await kyc.WebhookOutboxAsync(new Dictionary<string, object?> { ["status"] = "FAILED", ["limit"] = 10 });
            await kyc.RetryWebhookEventAsync("evt_1");
            await kyc.ErasureRequestsAsync(new Dictionary<string, object?> { ["limit"] = 5 });

            Assert.Equal(new[]
            {
                "POST /api/v1/kyc/sessions/sess_1/primary-document",
                "POST /api/v1/kyc/sessions/sess_1/jurisdiction",
                "POST /api/v1/kyc/sessions/sess_1/submit",
                "POST /api/v1/kyc/sessions/sess_1/manual-review",
                "POST /api/v1/kyc/sessions/sess_1/manual-review",
                "GET /api/v1/kyc/admin/merchant-policy",
                "POST /api/v1/kyc/admin/merchant-policy/webhook-secret/rotate",
                "GET /api/v1/kyc/admin/webhook-outbox?limit=10&status=FAILED",
                "POST /api/v1/kyc/admin/webhook-outbox/evt_1/retry",
                "GET /api/v1/kyc/admin/erasure-requests?limit=5",
            }, requests.Select(r => r.Method + " " + r.PathAndQuery));
            Assert.All(requests, request => Assert.False(request.Signed));
            Assert.Equal("{\"documentType\":\"PASSPORT\"}", requests[0].Body);
            Assert.Equal("{\"jurisdiction\":\"TR\"}", requests[1].Body);
            Assert.Null(requests[2].Body);
            Assert.Null(requests[3].Body);
            Assert.Equal("{\"reasonCode\":\"DOC_BLUR\"}", requests[4].Body);
        }
    }

    [Fact]
    public async Task UploadDocumentNeverEchoesTheImage()
    {
        var (client, requests) = RecordingClient.Create();
        using (client)
        {
            var error = await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Kyc.UploadDocumentAsync("sess_1", new
            {
                documentType = "PASSPORT",
                fileBase64 = "SECRETIMAGEDATA",
                mimeType = "image/jpeg",
                extra = "SECRETIMAGEDATA",
            }));
            Assert.DoesNotContain("SECRETIMAGEDATA", error.Message);
            var missing = await Assert.ThrowsAsync<LunixiConfigurationException>(() =>
                client.Kyc.UploadDocumentAsync("sess_1", new { documentType = "PASSPORT", fileBase64 = "SECRETIMAGEDATA" }));
            Assert.Contains("mimeType", missing.Message);
            Assert.DoesNotContain("SECRETIMAGEDATA", missing.Message);
            Assert.Empty(requests);
        }
    }
}
