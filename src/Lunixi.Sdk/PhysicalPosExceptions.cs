using System.Text.Json;

namespace Lunixi;

/// <summary>
/// A physical POS request carried card data (a card number, expiry date, security code, track or PIN data). The card is
/// read on the device, so card data is never sent. Raised by the SDK before anything is sent (<see cref="LunixiApiException.StatusCode"/>
/// 0) when a field name looks like card data or a free-text value holds a card number, and mapped from the gateway's
/// 400 <c>physical_pos.payment.card_data_rejected</c>. <see cref="Field"/> is the path of the offending field
/// (<c>items[1].cvv</c>); the value itself is never part of the error.
/// </summary>
public sealed class PhysicalPosCardDataRejectedException : LunixiValidationException
{
    /// <summary>The gateway error code of a card-data rejection.</summary>
    public const string ErrorCode = "physical_pos.payment.card_data_rejected";
    /// <summary>The path of the field that looks like card data, if known.</summary>
    public string? Field { get; }
    /// <summary>The <c>Idempotency-Key</c> of the write that was refused, if any.</summary>
    public string? IdempotencyKey { get; }
    internal PhysicalPosCardDataRejectedException(string field)
        : base("Physical POS requests must not contain card data: the card is read on the device. Remove the field or value; do not send card numbers, expiry dates or security codes.",
            0, ErrorCode, null, null, null)
        => Field = field;
    internal PhysicalPosCardDataRejectedException(LunixiApiException error, string? field, string? idempotencyKey)
        : base(error.Message, error.StatusCode, error.Code, error.RequestId, error.Response, error)
    { Field = field; IdempotencyKey = idempotencyKey; }
}

/// <summary>
/// 409 <c>physical_pos.payment.void_after_settlement</c>: the payment is already in a device end-of-day and can no longer
/// be voided. Return the amount with a <c>REFUND</c> reversal of the same payment, with a new <c>Idempotency-Key</c>.
/// </summary>
public sealed class PhysicalPosVoidAfterSettlementException : LunixiApiException
{
    /// <summary>The gateway error code of a void after end-of-day.</summary>
    public const string ErrorCode = "physical_pos.payment.void_after_settlement";
    /// <summary>The operation the gateway allows instead (<c>details.allowedOperation</c>, normally <c>REFUND</c>).</summary>
    public string? AllowedOperation { get; }
    /// <summary>The <c>Idempotency-Key</c> of the refused reversal.</summary>
    public string? IdempotencyKey { get; }
    internal PhysicalPosVoidAfterSettlementException(LunixiApiException error, string? allowedOperation, string? idempotencyKey)
        : base(error.Message, error.StatusCode, error.Code, error.RequestId, error.Response, error.RetryAfterSeconds, error)
    { AllowedOperation = allowedOperation; IdempotencyKey = idempotencyKey; }
}

/// <summary>
/// A physical POS 429: payment writes are rate limited per API key. <see cref="LunixiApiException.RetryAfterSeconds"/> is the
/// <c>Retry-After</c> header, or <c>details.retryAfterSeconds</c> when the header is missing. A rate-limited payment write
/// was not started; call again later with the same key.
/// </summary>
public sealed class PhysicalPosRateLimitException : LunixiRateLimitException
{
    /// <summary>The <c>Idempotency-Key</c> of the refused write, if any.</summary>
    public string? IdempotencyKey { get; }
    internal PhysicalPosRateLimitException(LunixiApiException error, double? retryAfter, string? idempotencyKey)
        : base(error.Message, error.Code, error.RequestId, error.Response, retryAfter, error)
        => IdempotencyKey = idempotencyKey;
}

/// <summary>
/// The outcome of a physical POS payment write (start, reversal, cancel) is unknown: the payment may or may not exist. An
/// attempt got no HTTP answer, a 2xx without a readable payment, or a 5xx that does not say the payment was not started;
/// the whole call then ends with this exception, whatever a later attempt answered.
/// Call again with the SAME <see cref="IdempotencyKey"/> (an existing payment comes back with <c>replayed: true</c>) or look
/// the payment up by <c>merchantReference</c>. A new key can start a second payment.
/// <see cref="LunixiApiException.StatusCode"/> and <see cref="LunixiApiException.Code"/> are those of the last HTTP answer
/// (0 and null when there was none); <see cref="Exception.InnerException"/> is the last error.
/// </summary>
public sealed class PhysicalPosOutcomeUnknownException : LunixiApiException
{
    /// <summary>The <c>Idempotency-Key</c> to call again with.</summary>
    public string IdempotencyKey { get; }
    /// <summary>The number of attempts made.</summary>
    public int Attempts { get; }
    internal PhysicalPosOutcomeUnknownException(string idempotencyKey, int attempts, int status, string? code, string? requestId,
        JsonElement? response, double? retryAfter, Exception? innerException)
        : base("The outcome of the physical POS payment request is unknown: the payment may exist. Call again with the same Idempotency-Key (an existing payment comes back with replayed: true) or look it up; never use a new key for it.",
            status, code, requestId, response, retryAfter, innerException)
    { IdempotencyKey = idempotencyKey; Attempts = attempts; }
}
