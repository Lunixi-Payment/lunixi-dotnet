using System.Text.Json.Serialization;

namespace Lunixi;

/// <summary>Server-side client configuration. Credentials must never be sent to a browser.</summary>
public sealed class LunixiOptions
{
    /// <summary>The gateway HTTPS origin, without an API path, query, credentials or fragment.</summary>
    public required Uri BaseUri { get; init; }
    /// <summary>The merchant SECRET key identifier.</summary>
    public required string KeyId { get; init; }
    /// <summary>The merchant's Ed25519 PKCS#8 PEM private key.</summary>
    [JsonIgnore]
    public string PrivateKeyPem { get; init; } = string.Empty;
    /// <summary>Timeout for each HTTP attempt, including authentication.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>Retries for safe reads or writes carrying a stable idempotency key; between zero and five.</summary>
    public int MaxRetries { get; init; } = 2;
    /// <summary>Initial exponential retry delay. The delay is capped at two seconds.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(100);
    /// <summary>Maximum accepted response bytes; one MiB by default.</summary>
    public int MaxResponseBytes { get; init; } = 1024 * 1024;
    /// <summary>Clock used for signatures and token expiration. Defaults to the system UTC clock.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    internal void Validate()
    {
        if (BaseUri is null || !BaseUri.IsAbsoluteUri || BaseUri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(BaseUri.UserInfo) || !string.IsNullOrEmpty(BaseUri.Query) ||
            !string.IsNullOrEmpty(BaseUri.Fragment) || BaseUri.AbsolutePath != "/")
            throw new LunixiConfigurationException("BaseUri must be an HTTPS origin without a path, credentials, query or fragment.");
        Validation.HeaderValue(KeyId, nameof(KeyId));
        if (string.IsNullOrWhiteSpace(PrivateKeyPem))
            throw new LunixiConfigurationException("An Ed25519 private key is required.");
        if (Timeout < TimeSpan.FromMilliseconds(1) || Timeout > TimeSpan.FromMinutes(5))
            throw new LunixiConfigurationException("Timeout must be between one millisecond and five minutes.");
        if (MaxRetries is < 0 or > 5 || RetryDelay < TimeSpan.Zero || RetryDelay > TimeSpan.FromSeconds(2))
            throw new LunixiConfigurationException("Retry settings are outside their permitted range.");
        if (MaxResponseBytes is < 1024 or > 16 * 1024 * 1024)
            throw new LunixiConfigurationException("MaxResponseBytes must be between 1 KiB and 16 MiB.");
        ArgumentNullException.ThrowIfNull(TimeProvider);
    }

    /// <summary>Returns a description with all credential values redacted.</summary>
    public override string ToString() => "LunixiOptions { Credentials = [REDACTED] }";
}

internal static class Validation
{
    internal const long MaxSafeInteger = 9_007_199_254_740_991;
    internal static void HeaderValue(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512 || value.Any(c => c < 0x21 || c > 0x7e))
            throw new LunixiConfigurationException($"{field} must contain printable ASCII without whitespace.");
    }
    internal static void IdempotencyKey(string key)
    {
        HeaderValue(key, "Idempotency-Key");
        if (key.Length is < 8 or > 128)
            throw new LunixiConfigurationException("Idempotency-Key must contain 8–128 characters and identify one persisted operation.");
    }
    internal static void Amount(long amount)
    {
        if (amount is < 1 or > MaxSafeInteger)
            throw new LunixiConfigurationException("Amounts must be positive, JavaScript-safe integer minor units.");
    }
    internal static string Id(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 256 || id.Any(char.IsControl))
            throw new LunixiConfigurationException("A non-empty resource identifier of at most 256 characters is required.");
        return Uri.EscapeDataString(id);
    }
    internal static string? SafeIdentifier(string? value) => value is { Length: > 0 and <= 256 } &&
        value.All(c => c >= 0x21 && c <= 0x7e) ? value : null;
}
