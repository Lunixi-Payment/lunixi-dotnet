using System.Text.Json;

namespace Lunixi.Sdk.Tests;

public class EnabledInstallmentsTests
{
    public static TheoryData<int[]> BrokenLists => new()
    {
        Array.Empty<int>(),
        new[] { 2, 3 },
        new[] { 1, 2, 2 },
        new[] { 0, 1 },
        new[] { 1, -3 },
        new[] { 1, 37 },
    };

    [Theory]
    [MemberData(nameof(BrokenLists))]
    public async Task BrokenListsAreRejectedBeforeAnyRequest(int[] counts)
    {
        using var client = new LunixiClient(TestOptions.Create(), new FakeHandler((_, _) => throw new Xunit.Sdk.XunitException("No network expected")));
        await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Payments.CreateIntentAsync(
            new("10050", "TRY", "order-1") { EnabledInstallments = counts }, "checkout:attempt-1"));
    }

    [Fact]
    public async Task ListIsSentInAscendingOrderWithoutChangingTheRequest()
    {
        string? body = null;
        using var client = new LunixiClient(TestOptions.Create(), new FakeHandler(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/auth/token")) return FakeHandler.Json("{\"access_token\":\"token\"}");
            body = await request.Content!.ReadAsStringAsync(token);
            return FakeHandler.Json("{\"data\":{\"token\":\"ct_example\"}}");
        }));
        var counts = new[] { 6, 1, 3 };
        await client.Payments.CreateIntentAsync(new("10050", "TRY", "order-1") { EnabledInstallments = counts }, "checkout:attempt-1");
        using var sent = JsonDocument.Parse(body!);
        Assert.Equal(new[] { 1, 3, 6 }, sent.RootElement.GetProperty("enabledInstallments").EnumerateArray().Select(e => e.GetInt32()));
        Assert.Equal(new[] { 6, 1, 3 }, counts);
    }

    [Fact]
    public async Task NullSendsNoField()
    {
        string? body = null;
        using var client = new LunixiClient(TestOptions.Create(), new FakeHandler(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/auth/token")) return FakeHandler.Json("{\"access_token\":\"token\"}");
            body = await request.Content!.ReadAsStringAsync(token);
            return FakeHandler.Json("{\"data\":{\"token\":\"ct_example\"}}");
        }));
        await client.Payments.CreateIntentAsync(new("10050", "TRY", "order-1"), "checkout:attempt-1");
        using var sent = JsonDocument.Parse(body!);
        Assert.False(sent.RootElement.TryGetProperty("enabledInstallments", out _));
    }

    [Theory]
    [InlineData(new[] { 1 })]
    [InlineData(new[] { 36, 1 })]
    public async Task SinglePaymentAndBoundsAreAccepted(int[] counts)
    {
        using var client = new LunixiClient(TestOptions.Create(), new FakeHandler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/auth/token")
            ? FakeHandler.Json("{\"access_token\":\"token\"}") : FakeHandler.Json("{\"data\":{\"token\":\"ct_example\"}}"))));
        await client.Payments.CreateIntentAsync(new("10050", "TRY", "order-1") { EnabledInstallments = counts }, "checkout:attempt-1");
    }
}
