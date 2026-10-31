using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using NSec.Cryptography;

namespace Lunixi;

/// <summary>Ed25519 signing using NSec/libsodium. No curve arithmetic is implemented by the SDK.</summary>
public sealed class Ed25519Signer : IDisposable
{
    private static readonly SignatureAlgorithm Algorithm = SignatureAlgorithm.Ed25519;
    private readonly Key key;
    /// <summary>Imports an unencrypted Ed25519 PKCS#8 PEM key.</summary>
    public Ed25519Signer(string privateKeyPem)
    {
        try { key = Key.Import(Algorithm, Encoding.UTF8.GetBytes(privateKeyPem), KeyBlobFormat.PkixPrivateKeyText); }
        catch (Exception e) when (e is ArgumentException or FormatException or CryptographicException)
        { throw new LunixiConfigurationException("The private key must be a valid unencrypted Ed25519 PKCS#8 PEM."); }
    }
    /// <summary>Signs exact bytes and returns a base64 Ed25519 signature.</summary>
    public string Sign(ReadOnlySpan<byte> message) => Convert.ToBase64String(Algorithm.Sign(key, message));
    /// <summary>Exports the derived public key as a PEM SPKI key.</summary>
    public string PublicKeyPem => Pem(key.PublicKey.Export(KeyBlobFormat.PkixPublicKeyText));
    /// <summary>Generates a merchant-controlled key pair. Register only the public key in the Lunixi dashboard.</summary>
    public static LunixiKeyPair GenerateKeyPair()
    {
        using var generated = Key.Create(Algorithm, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        return new LunixiKeyPair(Pem(generated.PublicKey.Export(KeyBlobFormat.PkixPublicKeyText)),
            Pem(generated.Export(KeyBlobFormat.PkixPrivateKeyText)));
    }
    private static string Pem(byte[] bytes) => Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n", StringComparison.Ordinal);
    /// <summary>Releases the private key held by the crypto provider.</summary>
    public void Dispose() => key.Dispose();
    /// <summary>Returns no key material.</summary>
    public override string ToString() => "Ed25519Signer { PrivateKey = [REDACTED] }";
}

/// <summary>A newly generated Ed25519 key pair. Keep the private key on your server.</summary>
public sealed class LunixiKeyPair
{
    /// <summary>The public key to register with Lunixi.</summary>
    public string PublicKeyPem { get; }
    /// <summary>The server-side private key. Never log or serialize it.</summary>
    [JsonIgnore]
    public string PrivateKeyPem { get; }
    internal LunixiKeyPair(string publicKey, string privateKey) { PublicKeyPem = publicKey; PrivateKeyPem = privateKey; }
    /// <summary>Returns no key material.</summary>
    public override string ToString() => "LunixiKeyPair { PrivateKey = [REDACTED] }";
}
