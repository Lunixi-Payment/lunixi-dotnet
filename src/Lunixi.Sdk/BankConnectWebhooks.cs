using System.Text.Json;

namespace Lunixi;

/// <summary>
/// The <c>data</c> of a verified <c>bank_account.transaction.received</c> webhook (<see cref="BankConnectEvents.TransactionReceived"/>).
/// Values the bank did not send are null. Deduplicate on <see cref="EventId"/>: it is the same on every redelivery.
/// </summary>
public sealed class BankAccountTransactionReceivedData
{
    /// <summary>The event id (the envelope's <c>id</c>).</summary>
    public string EventId { get; }
    /// <summary><c>TEST</c> or <c>LIVE</c>, when the envelope carries it.</summary>
    public string? Environment { get; }
    /// <summary>The transaction id.</summary>
    public string TransactionId { get; }
    /// <summary>The account the transaction was read from.</summary>
    public string AccountId { get; }
    /// <summary>The account's connection.</summary>
    public string? ConnectionId { get; }
    /// <summary>The bank's EFT code.</summary>
    public string? BankEftCode { get; }
    /// <summary>The bank's name.</summary>
    public string? BankName { get; }
    /// <summary><see cref="BankTransactionDirections"/>: <c>CREDIT</c> or <c>DEBIT</c>.</summary>
    public string? Direction { get; }
    /// <summary>The amount in minor units, always positive.</summary>
    public string AmountMinor { get; }
    /// <summary>The currency.</summary>
    public string Currency { get; }
    /// <summary>The bank's booking time.</summary>
    public string? BookedAt { get; }
    /// <summary>The value date, or null.</summary>
    public string? ValueAt { get; }
    /// <summary>When Lunixi recorded the transaction.</summary>
    public string? ReceivedAt { get; }
    /// <summary>The <see cref="BankTransactionTypes"/> value.</summary>
    public string? CanonicalType { get; }
    /// <summary>The bank's description text, or null.</summary>
    public string? Description { get; }
    /// <summary>The counterparty's masked name, or null.</summary>
    public string? CounterpartyNameMasked { get; }
    /// <summary>The counterparty's masked IBAN, or null.</summary>
    public string? CounterpartyIbanMasked { get; }
    /// <summary>The bank's receipt number, or null.</summary>
    public string? ReceiptNo { get; }
    /// <summary>The whole <c>data</c> object.</summary>
    public JsonElement Data { get; }

    private BankAccountTransactionReceivedData(LunixiWebhook verified, JsonElement data)
    {
        EventId = verified.Id; Environment = WebhookData.Text(verified.Envelope, "environment"); Data = data;
        TransactionId = WebhookData.Required(data, "transactionId"); AccountId = WebhookData.Required(data, "accountId");
        ConnectionId = WebhookData.Text(data, "connectionId"); BankEftCode = WebhookData.Text(data, "bankEftCode"); BankName = WebhookData.Text(data, "bankName");
        Direction = WebhookData.Text(data, "direction"); AmountMinor = WebhookData.Money(data, "amountMinor", true)!;
        Currency = WebhookData.Required(data, "currency"); BookedAt = WebhookData.Text(data, "bookedAt"); ValueAt = WebhookData.Text(data, "valueAt");
        ReceivedAt = WebhookData.Text(data, "receivedAt"); CanonicalType = WebhookData.Text(data, "canonicalType");
        Description = WebhookData.Text(data, "description"); CounterpartyNameMasked = WebhookData.Text(data, "counterpartyNameMasked");
        CounterpartyIbanMasked = WebhookData.Text(data, "counterpartyIbanMasked"); ReceiptNo = WebhookData.Text(data, "receiptNo");
    }

    /// <summary>
    /// Reads a webhook already verified by <see cref="WebhookVerifier"/>. Throws <see cref="LunixiWebhookException"/> for
    /// another event type, a missing payload, a missing identifier or a non-canonical amount.
    /// </summary>
    public static BankAccountTransactionReceivedData FromWebhook(LunixiWebhook verified)
    {
        ArgumentNullException.ThrowIfNull(verified);
        if (verified.Type != BankConnectEvents.TransactionReceived) throw new LunixiWebhookException("Not a bank_account.transaction.received webhook event.");
        return new BankAccountTransactionReceivedData(verified, WebhookData.Payload(verified));
    }

    /// <summary>Returns no account or transaction data.</summary>
    public override string ToString() => "BankAccountTransactionReceivedData { Data = [REDACTED] }";
}

/// <summary>
/// The <c>data</c> of a verified <c>bank_transfer.*</c> webhook (<see cref="BankTransferEvents"/>). Fields without a value are
/// null. Deduplicate on <see cref="EventId"/>: one event per transfer and type, the same id on every redelivery.
/// </summary>
public sealed class BankTransferWebhookData
{
    /// <summary>The event id (the envelope's <c>id</c>).</summary>
    public string EventId { get; }
    /// <summary>The event type, for example <see cref="BankTransferEvents.Completed"/>.</summary>
    public string Type { get; }
    /// <summary><c>TEST</c> or <c>LIVE</c>, when the envelope carries it.</summary>
    public string? Environment { get; }
    /// <summary>The transfer id.</summary>
    public string TransferId { get; }
    /// <summary>The source account's connection.</summary>
    public string? ConnectionId { get; }
    /// <summary>The source account.</summary>
    public string? AccountId { get; }
    /// <summary>The bank's EFT code.</summary>
    public string? BankEftCode { get; }
    /// <summary>The bank's name.</summary>
    public string? BankName { get; }
    /// <summary>The <see cref="BankTransferStatuses"/> value.</summary>
    public string Status { get; }
    /// <summary>The <see cref="BankTransferRails"/> value, or null until the bank says.</summary>
    public string? Rail { get; }
    /// <summary>The amount in minor units.</summary>
    public string AmountMinor { get; }
    /// <summary>The currency.</summary>
    public string Currency { get; }
    /// <summary>The receiver's masked IBAN.</summary>
    public string? ReceiverIbanMasked { get; }
    /// <summary>The receiver's masked name.</summary>
    public string? ReceiverNameMasked { get; }
    /// <summary>The payment purpose code, or null.</summary>
    public string? PaymentPurpose { get; }
    /// <summary>The description, or null.</summary>
    public string? Description { get; }
    /// <summary>Why the transfer failed (<c>bank_connect.transfer.*</c>), or null.</summary>
    public string? FailureCode { get; }
    /// <summary>The <c>Idempotency-Key</c> the transfer was created with.</summary>
    public string? IdempotencyKey { get; }
    /// <summary>When the transfer was created.</summary>
    public string? CreatedAt { get; }
    /// <summary>When the bank completed it, or null.</summary>
    public string? CompletedAt { get; }
    /// <summary>When it failed, or null.</summary>
    public string? FailedAt { get; }
    /// <summary>When it was returned, or null.</summary>
    public string? ReturnedAt { get; }
    /// <summary>The whole <c>data</c> object.</summary>
    public JsonElement Data { get; }

    private BankTransferWebhookData(LunixiWebhook verified, JsonElement data)
    {
        EventId = verified.Id; Type = verified.Type; Environment = WebhookData.Text(verified.Envelope, "environment"); Data = data;
        TransferId = WebhookData.Required(data, "transferId"); ConnectionId = WebhookData.Text(data, "connectionId");
        AccountId = WebhookData.Text(data, "accountId"); BankEftCode = WebhookData.Text(data, "bankEftCode"); BankName = WebhookData.Text(data, "bankName");
        Status = WebhookData.Required(data, "status"); Rail = WebhookData.Text(data, "rail"); AmountMinor = WebhookData.Money(data, "amountMinor", true)!;
        Currency = WebhookData.Required(data, "currency"); ReceiverIbanMasked = WebhookData.Text(data, "receiverIbanMasked");
        ReceiverNameMasked = WebhookData.Text(data, "receiverNameMasked"); PaymentPurpose = WebhookData.Text(data, "paymentPurpose");
        Description = WebhookData.Text(data, "description"); FailureCode = WebhookData.Text(data, "failureCode");
        IdempotencyKey = WebhookData.Text(data, "idempotencyKey"); CreatedAt = WebhookData.Text(data, "createdAt");
        CompletedAt = WebhookData.Text(data, "completedAt"); FailedAt = WebhookData.Text(data, "failedAt"); ReturnedAt = WebhookData.Text(data, "returnedAt");
    }

    /// <summary>
    /// Reads a webhook already verified by <see cref="WebhookVerifier"/>. Throws <see cref="LunixiWebhookException"/> for an
    /// event that is not <c>bank_transfer.*</c> (a payment <c>payment.bank_transfer.*</c> event included), a missing payload,
    /// a missing identifier or a non-canonical amount.
    /// </summary>
    public static BankTransferWebhookData FromWebhook(LunixiWebhook verified)
    {
        ArgumentNullException.ThrowIfNull(verified);
        if (!verified.Type.StartsWith("bank_transfer.", StringComparison.Ordinal)) throw new LunixiWebhookException("Not a bank_transfer.* webhook event.");
        return new BankTransferWebhookData(verified, WebhookData.Payload(verified));
    }

    /// <summary>Returns only the event type.</summary>
    public override string ToString() => $"BankTransferWebhookData {{ Type = {Type}, Data = [REDACTED] }}";
}
