using System.Text.Json;

namespace Lunixi;

/// <summary>Invalid local configuration or request data. No request is sent for this error.</summary>
public sealed class LunixiConfigurationException(string message) : ArgumentException(message);

/// <summary>A gateway response with a non-success status. Exception messages contain no response bodies or credentials.</summary>
public class LunixiApiException : Exception
{
    /// <summary>The HTTP response status code.</summary>
    public int StatusCode { get; }
    /// <summary>The gateway's machine-readable error code, if present.</summary>
    public string? Code { get; }
    /// <summary>The gateway request identifier, useful when contacting support.</summary>
    public string? RequestId { get; }
    /// <summary>The parsed response, available for explicit inspection; it may contain customer data and must not be logged wholesale.</summary>
    public JsonElement? Response { get; }
    /// <summary>The server's retry-after value in seconds, if valid.</summary>
    public double? RetryAfterSeconds { get; }
    internal LunixiApiException(int status, string? code, string? requestId, JsonElement? response, double? retryAfter = null)
        : base($"Lunixi gateway returned HTTP {status}. Inspect Code and RequestId for diagnostics.")
    {
        StatusCode = status; Code = Validation.SafeIdentifier(code); RequestId = Validation.SafeIdentifier(requestId);
        Response = response; RetryAfterSeconds = retryAfter;
    }
}

/// <summary>A gateway HTTP 400 or 422 response; inspect Response explicitly for validation details.</summary>
public sealed class LunixiValidationException : LunixiApiException
{
    internal LunixiValidationException(int status, string? code, string? requestId, JsonElement? response)
        : base(status, code, requestId, response) { }
}

/// <summary>A gateway HTTP 429 response. The SDK does not automatically retry rate limits.</summary>
public sealed class LunixiRateLimitException : LunixiApiException
{
    internal LunixiRateLimitException(string? code, string? requestId, JsonElement? response, double? retryAfter)
        : base(429, code, requestId, response, retryAfter) { }
}

/// <summary>A network failure, timeout, redirect or malformed gateway response.</summary>
public sealed class LunixiTransportException : Exception
{
    /// <summary>True for writes when a final financial outcome could not be established.</summary>
    public bool OutcomeMayBeUnknown { get; }
    internal LunixiTransportException(string message, bool outcomeMayBeUnknown = false) : base(message)
        => OutcomeMayBeUnknown = outcomeMayBeUnknown;
}

/// <summary>An invalid or conflicting webhook. Never acknowledge it as a successful financial event.</summary>
public sealed class LunixiWebhookException(string message) : Exception(message);

/// <summary>A valid webhook is already leased by another processor. Return a retriable HTTP failure.</summary>
public sealed class LunixiWebhookBusyException() : Exception("Webhook processing is already in progress; retry later.");
