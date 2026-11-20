using System.Globalization;
using System.Text.Json;
using Lunixi;
using Microsoft.Data.Sqlite;

namespace Lunixi.AspNetCoreSample;

/// <summary>Persistent sample order and attempt, with money read from the server database.</summary>
public sealed record SampleAttempt(string OrderId, long Amount, string Currency, string State, string IdempotencyKey, DateTimeOffset CreatedAt);

/// <summary>
/// The order details every payment requires, stored with the order: the buyer, the billing address, the shipping address
/// (for physical goods) and the basket lines. The buyer's IP is left out: the Lunixi form takes it from the browser.
/// </summary>
public sealed record SampleOrderDetails(JsonElement Buyer, JsonElement BillingAddress, JsonElement? ShippingAddress, IReadOnlyList<BasketItem> BasketItems);

/// <summary>A locally bound payment candidate; webhook input cannot choose an unrelated payment query.</summary>
public sealed record KnownPayment(string OrderId, long Amount, string Currency, string PaymentId);

/// <summary>A verified event needs reconciliation and has deliberately not been acknowledged.</summary>
public sealed class SampleReconciliationException : Exception;

/// <summary>SQLite example using one transaction for the webhook inbox and order mutation.</summary>
public sealed class SampleStore(string databasePath)
{
    private readonly string connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate, DefaultTimeout = 5 }.ToString();
    private SqliteConnection Open() { var connection = new SqliteConnection(connectionString); connection.Open(); return connection; }

    /// <summary>Creates the schema and one server-priced demo order.</summary>
    public void Initialize()
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS orders(order_id TEXT PRIMARY KEY, amount INTEGER NOT NULL, currency TEXT NOT NULL, payment_id TEXT UNIQUE, state TEXT NOT NULL DEFAULT 'UNPAID');
            CREATE TABLE IF NOT EXISTS attempts(order_id TEXT PRIMARY KEY REFERENCES orders(order_id), operation_key TEXT NOT NULL UNIQUE, created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS webhook_inbox(event_id TEXT PRIMARY KEY, body_sha256 TEXT NOT NULL, completed_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS order_details(order_id TEXT PRIMARY KEY REFERENCES orders(order_id), buyer TEXT NOT NULL, billing_address TEXT NOT NULL, shipping_address TEXT, basket_items TEXT NOT NULL);
            INSERT OR IGNORE INTO orders(order_id,amount,currency) VALUES('DEMO-1001',10050,'TRY');
            INSERT OR IGNORE INTO order_details(order_id,buyer,billing_address,shipping_address,basket_items) VALUES('DEMO-1001',
              '{"name":"Demo","surname":"Buyer","identityNumber":"11111111111","email":"buyer@example.com","gsmNumber":"+905350000000","city":"Istanbul","country":"Turkey","zipCode":"34000"}',
              '{"contactName":"Demo Buyer","address":"Example Street 1","city":"Istanbul","country":"Turkey","zipCode":"34000"}',
              NULL,
              '[{"id":"DEMO-SKU","name":"Demo product","category1":"Demo","itemType":"VIRTUAL","price":"10050"}]');
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>Reads the order details stored with the order; null when the order has none (it cannot be paid).</summary>
    public SampleOrderDetails? GetOrderDetails(string orderId)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT buyer,billing_address,shipping_address,basket_items FROM order_details WHERE order_id=$order";
        command.Parameters.AddWithValue("$order", orderId); using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        using var basket = JsonDocument.Parse(reader.GetString(3));
        var items = basket.RootElement.EnumerateArray().Select(line =>
            new BasketItem(Text(line, "id")!, Text(line, "name")!, Text(line, "price")!, Text(line, "itemType")!) { Category1 = Text(line, "category1") }).ToArray();
        return new(Json(reader.GetString(0)), Json(reader.GetString(1)), reader.IsDBNull(2) ? null : Json(reader.GetString(2)), items);
    }

    private static JsonElement Json(string value) { using var document = JsonDocument.Parse(value); return document.RootElement.Clone(); }

    /// <summary>Atomically persists a checkout-attempt key before contacting the gateway.</summary>
    public SampleAttempt? GetOrCreateAttempt(string orderId)
    {
        using var connection = Open(); using var transaction = connection.BeginTransaction(); using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT OR IGNORE INTO attempts(order_id,operation_key,created_at) SELECT order_id,$key,$now FROM orders WHERE order_id=$order";
        insert.Parameters.AddWithValue("$key", "checkout:" + Guid.NewGuid().ToString("N"));
        insert.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O")); insert.Parameters.AddWithValue("$order", orderId); insert.ExecuteNonQuery();
        using var query = connection.CreateCommand(); query.Transaction = transaction;
        query.CommandText = "SELECT o.order_id,o.amount,o.currency,o.state,a.operation_key,a.created_at FROM orders o JOIN attempts a ON a.order_id=o.order_id WHERE o.order_id=$order";
        query.Parameters.AddWithValue("$order", orderId); using var reader = query.ExecuteReader();
        SampleAttempt? result = reader.Read() ? new(reader.GetString(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture)) : null;
        reader.Close(); transaction.Commit(); return result;
    }

    /// <summary>Binds only the gateway's authenticated intent identifier; conflicting attempts fail closed.</summary>
    public void BindPayment(string orderId, string paymentId)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "UPDATE orders SET payment_id=$payment WHERE order_id=$order AND (payment_id IS NULL OR payment_id=$payment)";
        command.Parameters.AddWithValue("$payment", paymentId); command.Parameters.AddWithValue("$order", orderId);
        if (command.ExecuteNonQuery() != 1) throw new SampleReconciliationException();
    }

    /// <summary>Reads the server's order state, never a browser-return status.</summary>
    public string? GetState(string orderId)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT state FROM orders WHERE order_id=$order"; command.Parameters.AddWithValue("$order", orderId);
        return command.ExecuteScalar() as string;
    }

    /// <summary>Checks a previously committed delivery before making another gateway call.</summary>
    public bool IsWebhookComplete(LunixiWebhook webhook)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT body_sha256 FROM webhook_inbox WHERE event_id=$id"; command.Parameters.AddWithValue("$id", webhook.Id);
        if (command.ExecuteScalar() is not string hash) return false;
        if (hash != webhook.BodySha256) throw new LunixiWebhookException("Conflicting webhook delivery.");
        return true;
    }

    /// <summary>Locates a known local payment using the signed event identifier, without trusting the event's settlement status.</summary>
    public KnownPayment FindKnownPayment(LunixiWebhook webhook, string merchantId)
    {
        var data = webhook.Data;
        if (data.ValueKind != JsonValueKind.Object || data.TryGetProperty("splitLeg", out _) || data.TryGetProperty("settlementMode", out _) || data.TryGetProperty("promissoryNote", out _))
            throw new SampleReconciliationException();
        var id = Text(data, "paymentId") ?? Text(data, "intentId");
        if (!ValidIdentifier(id) || data.TryGetProperty("paymentId", out _) && data.TryGetProperty("intentId", out _) && Text(data, "paymentId") != Text(data, "intentId"))
            throw new SampleReconciliationException();
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT order_id,amount,currency FROM orders WHERE payment_id=$payment";
        command.Parameters.AddWithValue("$payment", id!); using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new SampleReconciliationException();
        var known = new KnownPayment(reader.GetString(0), reader.GetInt64(1), reader.GetString(2), id!);
        if (data.TryGetProperty("merchantId", out _) && Text(data, "merchantId") != merchantId ||
            data.TryGetProperty("orderId", out _) && Text(data, "orderId") != known.OrderId ||
            data.TryGetProperty("currency", out _) && Text(data, "currency") != known.Currency ||
            webhook.Type == "payment.captured" && Money(data, "capturedAmountMinor") != known.Amount)
            throw new SampleReconciliationException();
        return known;
    }

    /// <summary>Atomically commits inbox and local order changes only after full authenticated payment/merchant/settlement validation.</summary>
    public void ApplyWebhook(LunixiWebhook webhook, KnownPayment known, LunixiResponse authenticatedPayment, string merchantId)
    {
        var payment = authenticatedPayment.Data;
        if (payment.ValueKind != JsonValueKind.Object || Text(payment, "id") != known.PaymentId || Text(payment, "merchantId") != merchantId ||
            Text(payment, "orderId") != known.OrderId || Text(payment, "currency") != known.Currency || Money(payment, "amount") != known.Amount ||
            Text(payment, "paymentMethod") == "PROMISSORY_NOTE" ||
            payment.TryGetProperty("split", out var split) && split.ValueKind != JsonValueKind.Null)
            throw new SampleReconciliationException();
        var details = authenticatedPayment.Envelope.TryGetProperty("details", out var outerDetails) ? outerDetails :
            payment.TryGetProperty("details", out var innerDetails) ? innerDetails : default;
        var captured = Money(details, "capturedAmount", allowZero: true);
        var refunded = Money(details, "refundedAmount", allowZero: true);
        if (captured is null || refunded is null || captured > known.Amount || refunded > captured) throw new SampleReconciliationException();
        var state = Text(payment, "status") switch
        {
            "CAPTURED" when captured == known.Amount && refunded == 0 => "PAID",
            "REFUNDED" when captured == known.Amount && refunded == captured => "REFUNDED",
            "PARTIALLY_REFUNDED" when captured == known.Amount && refunded > 0 && refunded < captured => "PARTIALLY_REFUNDED",
            "AUTHORIZED" when captured == 0 && refunded == 0 && webhook.Type == "payment.authorized" => "AUTHORIZED",
            _ => throw new SampleReconciliationException(),
        };
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        using (var existing = connection.CreateCommand())
        {
            existing.Transaction = transaction; existing.CommandText = "SELECT body_sha256 FROM webhook_inbox WHERE event_id=$id"; existing.Parameters.AddWithValue("$id", webhook.Id);
            if (existing.ExecuteScalar() is string hash)
            {
                if (hash != webhook.BodySha256) throw new LunixiWebhookException("Conflicting webhook delivery.");
                transaction.Commit(); return;
            }
        }
        {
            using var update = connection.CreateCommand(); update.Transaction = transaction;
            // Preserve a later committed settlement when an authenticated read is temporarily stale.
            update.CommandText = """
                UPDATE orders SET state=CASE
                    WHEN state='REFUNDED' THEN state
                    WHEN state='PARTIALLY_REFUNDED' AND $state IN ('PAID','AUTHORIZED') THEN state
                    WHEN state='PAID' AND $state='AUTHORIZED' THEN state
                    ELSE $state END
                WHERE order_id=$order AND payment_id=$payment AND amount=$amount AND currency=$currency
                """;
            update.Parameters.AddWithValue("$state", state);
            update.Parameters.AddWithValue("$order", known.OrderId); update.Parameters.AddWithValue("$payment", known.PaymentId); update.Parameters.AddWithValue("$amount", known.Amount); update.Parameters.AddWithValue("$currency", known.Currency);
            if (update.ExecuteNonQuery() != 1) throw new SampleReconciliationException();
        }
        using var insert = connection.CreateCommand(); insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO webhook_inbox(event_id,body_sha256,completed_at) VALUES($id,$hash,$now)";
        insert.Parameters.AddWithValue("$id", webhook.Id); insert.Parameters.AddWithValue("$hash", webhook.BodySha256); insert.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        insert.ExecuteNonQuery(); transaction.Commit();
    }
    /// <summary>Extracts one bounded JSON string without converting other JSON value types.</summary>
    public static string? Text(JsonElement value, string field) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(field, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    /// <summary>Checks an opaque gateway resource identifier before binding or querying it.</summary>
    public static bool ValidIdentifier(string? value) => value is { Length: > 0 and <= 256 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or ':');
    private static long? Money(JsonElement value, string field, bool allowZero = false)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(field, out var item)) return null;
        var minimum = allowZero ? 0 : 1;
        if (item.ValueKind == JsonValueKind.String)
        {
            try { var exact = Lunixi.Money.Parse(item.GetString()!, positive: !allowZero); return exact; }
            catch (LunixiConfigurationException) { return null; }
        }
        return null;
    }
}
