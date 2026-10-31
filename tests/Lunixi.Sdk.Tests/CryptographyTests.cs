using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NSec.Cryptography;

namespace Lunixi.Sdk.Tests;

public class CryptographyTests
{
    [Fact]
    public void NodeJsonAndEd25519FixtureMatchesExactly()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/signing-vector.json")));
        var value = fixture.RootElement;
        using var input = JsonDocument.Parse(value.GetProperty("input").GetString()!);
        var bytes = CanonicalJson.Serialize(input.RootElement);
        Assert.Equal(value.GetProperty("json").GetString(), Encoding.UTF8.GetString(bytes));
        Assert.Equal(value.GetProperty("digest").GetString(), "SHA-256=" + Convert.ToBase64String(SHA256.HashData(bytes)));
        using var signer = new Ed25519Signer(value.GetProperty("privateKeyPem").GetString()!);
        var canonical = value.GetProperty("canonical").GetString()!;
        Assert.Equal(canonical, LunixiClient.CanonicalRequest("POST", "/api/v1/payments/intents?locale=tr", "2026-10-31T08:00:00Z", "synthetic-fixture", value.GetProperty("digest").GetString()));
        Assert.Equal(value.GetProperty("signature").GetString(), signer.Sign(Encoding.UTF8.GetBytes(canonical)));
        Assert.Equal(value.GetProperty("publicKeyPem").GetString()!.Trim(), signer.PublicKeyPem.Trim());
        var publicKey = PublicKey.Import(SignatureAlgorithm.Ed25519, Encoding.UTF8.GetBytes(signer.PublicKeyPem), KeyBlobFormat.PkixPublicKeyText);
        Assert.True(SignatureAlgorithm.Ed25519.Verify(publicKey, Encoding.UTF8.GetBytes(canonical), Convert.FromBase64String(signer.Sign(Encoding.UTF8.GetBytes(canonical)))));
    }

    [Theory]
    [InlineData("{\"a\":1,\"a\":2}")]
    [InlineData("{\"amount\":9007199254740992}")]
    [InlineData("{\"number\":1e999}")]
    public void AmbiguousOrUnsafeJsonIsRejected(string json)
    {
        using var input = JsonDocument.Parse(json);
        Assert.Throws<LunixiConfigurationException>(() => CanonicalJson.Serialize(input.RootElement));
    }

    [Fact]
    public void GeneratedKeysAndConfigurationNeverSerializePrivateMaterial()
    {
        var keys = Ed25519Signer.GenerateKeyPair();
        Assert.DoesNotContain("PRIVATE KEY", keys.ToString());
        Assert.DoesNotContain("PRIVATE KEY", JsonSerializer.Serialize(keys));
        Assert.DoesNotContain("PRIVATE KEY", TestOptions.Create().ToString());
        Assert.DoesNotContain("PRIVATE KEY", JsonSerializer.Serialize(TestOptions.Create()));
        Assert.Throws<LunixiConfigurationException>(() => new Ed25519Signer("secret-not-a-key"));
    }
}
