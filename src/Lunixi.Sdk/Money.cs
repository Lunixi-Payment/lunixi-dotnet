using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lunixi;

/// <summary>Canonical fiat minor-unit strings and exact decimal text conversion.</summary>
public static class Money
{
    /// <summary>Validate and decode canonical fiat text for exact internal arithmetic.</summary>
    public static long Parse(string value, bool positive = false, bool signed = false)
    {
        var pattern = signed ? @"\A(?:0|-?[1-9][0-9]*)\z" : positive ? @"\A[1-9][0-9]*\z" : @"\A(?:0|[1-9][0-9]*)\z";
        if (value is null || !Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant) || !long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var exact))
            throw new LunixiConfigurationException("Canonical minor-unit string in the signed 64-bit fiat range required.");
        return exact;
    }
    /// <summary>Convert decimal text with a supported currency exponent, without floating point.</summary>
    public static string DecimalToMinor(string value, string currency)
    {
        var exponent = currency switch { "JPY" => 0, "KWD" => 3, "TRY" or "USD" or "EUR" or "GBP" or "RUB" or "AED" or "SAR" or "QAR" or "CHF" or "CAD" or "AUD" or "CNY" or "AZN" => 2, _ => throw new LunixiConfigurationException("Unsupported currency.") };
        if (value is null || !Regex.IsMatch(value, @"\A(?:0|[1-9][0-9]*)(?:\.[0-9]+)?\z", RegexOptions.CultureInvariant)) throw new LunixiConfigurationException("Exact decimal text required.");
        var parts = value.Split('.'); var fraction = parts.Length == 2 ? parts[1] : "";
        if (fraction.Length > exponent && fraction[exponent..].Any(c => c != '0')) throw new LunixiConfigurationException("Currency precision exceeded.");
        var text = (parts[0] + fraction[..Math.Min(fraction.Length, exponent)].PadRight(exponent, '0')).TrimStart('0');
        if (text == "") text = "0";
        Parse(text); return text;
    }
    internal static void ValidateOperationAmount(string path, object? body)
    {
        if (body is null || !Regex.IsMatch(path, @"\A/api/v1/payments/(?:split/)?[^/]+/(?:capture|refund)\z")) return;
        var options = new JsonSerializerOptions(CanonicalJson.SerializerOptions) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never };
        var value = body is JsonElement json ? json : JsonSerializer.SerializeToElement(body, options);
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("amount", out var amount))
        {
            if (amount.ValueKind != JsonValueKind.String) throw new LunixiConfigurationException("An explicit operation amount must be a positive minor-unit string; omit it for the remaining balance.");
            Parse(amount.GetString()!, positive: true);
        }
    }
    internal static bool IsFiatRoute(string path) => Regex.IsMatch(path, @"\A/api/v1/(?:payments|wallet|insurance|bank-connect|fraud)(?:[/?]|\z)");
    /// <summary>The gateway currencies (ISO 4217, uppercase), in the Node SDK's order.</summary>
    internal static readonly IReadOnlyList<string> SupportedCurrencies =
        ["JPY", "KWD", "TRY", "USD", "EUR", "GBP", "RUB", "AED", "SAR", "QAR", "CHF", "CAD", "AUD", "CNY", "AZN"];

    /// <summary>A supported gateway currency, trimmed and upper-cased.</summary>
    internal static string SupportedCurrency(string? value, string field = "currency")
    {
        var currency = value?.Trim().ToUpperInvariant() ?? "";
        if (!SupportedCurrencies.Contains(currency))
            throw new LunixiConfigurationException($"{field} must be a supported currency ({string.Join(", ", SupportedCurrencies)}).");
        return currency;
    }

    /// <summary>The subscription and marketplace contracts carry money members (<see cref="ValidateWireFields"/>).</summary>
    internal static bool IsWireMoneyRoute(string path) => Regex.IsMatch(path, @"\A/api/v1/(?:subscriptions|marketplace)(?:/|\z)");

    /// <summary>Money members of the subscription and marketplace contracts (the members the PHP SDK checks on these routes).</summary>
    internal static readonly IReadOnlySet<string> WireMoneyFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "amount", "amountMinor", "price", "priceAmount", "paidAmount", "totalAmount", "subtotalAmount", "taxAmount", "amountPaid",
        "legAmount", "chargeAmount", "capturedAmount", "refundedAmount", "authorizedAmount", "compensatedAmount",
        "requestedAmount", "refundableAmount", "nonRefundableAmount", "disputedAmount", "legsAmount",
        "minAmount", "maxAmount", "minAmountMinor", "maxAmountMinor", "unitPriceMinor", "collectedAmountMinor",
        "premiumAmount", "collectedPremiumAmount", "inForcePremiumAmount", "cancelledPremiumAmount",
        "refundedPremiumAmount", "refundOpenPremiumAmount", "insuranceValueAmount", "priceSnapshotAmount",
        "sourceAmountMinor", "targetAmountMinor", "taxAmountMinor", "balanceMinor", "balanceAfterMinor",
        "availableMinor", "blockedMinor", "availableWithoutOverdraftMinor", "overdraftAvailableMinor",
        "sourceBalanceAfterMinor", "targetBalanceAfterMinor", "balance", "availableBalance", "blockedBalance",
        "feeAmount", "feeAmountMinor", "fixedAmountMinor", "minTransactionMinor", "commissionAmountMinor", "payoutAmountMinor",
        "settlementAmountMinor", "netAmountMinor", "grossAmountMinor", "fixedAmount", "fixedFee", "fixedFeeMinor", "mtdVolumeMinor",
        "orderAmountMinor", "capturedAmountMinor", "cumulativeCapturedMinor", "remainingCapturableMinor",
        "refundAmountMinor", "cumulativeRefundedMinor", "remainingRefundableMinor", "netAmount", "grossAmount",
    };

    /// <summary>Closed schema containers of those contracts; metadata, custom fields and provider bodies are opaque.</summary>
    private static readonly HashSet<string> WireMoneyContainers = new(StringComparer.Ordinal)
    {
        "data", "items", "legs", "steps", "pendingSteps", "schedule", "promissoryNote", "notes",
        "aggregate", "refund", "dispute", "details", "payment", "basketItems", "sellers", "scenario",
        "counters", "balances", "totals", "orders", "policies", "options", "quote", "order", "transfer",
        "commission", "commissionRule", "fee", "settlement", "payout", "context",
    };

    /// <summary>
    /// Subscription and marketplace bodies, filters and answers: every listed money member is a canonical minor-unit string
    /// (zero allowed, signed only for balances), checked the same way in both directions without coercion. The message names
    /// the member, never its value.
    /// </summary>
    internal static void ValidateWireFields(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array) { foreach (var item in value.EnumerateArray()) ValidateWireFields(item); return; }
        if (value.ValueKind != JsonValueKind.Object) return;
        foreach (var item in value.EnumerateObject())
        {
            if (item.Value.ValueKind == JsonValueKind.Null) continue;
            if (WireMoneyFields.Contains(item.Name)) WireAmount(item.Name, item.Value.ValueKind == JsonValueKind.String ? item.Value.GetString() : null);
            else if (item.Name == "suggestedAmountsMinor")
            {
                if (item.Value.ValueKind != JsonValueKind.Array) throw new LunixiConfigurationException("suggestedAmountsMinor must be an array.");
                foreach (var amount in item.Value.EnumerateArray())
                    WireAmount(item.Name, amount.ValueKind == JsonValueKind.String ? amount.GetString() : null);
            }
            else if (WireMoneyContainers.Contains(item.Name)) ValidateWireFields(item.Value);
        }
    }

    /// <summary>One money member of a subscription or marketplace contract: <paramref name="text"/> is null for a non-string value.</summary>
    internal static void WireAmount(string field, string? text)
    {
        var signed = field.Contains("balance", StringComparison.OrdinalIgnoreCase) || field is "availableMinor" or "availableWithoutOverdraftMinor";
        try { if (text is not null) { Parse(text, signed: signed); return; } }
        catch (LunixiConfigurationException) { }
        throw new LunixiConfigurationException($"{field} must be a canonical string in minor units.");
    }

    internal static void ValidateResponse(JsonElement value, bool write)
    {
        try { ValidateFields(value, true); }
        catch (LunixiConfigurationException) { throw new LunixiTransportException("Gateway response contains invalid canonical money.", write); }
    }
    internal static void ValidateFields(JsonElement value, bool response)
    {
        if (value.ValueKind == JsonValueKind.Array) { foreach (var item in value.EnumerateArray()) ValidateFields(item, response); return; }
        if (value.ValueKind != JsonValueKind.Object) return;
        foreach (var item in value.EnumerateObject())
        {
            var key = item.Name;
            if (item.Value.ValueKind == JsonValueKind.Null || key is "metadata" or "fields" or "inputFields" or "insuranceData") continue;
            if (key is "availableMinor" or "blockedMinor" or "availableWithoutOverdraftMinor" or "overdraftAvailableMinor" or "balanceAfterMinor" or "sourceBalanceAfterMinor" or "targetBalanceAfterMinor" or "amount" or "amountMinor" or "authorizedAmount" or "availableBalance" or "balanceMinor" or "buyerTotalCharge" or "cancelledPremiumAmount" or "capturedAmount" or "capturedAmountMinor" or "chargeAmount" or "collectedAmountMinor" or "collectedPremiumAmount" or "compensatedAmount" or "disputedAmount" or "feeTotal" or "fixedFee" or "inForcePremiumAmount" or "insuranceValueAmount" or "legAmount" or "legsAmount" or "lineTotalMinor" or "maxAmount" or "maxAmountMinor" or "minAmount" or "minAmountMinor" or "nonRefundableAmount" or "paidAmount" or "paidPrice" or "pendingBalance" or "premiumAmount" or "price" or "refundAmount" or "refundOpenPremiumAmount" or "refundableAmount" or "refundedAmount" or "refundedAmountMinor" or "refundedPremiumAmount" or "requestedAmount" or "reservedBalance" or "sourceAmountMinor" or "targetAmountMinor" or "taxAmountMinor" or "taxTotal" or "totalAmount" or "unitPriceMinor")
            {
                if (item.Value.ValueKind != JsonValueKind.String) throw new LunixiConfigurationException($"{key} requires a canonical minor-unit string.");
                Parse(item.Value.GetString()!, !response && key is "amount" or "price" or "totalAmount", key.Contains("balance", StringComparison.OrdinalIgnoreCase));
            }
            else if (key is "data" or "details" or "items" or "rows" or "results" or "payments" or "operations" or "balances" or "wallets" or "legs" or "aggregate" or "refund" or "refunds" or "totals" or "charge" or "fees" or "fee" or "balance" or "splits" or "sellers" or "basketItems" or "promissoryNote" or "schedule" or "notes" or "lines" or "offers" or "policies" or "reconciliation" or "quotes" or "payment" or "transactions" or "accounts" or "settlements" or "payouts" or "limits" or "options" or "quote") ValidateFields(item.Value, response);
        }
    }
}
