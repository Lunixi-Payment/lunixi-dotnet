using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lunixi;

/// <summary>One immutable gateway response, including the full envelope and HTTP metadata.</summary>
public sealed class LunixiResponse
{
    /// <summary>The HTTP status, including 202 for pending operations.</summary>
    public int StatusCode { get; }
    /// <summary>The complete JSON envelope.</summary>
    public JsonElement Envelope { get; }
    /// <summary>The envelope's data member, or the root when no data member exists.</summary>
    public JsonElement Data => Envelope.ValueKind == JsonValueKind.Object && Envelope.TryGetProperty("data", out var data) ? data : Envelope;
    /// <summary>The server request identifier, if present.</summary>
    public string? RequestId { get; }
    internal LunixiResponse(int status, JsonElement envelope, string? requestId)
    { StatusCode = status; Envelope = envelope.Clone(); RequestId = Validation.SafeIdentifier(requestId); }
}

/// <summary>Reusable, thread-safe server-side Lunixi client. Dispose it when the application stops.</summary>
public sealed class LunixiClient : IDisposable
{
    private readonly LunixiOptions options;
    private readonly HttpClient http;
    private readonly Ed25519Signer signer;
    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private string? accessToken;
    private DateTimeOffset tokenExpiresAt;
    private bool disposed;
    /// <summary>Payment intent, status, capture, refund, void and split-payment operations.</summary>
    public PaymentsClient Payments { get; }
    /// <summary>Fraud decisions, event ingest and decision-log reads.</summary>
    public FraudClient Fraud { get; }

    /// <summary>Creates a client with an SDK-owned HTTP handler that never follows redirects.</summary>
    public LunixiClient(LunixiOptions options) : this(options, new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    })
    { }

    /// <summary>Creates a client with a trusted transport handler, primarily for tests and instrumentation.</summary>
    /// <remarks>Known HTTP handlers with automatic redirects enabled are rejected. Custom handlers must preserve the request origin and never log credentials.</remarks>
    public LunixiClient(LunixiOptions options, HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(options); ArgumentNullException.ThrowIfNull(handler);
        options.Validate(); ValidateHandler(handler);
        this.options = options;
        signer = new Ed25519Signer(options.PrivateKeyPem);
        http = new HttpClient(handler, true) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        Payments = new PaymentsClient(this); Fraud = new FraudClient(this);
    }

    /// <summary>Sends a JSON request to a documented relative gateway API route.</summary>
    /// <remarks>Use typed clients where available. Generic calls do not validate product-specific DTOs. The caller must supply a persisted idempotency key for financial writes.</remarks>
    public async Task<LunixiResponse> SendAsync(HttpMethod method, string path, object? body = null,
        string? idempotencyKey = null, bool signed = true, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(method);
        var uri = Resolve(path);
        if (method != HttpMethod.Get && method != HttpMethod.Post && method != HttpMethod.Put &&
            method != HttpMethod.Patch && method != HttpMethod.Delete && method != HttpMethod.Head)
            throw new LunixiConfigurationException("Unsupported HTTP method.");
        if ((method == HttpMethod.Get || method == HttpMethod.Head) && body is not null)
            throw new LunixiConfigurationException("GET and HEAD requests cannot carry a body.");
        if (idempotencyKey is not null) Validation.IdempotencyKey(idempotencyKey);
        var bytes = body is null ? null : CanonicalJson.Serialize(body);
        if (bytes is not null && uri.AbsolutePath.StartsWith("/api/v1/payments/", StringComparison.Ordinal))
        {
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                (document.RootElement.TryGetProperty("price", out _) || document.RootElement.TryGetProperty("paidPrice", out _)))
                throw new LunixiConfigurationException("Root price and paidPrice are removed. Use integer minor-unit amount or totalAmount; basket line price remains supported.");
        }
        var write = method != HttpMethod.Get && method != HttpMethod.Head;
        var retryable = !write || idempotencyKey is not null;
        var retries = 0; var refreshed = false;
        while (true)
        {
            var token = await TokenAsync(cancellationToken).ConfigureAwait(false);
            using var request = Request(method, uri, bytes, signed, idempotencyKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.Timeout);
            HttpResponseMessage response;
            try { response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false); }
            catch (Exception ex) when ((ex is HttpRequestException or OperationCanceledException) && !cancellationToken.IsCancellationRequested)
            {
                if (retryable && retries < options.MaxRetries)
                { await DelayAsync(++retries, cancellationToken).ConfigureAwait(false); continue; }
                throw new LunixiTransportException("Gateway connection failed or timed out. For a write, reconcile status or reuse the original operation key.", write);
            }
            using (response)
            {
                if ((int)response.StatusCode is >= 300 and <= 399)
                    throw new LunixiTransportException("The gateway returned a redirect. Redirects are never followed.", write);
                if (response.StatusCode == HttpStatusCode.Unauthorized && !refreshed)
                { await InvalidateTokenAsync(token, cancellationToken).ConfigureAwait(false); refreshed = true; continue; }
                if ((int)response.StatusCode >= 500 && retryable && retries < options.MaxRetries)
                { await DelayAsync(++retries, cancellationToken).ConfigureAwait(false); continue; }
                var payload = await ReadJsonAsync(response, timeout.Token, cancellationToken, write).ConfigureAwait(false);
                var requestId = Header(response, "x-request-id");
                if (response.IsSuccessStatusCode) return new LunixiResponse((int)response.StatusCode, payload, requestId);
                throw Error(response, payload, requestId);
            }
        }
    }

    private HttpRequestMessage Request(HttpMethod method, Uri uri, byte[]? body, bool signed, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("Lunixi.Sdk/0.1.0");
        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        }
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        if (signed)
        {
            var date = options.TimeProvider.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            var nonce = Guid.NewGuid().ToString("N");
            var digest = body is null ? null : "SHA-256=" + Convert.ToBase64String(SHA256.HashData(body));
            var canonical = CanonicalRequest(method.Method, uri.PathAndQuery, date, nonce, digest);
            request.Headers.Add("X-Key-Id", options.KeyId); request.Headers.Add("X-Date", date);
            request.Headers.Add("X-Nonce", nonce); request.Headers.Add("X-Signature", signer.Sign(Encoding.UTF8.GetBytes(canonical)));
            if (digest is not null) request.Headers.Add("Digest", digest);
        }
        return request;
    }

    /// <summary>Builds the exact newline-delimited gateway signing string.</summary>
    public static string CanonicalRequest(string method, string pathAndQuery, string date, string nonce, string? digest = null)
        => string.Join('\n', new[] { method.ToUpperInvariant(), pathAndQuery, "X-Date:" + date, "X-Nonce:" + nonce }
            .Concat(string.IsNullOrEmpty(digest) ? [] : new[] { "Digest:" + digest }));

    private async Task<string> TokenAsync(CancellationToken cancellationToken)
    {
        await tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (accessToken is not null && tokenExpiresAt > options.TimeProvider.GetUtcNow()) return accessToken;
            using var request = Request(HttpMethod.Post, Resolve("/api/v1/auth/token"), null, true, null);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.Timeout);
            HttpResponseMessage response;
            try { response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false); }
            catch (Exception ex) when ((ex is HttpRequestException or OperationCanceledException) && !cancellationToken.IsCancellationRequested)
            { throw new LunixiTransportException("Lunixi authentication could not be completed."); }
            using (response)
            {
                if ((int)response.StatusCode is >= 300 and <= 399)
                    throw new LunixiTransportException("Authentication returned a redirect. Redirects are never followed.");
                var payload = await ReadJsonAsync(response, timeout.Token, cancellationToken, false).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) throw Error(response, payload, Header(response, "x-request-id"));
                var data = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("data", out var inner) && inner.ValueKind == JsonValueKind.Object ? inner : payload;
                var token = Property(data, "access_token") ?? Property(data, "accessToken");
                if (string.IsNullOrEmpty(token) || token.Length > 8192 || token.Any(c => c < 0x21 || c > 0x7e))
                    throw new LunixiTransportException("Authentication returned an invalid access token.");
                var ttl = 1800;
                if (data.TryGetProperty("expires_in", out var value) &&
                    (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out ttl)))
                    throw new LunixiTransportException("Authentication returned an invalid token lifetime.");
                if (ttl is < 1 or > 86400) throw new LunixiTransportException("Authentication returned an invalid token lifetime.");
                accessToken = token;
                tokenExpiresAt = options.TimeProvider.GetUtcNow().AddSeconds(Math.Max(0, ttl - 30));
                return token;
            }
        }
        finally { tokenLock.Release(); }
    }
    private async Task InvalidateTokenAsync(string usedToken, CancellationToken cancellationToken)
    {
        await tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { if (accessToken == usedToken) { accessToken = null; tokenExpiresAt = default; } }
        finally { tokenLock.Release(); }
    }
    private Uri Resolve(string path)
    {
        if (path is null || !path.StartsWith("/api/v1/", StringComparison.Ordinal) || path.Any(char.IsControl) ||
            path.Contains('\\') || path.Contains('#') || path.Split('?')[0].Split('/').Any(s => s is "." or ".."))
            throw new LunixiConfigurationException("API routes must be relative /api/v1/ paths without fragments or traversal.");
        var uri = new Uri(options.BaseUri, path);
        if (uri.GetLeftPart(UriPartial.Authority) != options.BaseUri.GetLeftPart(UriPartial.Authority) ||
            !uri.AbsolutePath.StartsWith("/api/v1/", StringComparison.Ordinal))
            throw new LunixiConfigurationException("API requests cannot leave the configured gateway origin.");
        return uri;
    }
    private async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken token, CancellationToken callerToken, bool write)
    {
        try
        {
            if (response.Content.Headers.ContentLength > options.MaxResponseBytes)
                throw new LunixiTransportException("Gateway response exceeds the configured size limit.", write);
            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var memory = new MemoryStream(); var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
            {
                if (memory.Length + read > options.MaxResponseBytes)
                    throw new LunixiTransportException("Gateway response exceeds the configured size limit.", write);
                memory.Write(buffer, 0, read);
            }
            if (memory.Length == 0)
            { using var empty = JsonDocument.Parse("{}"); return empty.RootElement.Clone(); }
            using var document = JsonDocument.Parse(memory.ToArray(), new JsonDocumentOptions { MaxDepth = 64 });
            if (document.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                throw new LunixiTransportException("Gateway response must be a JSON object or array.", write);
            return document.RootElement.Clone();
        }
        catch (JsonException) { throw new LunixiTransportException("Gateway returned malformed JSON.", write); }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        { throw new LunixiTransportException("Gateway response timed out.", write); }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        { throw new LunixiTransportException("Gateway response could not be read.", write); }
    }
    private static LunixiApiException Error(HttpResponseMessage response, JsonElement payload, string? requestId)
    {
        var code = Property(payload, "code"); var status = (int)response.StatusCode;
        double? retryAfter = double.TryParse(Header(response, "retry-after"), NumberStyles.Float, CultureInfo.InvariantCulture, out var retry) && retry >= 0 && double.IsFinite(retry) ? retry : null;
        return status switch
        {
            400 or 422 => new LunixiValidationException(status, code, requestId, payload),
            429 => new LunixiRateLimitException(code, requestId, payload, retryAfter),
            _ => new LunixiApiException(status, code, requestId, payload, retryAfter),
        };
    }
    internal static string? Property(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string? Header(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
    private Task DelayAsync(int attempt, CancellationToken cancellationToken)
        => Task.Delay(TimeSpan.FromMilliseconds(Math.Min(2000, options.RetryDelay.TotalMilliseconds * Math.Pow(2, attempt - 1))), cancellationToken);
    private static void ValidateHandler(HttpMessageHandler handler)
    {
        for (HttpMessageHandler? current = handler; current is not null; current = (current as DelegatingHandler)?.InnerHandler)
            if (current is HttpClientHandler { AllowAutoRedirect: true } or SocketsHttpHandler { AllowAutoRedirect: true })
                throw new LunixiConfigurationException("HTTP handlers must disable automatic redirects before receiving credentials.");
    }
    /// <summary>Disposes the HTTP transport and private signing key.</summary>
    /// <remarks>Call only after in-flight requests have completed, normally when the application stops.</remarks>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; accessToken = null; signer.Dispose(); http.Dispose(); tokenLock.Dispose();
    }
    /// <summary>Returns no credentials or request data.</summary>
    public override string ToString() => "LunixiClient { Credentials = [REDACTED] }";
}
