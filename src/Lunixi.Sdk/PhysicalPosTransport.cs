using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lunixi;

/// <summary>
/// Signed physical POS requests: reads and management writes (<see cref="SendAsync"/>), the payment-write attempt loop
/// (<see cref="PaymentWriteAsync"/>), typed error mapping and paging.
/// </summary>
internal static class PhysicalPosTransport
{
    private enum Attempt { Ambiguous, NotStarted, Definite }

    /// <summary>Sends one signed request under <c>/api/v1/physical-pos</c>, checks the response money and maps physical POS errors.</summary>
    internal static async Task<LunixiResponse> SendAsync(LunixiClient client, HttpMethod method, string path, JsonObject? body,
        string? idempotencyKey, CancellationToken cancellationToken)
    {
        LunixiResponse response;
        try
        {
            response = await client.SendCoreAsync(method, PhysicalPosRequest.BasePath + path, body, idempotencyKey, true,
                PhysicalPosValues.IdempotencyKeyMaxLength, cancellationToken).ConfigureAwait(false);
        }
        catch (LunixiApiException error)
        {
            var mapped = Map(error, idempotencyKey);
            if (ReferenceEquals(mapped, error)) throw;
            throw mapped;
        }
        if (!PhysicalPosMoney.IsValid(response.Envelope))
            throw new LunixiTransportException("Gateway response contains invalid canonical money.", method != HttpMethod.Get);
        return response;
    }

    /// <summary>
    /// Sends a payment write (start, reversal, cancel) with its own attempts: the transport's automatic retries are off, so
    /// every attempt is classified here. 1 + <see cref="LunixiOptions.MaxRetries"/> attempts, the same key and body each time.
    /// Success is only a 2xx carrying a payment with a <c>paymentId</c>. No HTTP answer, a 2xx without a readable payment or a
    /// 5xx that does not say "not started" (<see cref="PhysicalPosValues.NotStartedCodes"/>) is ambiguous and retried; a 5xx
    /// that says "not started" is retried; any 4xx stops. When any attempt was ambiguous the call fails with
    /// <see cref="PhysicalPosOutcomeUnknownException"/>, whatever the last answer was.
    /// </summary>
    internal static async Task<PhysicalPosWriteResult> PaymentWriteAsync(LunixiClient client, string path, JsonObject body,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        var attempts = 1 + Math.Max(0, client.MaxRetries);
        var ambiguous = false; Exception? last = null; LunixiResponse? unreadable = null; var made = 0;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            made = attempt;
            LunixiResponse response;
            try
            {
                response = await client.SendCoreAsync(HttpMethod.Post, PhysicalPosRequest.BasePath + path, body, idempotencyKey, true,
                    PhysicalPosValues.IdempotencyKeyMaxLength, cancellationToken, retry: false).ConfigureAwait(false);
            }
            catch (LunixiConfigurationException error) when (ambiguous) { throw OutcomeUnknown(error, null, idempotencyKey, made); }
            catch (LunixiApiException error)
            {
                last = error; unreadable = null;
                var outcome = Classify(error);
                if (outcome == Attempt.Ambiguous) ambiguous = true;
                if (outcome == Attempt.Definite) break;
                if (attempt < attempts) await client.DelayAsync(attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }
            catch (LunixiTransportException error)
            {
                last = error; unreadable = null; ambiguous = true;
                if (attempt < attempts) await client.DelayAsync(attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }
            if (HasPayment(response)) return new PhysicalPosWriteResult(response, "payment", idempotencyKey);
            ambiguous = true; unreadable = response;
            last = new LunixiTransportException("The gateway accepted the payment request but the answer carries no readable payment.", true);
            if (attempt < attempts) await client.DelayAsync(attempt, cancellationToken).ConfigureAwait(false);
        }
        if (ambiguous) throw OutcomeUnknown(last, unreadable, idempotencyKey, made);
        throw Map((LunixiApiException)last!, idempotencyKey);
    }

    private static Attempt Classify(LunixiApiException error)
    {
        if (error.StatusCode is 0 or >= 500)
            return error.StatusCode >= 500 && error.Code is { } code && PhysicalPosValues.NotStartedCodes.Contains(code) ? Attempt.NotStarted : Attempt.Ambiguous;
        return Attempt.Definite;
    }

    private static bool HasPayment(LunixiResponse response)
    {
        if (!PhysicalPosMoney.IsValid(response.Envelope)) return false;
        var data = response.Data;
        return data.ValueKind == JsonValueKind.Object && data.TryGetProperty("payment", out var payment) &&
            PhysicalPosWebhookEvent.Text(payment, "paymentId") is not null;
    }

    private static PhysicalPosOutcomeUnknownException OutcomeUnknown(Exception? last, LunixiResponse? unreadable, string idempotencyKey, int attempts)
    {
        if (last is LunixiApiException answered)
            return new PhysicalPosOutcomeUnknownException(idempotencyKey, attempts, answered.StatusCode, answered.Code, answered.RequestId,
                answered.Response, answered.RetryAfterSeconds ?? RetryAfterDetail(answered), last);
        return new PhysicalPosOutcomeUnknownException(idempotencyKey, attempts, unreadable?.StatusCode ?? 0, null, unreadable?.RequestId,
            unreadable?.Envelope, null, last);
    }

    /// <summary>
    /// Typed physical POS errors for one answered request: card data (field path), void after end-of-day
    /// (<c>allowedOperation</c>) and 429 (<c>Retry-After</c> or <c>details.retryAfterSeconds</c>), each with the key sent.
    /// Any other error is returned unchanged.
    /// </summary>
    internal static LunixiApiException Map(LunixiApiException error, string? idempotencyKey)
    {
        if (error is PhysicalPosCardDataRejectedException or PhysicalPosVoidAfterSettlementException or PhysicalPosRateLimitException or PhysicalPosOutcomeUnknownException)
            return error;
        if (error.Code == PhysicalPosCardDataRejectedException.ErrorCode)
            return new PhysicalPosCardDataRejectedException(error, DetailText(error, "field"), idempotencyKey);
        if (error.Code == PhysicalPosVoidAfterSettlementException.ErrorCode)
            return new PhysicalPosVoidAfterSettlementException(error, DetailText(error, "allowedOperation"), idempotencyKey);
        if (error.StatusCode == 429)
            return new PhysicalPosRateLimitException(error, error.RetryAfterSeconds ?? RetryAfterDetail(error), idempotencyKey);
        return error;
    }

    private static JsonElement? Details(LunixiApiException error)
        => error.Response is { ValueKind: JsonValueKind.Object } body && body.TryGetProperty("details", out var details) &&
           details.ValueKind == JsonValueKind.Object ? details : null;

    private static string? DetailText(LunixiApiException error, string name) => Details(error) is { } details ? LunixiClient.Property(details, name) : null;

    /// <summary><c>details.retryAfterSeconds</c> as a non-negative finite number (a JSON number or numeric text).</summary>
    private static double? RetryAfterDetail(LunixiApiException error)
    {
        if (Details(error) is not { } details || !details.TryGetProperty("retryAfterSeconds", out var value)) return null;
        double seconds;
        if (value.ValueKind == JsonValueKind.Number) seconds = value.GetDouble();
        else if (value.ValueKind != JsonValueKind.String ||
            !double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)) return null;
        return double.IsFinite(seconds) && seconds >= 0 ? seconds : null;
    }

    /// <summary><c>data[name]</c> when present and not null, otherwise <c>data</c>.</summary>
    internal static JsonElement Record(LunixiResponse response, string name)
        => response.Data.ValueKind == JsonValueKind.Object && response.Data.TryGetProperty(name, out var record) && record.ValueKind != JsonValueKind.Null
            ? record : response.Data;

    /// <summary>Walks every page, sending the previous page's <c>nextCursor</c> as <c>cursor</c> until the last page.</summary>
    internal static async IAsyncEnumerable<JsonElement> IterateAsync(
        Func<IReadOnlyDictionary<string, object?>, CancellationToken, Task<PhysicalPosPage>> fetchPage,
        IReadOnlyDictionary<string, object?>? filters, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = filters is null ? new Dictionary<string, object?>(StringComparer.Ordinal) : new Dictionary<string, object?>(filters, StringComparer.Ordinal);
        while (true)
        {
            var page = await fetchPage(request, cancellationToken).ConfigureAwait(false);
            foreach (var item in page.Items) yield return item;
            if (!page.HasMore || page.NextCursor is null) yield break;
            request["cursor"] = page.NextCursor;
        }
    }
}
