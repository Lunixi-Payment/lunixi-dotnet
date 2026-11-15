using System.Text.Json;

namespace Lunixi;

/// <summary>
/// Bank accounts (<c>/api/v1/bank-connect</c>, product <c>BANK_CONNECT</c>): read your connected bank accounts, balances and
/// transactions. These routes accept the API key's per-request signature only, so every call is signed, reads included.
/// Scopes: <c>bank-connect:view</c> for banks, connections, accounts and <see cref="RequestSyncAsync"/>;
/// <c>bank-connect:transactions:view</c> for balances and transactions. The key decides the environment (TEST or LIVE).
/// <para>
/// Connecting a bank and entering bank credentials happen in the Lunixi dashboard only. Money movement lives on
/// <see cref="Transfers"/> and <see cref="Fx"/> (scope <c>bank-connect:transfer</c> and a key with an IP allowlist).
/// </para>
/// <para>
/// Filters are checked before anything is sent (<see cref="BankConnectFilters"/>): an unknown filter, a value outside its
/// set, <c>limit</c> outside 1–100, more than 100 account ids, a non-canonical amount, <c>minAmountMinor</c> above
/// <c>maxAmountMinor</c>, <c>bookedTo</c> without <c>bookedFrom</c> or a window wider than 92 days is a
/// <see cref="LunixiConfigurationException"/>. Methods return the envelope's <c>data</c>; lists return <see cref="LunixiPage"/>.
/// </para>
/// </summary>
public sealed class BankConnectClient
{
    internal const string BasePath = "/api/v1/bank-connect";
    private readonly LunixiClient client;

    internal BankConnectClient(LunixiClient client)
    {
        this.client = client;
        Transfers = new BankTransferClient(client); Fx = new BankFxClient(client);
    }

    /// <summary>Outgoing transfers from a connected account (<c>bank-connect:transfer</c>).</summary>
    public BankTransferClient Transfers { get; }
    /// <summary>Currency exchange between two of your accounts on one connection (<c>bank-connect:transfer</c>).</summary>
    public BankFxClient Fx { get; }

    /// <summary>The bank catalogue: whether each bank can be connected and what is read from it; filters <c>limit</c>, <c>cursor</c>.</summary>
    public Task<LunixiPage> ListBanksAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => PageAsync("/banks", "listBanks", filters, cancellationToken);

    /// <summary>Every bank of <see cref="ListBanksAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateBanksAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => RouteRequest.IterateAsync(ListBanksAsync, filters, cancellationToken);

    /// <summary>Your bank connections, newest first; filters <c>limit</c>, <c>cursor</c>, <c>status</c> (one value or a list), <c>bankId</c>.</summary>
    public Task<LunixiPage> ListConnectionsAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => PageAsync("/connections", "listConnections", filters, cancellationToken);

    /// <summary>Every connection of <see cref="ListConnectionsAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateConnectionsAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => RouteRequest.IterateAsync(ListConnectionsAsync, filters, cancellationToken);

    /// <summary>One connection; 404 <c>bank_connect.connection.not_found</c> for an unknown one.</summary>
    public Task<JsonElement> GetConnectionAsync(string connectionId, CancellationToken cancellationToken = default)
        => ReadAsync("/connections/" + RouteRequest.PathId(connectionId, "connectionId"), "", cancellationToken);

    /// <summary>
    /// Your connected accounts, newest first, each with its data <c>freshness</c>; filters <c>limit</c>, <c>cursor</c>,
    /// <c>connectionId</c>, <c>bankId</c>, <c>currency</c>, <c>status</c>.
    /// </summary>
    public Task<LunixiPage> ListAccountsAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => PageAsync("/accounts", "listAccounts", filters, cancellationToken);

    /// <summary>Every account of <see cref="ListAccountsAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateAccountsAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => RouteRequest.IterateAsync(ListAccountsAsync, filters, cancellationToken);

    /// <summary>One account; 404 <c>bank_connect.account.not_found</c> for an unknown one.</summary>
    public Task<JsonElement> GetAccountAsync(string accountId, CancellationToken cancellationToken = default)
        => ReadAsync(AccountPath(accountId), "", cancellationToken);

    /// <summary>
    /// Asks for a fresh read of the account from the bank (HTTP 202). It never reads earlier than the bank's minimum interval:
    /// then the answer is <c>SCHEDULED</c> with <c>scheduledAt</c>, and a request while one already waits joins it. Nothing is
    /// written, so no <c>Idempotency-Key</c> is sent; new transactions arrive by the <c>bank_account.transaction.received</c>
    /// webhook. 409 <c>bank_connect.connection.not_active</c> when the connection is not ACTIVE or DEGRADED; 429
    /// <c>RATE_LIMITED</c> (<see cref="LunixiRateLimitException"/> with <c>RetryAfterSeconds</c>) after too many sync requests.
    /// </summary>
    public async Task<JsonElement> RequestSyncAsync(string accountId, CancellationToken cancellationToken = default)
        => (await client.SendCoreAsync(HttpMethod.Post, BasePath + AccountPath(accountId) + "/sync", null, null, true,
            Validation.MaxIdempotencyKeyLength, cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>
    /// The latest balance of each account (scope <c>bank-connect:transactions:view</c>); filters <c>limit</c>, <c>cursor</c>,
    /// <c>accountId</c> (one id or a list of up to 100), <c>currency</c>. Without <c>accountId</c>, every active account.
    /// </summary>
    public Task<LunixiPage> ListBalancesAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => PageAsync("/balances", "listBalances", filters, cancellationToken);

    /// <summary>Every balance of <see cref="ListBalancesAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateBalancesAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => RouteRequest.IterateAsync(ListBalancesAsync, filters, cancellationToken);

    /// <summary>
    /// Transactions, newest recorded first (scope <c>bank-connect:transactions:view</c>). The order is <c>receivedAt</c>, which
    /// never changes, so paging forward never skips a transaction that arrives late with an older bank date. Filters
    /// <c>limit</c>, <c>cursor</c>, <c>accountId</c>, <c>bookedFrom</c> (inclusive) / <c>bookedTo</c> (exclusive) as a
    /// <see cref="DateTimeOffset"/> or ISO-8601 text with a time zone at most 92 days apart (<c>bookedTo</c> needs
    /// <c>bookedFrom</c>), <c>direction</c>, inclusive <c>minAmountMinor</c> / <c>maxAmountMinor</c> (minor-unit strings),
    /// <c>canonicalType</c> (one value or a list).
    /// </summary>
    public Task<LunixiPage> ListTransactionsAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
    {
        var query = BankConnectQuery.Build(filters, "listTransactions");
        BankConnectQuery.CheckTransactionRules(filters);
        return PageWithQueryAsync("/transactions", query, cancellationToken);
    }

    /// <summary>Every transaction of <see cref="ListTransactionsAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateTransactionsAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => RouteRequest.IterateAsync(ListTransactionsAsync, filters, cancellationToken);

    /// <summary>One transaction with its detail fields (<c>balanceAfterMinor</c>, <c>documentNo</c>, <c>bank</c>).</summary>
    public Task<JsonElement> GetTransactionAsync(string transactionId, CancellationToken cancellationToken = default)
        => ReadAsync("/transactions/" + RouteRequest.PathId(transactionId, "transactionId"), "", cancellationToken);

    private static string AccountPath(string accountId) => "/accounts/" + RouteRequest.PathId(accountId, "accountId");

    private Task<LunixiPage> PageAsync(string path, string route, IReadOnlyDictionary<string, object?>? filters, CancellationToken cancellationToken)
        => PageWithQueryAsync(path, BankConnectQuery.Build(filters, route), cancellationToken);

    private async Task<LunixiPage> PageWithQueryAsync(string path, string query, CancellationToken cancellationToken)
        => new(await client.SendCoreAsync(HttpMethod.Get, BasePath + path + query, null, null, true, Validation.MaxIdempotencyKeyLength, cancellationToken)
            .ConfigureAwait(false));

    private async Task<JsonElement> ReadAsync(string path, string query, CancellationToken cancellationToken)
        => (await client.SendCoreAsync(HttpMethod.Get, BasePath + path + query, null, null, true, Validation.MaxIdempotencyKeyLength, cancellationToken)
            .ConfigureAwait(false)).Data;
}

/// <summary>The bank-connect read filters: names from <see cref="BankConnectFilters.All"/>, values checked one by one.</summary>
internal static class BankConnectQuery
{
    internal static string Build(IReadOnlyDictionary<string, object?>? filters, string route)
        => RouteRequest.Query(filters, BankConnectFilters.All[route], "Bank-connect " + route, (key, value) => Value(key, value, route));

    private static string Value(string key, object value, string route) => key switch
    {
        "limit" => Limit(value),
        "cursor" => Cursor(value),
        "status" when route == "listAccounts" => One(key, value, BankAccountStatuses.All),
        "status" => string.Join(',', RouteRequest.Values(key, value, BankConnectionStatuses.All)),
        "direction" => One(key, value, BankTransactionDirections.All),
        "canonicalType" => string.Join(',', RouteRequest.Values(key, value, BankTransactionTypes.All)),
        "accountId" => AccountIds(value),
        "bankId" or "connectionId" => RouteRequest.Id(value as string, key),
        "currency" => RouteRequest.Currency(value, key),
        "bookedFrom" or "bookedTo" => RouteRequest.Time(key, value),
        "minAmountMinor" or "maxAmountMinor" => Amount(key, value),
        _ => throw new LunixiConfigurationException($"Unknown bank-connect filter '{key}'."),
    };

    internal static string Limit(object value)
    {
        if (RouteRequest.Integer(value) is not { } limit || limit is < 1 or > BankConnectFilters.MaxLimit)
            throw new LunixiConfigurationException($"'limit' must be an integer from 1 to {BankConnectFilters.MaxLimit}.");
        return limit.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static string Cursor(object value)
    {
        if (value is not string cursor || cursor.Length > BankConnectFilters.CursorMaxLength)
            throw new LunixiConfigurationException("'cursor' must be the nextCursor of the previous page, passed back unchanged.");
        return cursor;
    }

    private static string One(string key, object value, IReadOnlyList<string> allowed)
    {
        if (value is not string text || !allowed.Contains(text)) throw new LunixiConfigurationException($"'{key}' must be one of {string.Join(", ", allowed)}.");
        return text;
    }

    private static string AccountIds(object value)
    {
        string[]? ids = value is string single ? [single] : value is IEnumerable<string> list ? list.ToArray() : null;
        if (ids is null) throw new LunixiConfigurationException("'accountId' must be an account id or a list of account ids.");
        if (ids.Length > BankConnectFilters.MaxAccountIds)
            throw new LunixiConfigurationException($"'accountId' takes at most {BankConnectFilters.MaxAccountIds} account ids.");
        return string.Join(',', ids.Select(id =>
        {
            if (string.IsNullOrWhiteSpace(id) || id.Contains(',')) throw new LunixiConfigurationException("'accountId' must be account ids (strings without commas).");
            return id.Trim();
        }));
    }

    private static string Amount(string key, object value)
    {
        if (value is string text)
        {
            try { Money.Parse(text); return text; }
            catch (LunixiConfigurationException) { }
        }
        throw new LunixiConfigurationException($"'{key}' must be a canonical minor-unit string (for example \"10000\").");
    }

    /// <summary>
    /// The gateway's window and amount rules, checked locally when both ends are readable: <c>bookedTo</c> needs
    /// <c>bookedFrom</c>, <c>bookedFrom</c> ≤ <c>bookedTo</c>, at most 92 days, <c>minAmountMinor</c> ≤ <c>maxAmountMinor</c>.
    /// </summary>
    internal static void CheckTransactionRules(IReadOnlyDictionary<string, object?>? filters)
    {
        if (filters is null) return;
        var from = Given(filters, "bookedFrom"); var to = Given(filters, "bookedTo");
        if (to is not null && from is null) throw new LunixiConfigurationException("'bookedTo' needs 'bookedFrom': a window without a start is unbounded.");
        if (RouteRequest.ReadTime(from) is { } start && RouteRequest.ReadTime(to) is { } end)
        {
            if (start > end) throw new LunixiConfigurationException("'bookedFrom' must not be after 'bookedTo'.");
            if (end - start > TimeSpan.FromDays(BankConnectFilters.BookedRangeMaxDays))
                throw new LunixiConfigurationException(
                    $"'bookedFrom' and 'bookedTo' may be at most {BankConnectFilters.BookedRangeMaxDays} days apart. Page through narrower windows.");
        }
        if (Given(filters, "minAmountMinor") is string min && Given(filters, "maxAmountMinor") is string max && Money.Parse(min) > Money.Parse(max))
            throw new LunixiConfigurationException("'minAmountMinor' must not be greater than 'maxAmountMinor'.");
    }

    private static object? Given(IReadOnlyDictionary<string, object?> filters, string key)
        => filters.TryGetValue(key, out var value) && value is not null && value is not "" ? value : null;
}
