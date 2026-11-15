using System.Globalization;
using System.Text.Json;

namespace Lunixi;

/// <summary>
/// Bank transfer (<c>BANK_TRANSFER</c>): the buyer sends the money from their own bank to the merchant's bank account.
/// The money never passes through Lunixi; the incoming transaction is read from the merchant's connected bank account
/// and matched to the payment. The <c>bankTransfer</c> block carries the buyer's bank as <c>{ bankCode }</c> and never a
/// <c>providerCode</c>.
/// </summary>
public static class BankTransferValues
{
    /// <summary>The "Other banks" option of the <c>bankCode</c> field: the buyer pays by EFT/FAST from a bank not listed.</summary>
    public const string OtherBanksCode = "9999";
    /// <summary>Largest <see cref="BankTransferExceptionFilters.Limit"/>.</summary>
    public const int ExceptionLimitMax = 200;
}

/// <summary>Kind of a bank transfer exception (<c>items[].kind</c>).</summary>
public static class BankTransferExceptionKinds
{
    /// <summary><c>LATE</c>: arrived after the deadline.</summary>
    public const string Late = "LATE";
    /// <summary><c>UNDERPAID</c>: the deadline passed with less than expected; the payment waits for your decision.</summary>
    public const string Underpaid = "UNDERPAID";
    /// <summary><c>OVERPAID</c>: more than expected arrived; refund the excess.</summary>
    public const string Overpaid = "OVERPAID";
}

/// <summary>Status of a bank transfer exception (<c>items[].status</c> and the <see cref="BankTransferExceptionFilters.Status"/> filter).</summary>
public static class BankTransferExceptionStatuses
{
    /// <summary><c>PENDING_DECISION</c></summary>
    public const string PendingDecision = "PENDING_DECISION";
    /// <summary><c>ACCEPTED</c></summary>
    public const string Accepted = "ACCEPTED";
    /// <summary><c>REFUND_PENDING</c></summary>
    public const string RefundPending = "REFUND_PENDING";
    /// <summary><c>REFUNDED</c></summary>
    public const string Refunded = "REFUNDED";
    /// <summary>Every status, in the documented order.</summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { PendingDecision, Accepted, RefundPending, Refunded });
}

/// <summary>
/// Bank transfer webhook events (<c>payment.*</c> envelope with a <c>bankTransfer</c> payload). They do not change the
/// payment status: the outcome is still <c>payment.captured</c> / <c>payment.failed</c>. They are distinct from the Bank
/// Connect <c>bank_transfer.*</c> events.
/// </summary>
public static class PaymentBankTransferEvents
{
    /// <summary><c>payment.bank_transfer.partially_received</c></summary>
    public const string PartiallyReceived = "payment.bank_transfer.partially_received";
    /// <summary><c>payment.bank_transfer.underpaid</c></summary>
    public const string Underpaid = "payment.bank_transfer.underpaid";
    /// <summary><c>payment.bank_transfer.overpaid</c></summary>
    public const string Overpaid = "payment.bank_transfer.overpaid";
    /// <summary><c>payment.bank_transfer.late_arrival</c></summary>
    public const string LateArrival = "payment.bank_transfer.late_arrival";
}

/// <summary>Filters of <see cref="PaymentsClient.ListBankTransferExceptionsAsync"/>. Unset filters are not sent.</summary>
public sealed class BankTransferExceptionFilters
{
    /// <summary><c>TEST</c> or <c>LIVE</c>.</summary>
    public string? Environment { get; init; }
    /// <summary>A <see cref="BankTransferExceptionStatuses"/> value.</summary>
    public string? Status { get; init; }
    /// <summary>Page size from 1 to 200.</summary>
    public int? Limit { get; init; }

    internal string Query()
    {
        var pairs = new List<string>();
        if (Environment is not null)
        {
            if (Environment is not ("TEST" or "LIVE")) throw new LunixiConfigurationException("environment must be TEST or LIVE.");
            pairs.Add("environment=" + Environment);
        }
        if (Status is not null)
        {
            if (!BankTransferExceptionStatuses.All.Contains(Status))
                throw new LunixiConfigurationException("status must be one of " + string.Join(", ", BankTransferExceptionStatuses.All) + ".");
            pairs.Add("status=" + Status);
        }
        if (Limit is { } limit)
        {
            if (limit is < 1 or > BankTransferValues.ExceptionLimitMax)
                throw new LunixiConfigurationException($"limit must be an integer between 1 and {BankTransferValues.ExceptionLimitMax}.");
            pairs.Add("limit=" + limit.ToString(CultureInfo.InvariantCulture));
        }
        return pairs.Count == 0 ? "" : "?" + string.Join('&', pairs);
    }

    internal static string ExceptionId(string exceptionId)
    {
        if (exceptionId is null || exceptionId.Length != 36 || !Guid.TryParseExact(exceptionId, "D", out _))
            throw new LunixiConfigurationException("exceptionId must be the UUID of a bank transfer exception (items[].id).");
        return Uri.EscapeDataString(exceptionId);
    }
}

/// <summary>
/// The transfer instruction of a <c>BANK_TRANSFER</c> payment (<c>data.action.bankTransfer</c> of a <c>DISPLAY</c> action):
/// the merchant's bank account the buyer sends the money to. Show it to the buyer as it is; amounts are display values.
/// </summary>
public sealed class BankTransferInstruction
{
    /// <summary>Four-digit bank code of the merchant's bank the money is sent to.</summary>
    public string? BankCode { get; }
    /// <summary>The merchant bank's name.</summary>
    public string? BankName { get; }
    /// <summary>Account holder name; the buyer enters it as the recipient name.</summary>
    public string? HolderName { get; }
    /// <summary>IBAN without spaces.</summary>
    public string? Iban { get; }
    /// <summary>Amount to send in minor units.</summary>
    public string? AmountMinor { get; }
    /// <summary>Currency of the amount.</summary>
    public string? Currency { get; }
    /// <summary>Code the buyer writes in the transfer description; it matches the transfer to the payment.</summary>
    public string? PaymentCode { get; }
    /// <summary>When the instruction expires.</summary>
    public string? ExpiresAt { get; }
    /// <summary>True when the buyer chose "Other banks" (<see cref="BankTransferValues.OtherBanksCode"/>); the instruction is for EFT/FAST.</summary>
    public bool OtherBank { get; }

    private BankTransferInstruction(JsonElement value)
    {
        BankCode = LunixiClient.Property(value, "bankCode"); BankName = LunixiClient.Property(value, "bankName");
        HolderName = LunixiClient.Property(value, "holderName"); Iban = LunixiClient.Property(value, "iban");
        AmountMinor = LunixiClient.Property(value, "amountMinor"); Currency = LunixiClient.Property(value, "currency");
        PaymentCode = LunixiClient.Property(value, "paymentCode"); ExpiresAt = LunixiClient.Property(value, "expiresAt");
        OtherBank = value.TryGetProperty("otherBank", out var other) && other.ValueKind == JsonValueKind.True;
    }

    /// <summary>Reads the instruction of a payment response; null when there is no <c>DISPLAY</c> action with a transfer instruction.</summary>
    public static BankTransferInstruction? FromResponse(LunixiResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return FromData(response.Data);
    }

    /// <summary>Reads the instruction from a payment's <c>data</c> object; null when there is no <c>DISPLAY</c> action with a transfer instruction.</summary>
    public static BankTransferInstruction? FromData(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("action", out var action) || action.ValueKind != JsonValueKind.Object) return null;
        if (LunixiClient.Property(action, "type") != PaymentActionTypes.Display) return null;
        return action.TryGetProperty("bankTransfer", out var instruction) && instruction.ValueKind == JsonValueKind.Object
            ? new BankTransferInstruction(instruction) : null;
    }

    /// <summary>Returns no account or payment details.</summary>
    public override string ToString() => "BankTransferInstruction { Details = [REDACTED] }";
}
