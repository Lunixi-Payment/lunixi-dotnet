using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using NSec.Cryptography;

namespace Lunixi.Sdk.Tests;

public class TransportTests
{
    [Fact]
    public async Task AuthHasNoBodyAndSignedWritesHaveExactDigestAndSignature()
    {
        var publicKey = PublicKey.Import(SignatureAlgorithm.Ed25519, Encoding.UTF8.GetBytes(TestOptions.Keys.PublicKeyPem), KeyBlobFormat.PkixPublicKeyText);
        var authCount = 0; var operations = 0;
        using var client = new LunixiClient(TestOptions.Create(), new FakeHandler(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v1/auth/token")
            {
                authCount++; Assert.Null(request.Content); Assert.False(request.Headers.Contains("Digest")); Assert.Null(request.Headers.Authorization);
            }
            else
            {
                operations++; Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                Assert.Equal("synthetic-token", request.Headers.Authorization?.Parameter);
                var body = await request.Content!.ReadAsByteArrayAsync(ct);
                Assert.Equal("SHA-256=" + Convert.ToBase64String(SHA256.HashData(body)), request.Headers.GetValues("Digest").Single());
                Assert.Equal("checkout:persisted-001", request.Headers.GetValues("Idempotency-Key").Single());
            }
            var canonical = LunixiClient.CanonicalRequest(request.Method.Method, request.RequestUri.PathAndQuery,
                request.Headers.GetValues("X-Date").Single(), request.Headers.GetValues("X-Nonce").Single(), request.Headers.TryGetValues("Digest", out var values) ? values.Single() : null);
            Assert.True(SignatureAlgorithm.Ed25519.Verify(publicKey, Encoding.UTF8.GetBytes(canonical), Convert.FromBase64String(request.Headers.GetValues("X-Signature").Single())));
            return FakeHandler.Json(request.RequestUri.AbsolutePath == "/api/v1/auth/token" ? "{\"access_token\":\"synthetic-token\",\"expires_in\":1800}" : "{\"data\":{\"status\":\"CREATED\"}}");
        }));
        await client.Payments.CreateIntentAsync(new("10050", "TRY", "ORDER-1"), "checkout:persisted-001");
        await client.Payments.CreateIntentAsync(new("10050", "TRY", "ORDER-1"), "checkout:persisted-001");
        Assert.Equal(1, authCount); Assert.Equal(2, operations);
    }

    [Fact]
    public async Task ConcurrentCallsShareTokenAndRefreshAfterExpiry()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-10-31T08:00:00Z")); var authCount = 0;
        using var client = new LunixiClient(TestOptions.Create(clock), new FakeHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/auth/token"))
            { Interlocked.Increment(ref authCount); return Task.FromResult(FakeHandler.Json("{\"data\":{\"access_token\":\"token\",\"expires_in\":60}}")); }
            return Task.FromResult(FakeHandler.Json());
        }));
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => client.Payments.GetAsync("pay-1")));
        Assert.Equal(1, authCount); clock.Now = clock.Now.AddSeconds(31);
        await client.Payments.GetAsync("pay-1"); Assert.Equal(2, authCount);
    }

    [Fact]
    public async Task RetryReusesBodyAndKeyButGeneratesNewNonce()
    {
        var bodies = new List<string>(); var nonces = new List<string>(); var keys = new List<string>();
        using var client = new LunixiClient(TestOptions.Create(), new FakeHandler(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/auth/token")) return FakeHandler.Json("{\"access_token\":\"token\"}");
            bodies.Add(await request.Content!.ReadAsStringAsync(ct)); keys.Add(request.Headers.GetValues("Idempotency-Key").Single());
            nonces.Add(request.Headers.GetValues("X-Nonce").Single());
            return FakeHandler.Json(code: bodies.Count == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        }));
        await client.Payments.RefundAsync("pay-1", "refund:persisted-key", "100");
        Assert.Equal(2, bodies.Count); Assert.Single(bodies.Distinct()); Assert.Single(keys.Distinct()); Assert.Equal(2, nonces.Distinct().Count());
    }

    [Fact]
    public async Task FinancialWriteWithoutKeyNeverRetriesNetworkFailure()
    {
        var writes = 0;
        using var client = new LunixiClient(TestOptions.Create(), new FakeHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/auth/token")) return Task.FromResult(FakeHandler.Json("{\"access_token\":\"token\"}"));
            writes++; throw new HttpRequestException("private upstream detail");
        }));
        var error = await Assert.ThrowsAsync<LunixiTransportException>(() => client.SendAsync(HttpMethod.Post, "/api/v1/fraud/events/transactions", new { transactionRef = "tx-1" }));
        Assert.True(error.OutcomeMayBeUnknown); Assert.Equal(1, writes); Assert.DoesNotContain("private upstream", error.ToString());
    }

    [Fact]
    public async Task OneUnauthorizedRefreshesOnceAndPreservesKey()
    {
        var authCount = 0; var calls = 0;
        using var client = new LunixiClient(TestOptions.Create(), new FakeHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/auth/token")) return Task.FromResult(FakeHandler.Json("{\"access_token\":\"token-" + ++authCount + "\"}"));
            calls++; Assert.Equal("capture:stable-key", request.Headers.GetValues("Idempotency-Key").Single());
            return Task.FromResult(FakeHandler.Json(code: HttpStatusCode.Unauthorized));
        }));
        var error = await Assert.ThrowsAsync<LunixiApiException>(() => client.Payments.CaptureAsync("pay-1", "capture:stable-key"));
        Assert.Equal(401, error.StatusCode); Assert.Equal(2, authCount); Assert.Equal(2, calls);
    }

    [Fact]
    public async Task RedirectIsRejectedWithoutForwardingCredentials()
    {
        var count = 0;
        using var client = new LunixiClient(TestOptions.Create(), new FakeHandler((_, _) =>
        {
            count++; var response = FakeHandler.Json(code: HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("https://attacker.example/collect"); return Task.FromResult(response);
        }));
        await Assert.ThrowsAsync<LunixiTransportException>(() => client.Payments.GetAsync("pay-1")); Assert.Equal(1, count);
        Assert.Throws<LunixiConfigurationException>(() => new LunixiClient(TestOptions.Create(), new HttpClientHandler { AllowAutoRedirect = true }));
    }

    [Fact]
    public async Task ApiErrorsExposeCodesAndRequestIdsWithoutSensitiveMessages()
    {
        using var client = new LunixiClient(TestOptions.Create(), new FakeHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/auth/token")) return Task.FromResult(FakeHandler.Json("{\"access_token\":\"token\"}"));
            var response = FakeHandler.Json("{\"code\":\"rate.limit\",\"message\":\"sensitive secret\"}", HttpStatusCode.TooManyRequests);
            response.Headers.Add("x-request-id", "req-test-1"); response.Headers.Add("retry-after", "42"); return Task.FromResult(response);
        }));
        var error = await Assert.ThrowsAsync<LunixiRateLimitException>(() => client.Payments.GetAsync("pay-1"));
        Assert.Equal("rate.limit", error.Code); Assert.Equal("req-test-1", error.RequestId); Assert.Equal(42, error.RetryAfterSeconds);
        Assert.DoesNotContain("sensitive secret", error.ToString());
    }

    [Theory]
    [InlineData("https://attacker.example/api/v1/test")]
    [InlineData("//attacker.example/api/v1/test")]
    [InlineData("/api/v1/../../secrets")]
    [InlineData("/api/v1/%2e%2e/%2e%2e/secrets")]
    [InlineData("/api/v1/test#fragment")]
    public async Task UnsafeRoutesAreRejectedBeforeNetwork(string route)
    {
        using var client = new LunixiClient(TestOptions.Create(), new FakeHandler((_, _) => throw new Xunit.Sdk.XunitException("No network expected")));
        await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.SendAsync(HttpMethod.Get, route));
    }

    [Fact]
    public async Task RemovedAmountFieldsAreRejectedBeforeNetwork()
    {
        using var client = new LunixiClient(TestOptions.Create(), new FakeHandler((_, _) => throw new Xunit.Sdk.XunitException("No network expected")));
        await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.SendAsync(HttpMethod.Post, "/api/v1/payments/intents", new { price = "1", amount = "100" }));
        await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.SendAsync(HttpMethod.Post, "/api/v1/payments/intents", new { paidPrice = "1", amount = "100" }));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("42")]
    public async Task MalformedResponsesFailClosed(string body)
    {
        using var client = new LunixiClient(TestOptions.Create(), new FakeHandler((request, _) => Task.FromResult(FakeHandler.Json(
            request.RequestUri!.AbsolutePath.EndsWith("/auth/token") ? "{\"access_token\":\"token\"}" : body))));
        await Assert.ThrowsAsync<LunixiTransportException>(() => client.Payments.GetAsync("pay-1"));
    }

    [Fact]
    public async Task OversizedResponseIsRejected()
    {
        using var client = new LunixiClient(TestOptions.Create(maxResponseBytes: 1024), new FakeHandler((request, _) => Task.FromResult(FakeHandler.Json(
            request.RequestUri!.AbsolutePath.EndsWith("/auth/token") ? "{\"access_token\":\"token\"}" : "{\"data\":\"" + new string('x', 1024) + "\"}"))));
        await Assert.ThrowsAsync<LunixiTransportException>(() => client.Payments.GetAsync("pay-1"));
    }

    [Fact]
    public async Task RequestTimeoutIsSanitizedAndCallerCancellationIsPreserved()
    {
        var options = new LunixiOptions
        {
            BaseUri = new("https://gateway.example"),
            KeyId = "mk_test_synthetic",
            PrivateKeyPem = TestOptions.Keys.PrivateKeyPem,
            Timeout = TimeSpan.FromMilliseconds(30),
            MaxRetries = 0,
        };
        using var client = new LunixiClient(options, new FakeHandler(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/auth/token")) return FakeHandler.Json("{\"access_token\":\"token\"}");
            await Task.Delay(Timeout.Infinite, ct); return FakeHandler.Json();
        }));
        var error = await Assert.ThrowsAsync<LunixiTransportException>(() => client.Payments.CaptureAsync("pay-1", "capture:persisted-key"));
        Assert.True(error.OutcomeMayBeUnknown);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.Payments.CaptureAsync("pay-1", "capture:persisted-key", cancellationToken: canceled.Token));
    }

    [Fact]
    public async Task MalformedTokenExpiryFailsClosed()
    {
        using var client = new LunixiClient(TestOptions.Create(), new FakeHandler((_, _) => Task.FromResult(FakeHandler.Json("{\"access_token\":\"token\",\"expires_in\":\"unexpected\"}"))));
        await Assert.ThrowsAsync<LunixiTransportException>(() => client.Payments.GetAsync("pay-1"));
    }
}
