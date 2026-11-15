using System.Globalization;
using System.Text.Json;

namespace Lunixi;

/// <summary>One row of a promissory-note schedule: a positive minor-unit amount and a <c>YYYY-MM-DD</c> due date.</summary>
/// <param name="Amount">The note amount in canonical positive minor units, for example <c>"5000"</c>.</param>
/// <param name="DueDate">The due date as a calendar date in <c>YYYY-MM-DD</c> form (Istanbul calendar).</param>
public sealed record PromissoryNoteScheduleItem(string Amount, string DueDate);

/// <summary>
/// Plan rules of a promissory-note payment (<c>PROMISSORY_NOTE</c>): either a schedule or a note count, never both.
/// The SDK checks what it can without the provider connection; future due dates and the note-count limits of your
/// provider connection are checked by the gateway.
/// </summary>
public static class PromissoryNotePlan
{
    /// <summary>Most rows of a schedule, most notes of a payment and most serial numbers of one cancellation.</summary>
    public const int MaxNotes = 24;

    /// <summary>
    /// The plan of a <c>promissoryNote</c> block: exactly one of <c>schedule</c> or <c>noteCount</c>; the schedule rows
    /// add up to <paramref name="amount"/>.
    /// </summary>
    internal static void Assert(JsonElement block, string amount)
    {
        if (block.ValueKind != JsonValueKind.Object) throw new LunixiConfigurationException("promissoryNote must be a JSON object.");
        var hasSchedule = block.TryGetProperty("schedule", out var schedule) && schedule.ValueKind != JsonValueKind.Null;
        var hasCount = block.TryGetProperty("noteCount", out var count) && count.ValueKind != JsonValueKind.Null;
        if (hasSchedule && hasCount) throw new LunixiConfigurationException("promissoryNote: send either schedule or noteCount, not both.");
        if (!hasSchedule && !hasCount) throw new LunixiConfigurationException("promissoryNote needs the plan: a schedule of { amount, dueDate } rows or a noteCount.");
        if (hasCount) { NoteCount(count); return; }
        Schedule(schedule, amount);
    }

    /// <summary>A schedule of 1–24 rows <c>{ amount, dueDate }</c> in non-decreasing due-date order adding up to the amount (when given).</summary>
    internal static void Schedule(JsonElement schedule, string? amount)
    {
        if (schedule.ValueKind != JsonValueKind.Array || schedule.GetArrayLength() == 0)
            throw new LunixiConfigurationException("promissoryNote.schedule must be a non-empty list of { amount, dueDate } rows.");
        if (schedule.GetArrayLength() > MaxNotes)
            throw new LunixiConfigurationException($"promissoryNote.schedule can have at most {MaxNotes} rows.");
        long total = 0; var limit = amount is null ? long.MaxValue : Money.Parse(amount, positive: true); string? previous = null;
        foreach (var row in schedule.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object || row.EnumerateObject().Any(p => p.Name is not ("amount" or "dueDate")))
                throw new LunixiConfigurationException("Each promissoryNote.schedule row must be an object { amount, dueDate }.");
            var rowAmount = RequestValidation.PositiveAmount(row, "amount");
            var dueDate = LunixiClient.Property(row, "dueDate");
            if (dueDate is null || !IsCalendarDate(dueDate))
                throw new LunixiConfigurationException("promissoryNote.schedule dueDate must be a calendar date in YYYY-MM-DD form.");
            if (previous is not null && string.CompareOrdinal(dueDate, previous) < 0)
                throw new LunixiConfigurationException("promissoryNote.schedule rows must be in due-date order.");
            previous = dueDate;
            if (total > limit - rowAmount) throw new LunixiConfigurationException("promissoryNote.schedule rows must add up to amount.");
            total += rowAmount;
        }
        if (amount is not null && total != limit) throw new LunixiConfigurationException("promissoryNote.schedule rows must add up to amount.");
    }

    /// <summary>A note count from 1 to 24, as a JSON number or its digit string. Returns the string sent to the gateway.</summary>
    internal static string NoteCount(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return NoteCount(number);
        if (value.ValueKind == JsonValueKind.String) return NoteCount(value.GetString()!);
        throw new LunixiConfigurationException($"promissoryNote.noteCount must be a number of notes from 1 to {MaxNotes}.");
    }

    internal static string NoteCount(int count)
    {
        if (count is < 1 or > MaxNotes) throw new LunixiConfigurationException($"promissoryNote.noteCount must be a number of notes from 1 to {MaxNotes}.");
        return count.ToString(CultureInfo.InvariantCulture);
    }

    internal static string NoteCount(string count)
    {
        if (count is null || count.Length is < 1 or > 2 || count[0] == '0' || count.Any(c => !char.IsAsciiDigit(c)))
            throw new LunixiConfigurationException($"promissoryNote.noteCount must be a number of notes from 1 to {MaxNotes}.");
        return NoteCount(int.Parse(count, CultureInfo.InvariantCulture));
    }

    /// <summary>Serial numbers to cancel: 1–24 values of letters, digits and <c>-</c>, 4–64 characters each.</summary>
    internal static string[] SerialNumbers(IReadOnlyList<string> values)
    {
        if (values.Count is < 1 or > MaxNotes)
            throw new LunixiConfigurationException($"serialNumbers must list 1 to {MaxNotes} note serial numbers; pass null to cancel every open note.");
        foreach (var value in values)
            if (value is null || value.Length is < 4 or > 64 || value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-')))
                throw new LunixiConfigurationException("serialNumbers contains an invalid serial number (letters, digits and -, 4 to 64 characters).");
        return values.ToArray();
    }

    internal static string DocumentLocale(string locale)
    {
        if (locale is not (PromissoryNoteDocumentLocales.Turkish or PromissoryNoteDocumentLocales.English))
            throw new LunixiConfigurationException("locale must be tr-TR or en-GB.");
        return locale;
    }

    private static bool IsCalendarDate(string value) => value.Length == 10 && value[4] == '-' && value[7] == '-' &&
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}

/// <summary>Status of one note (<c>notes[].status</c>).</summary>
public static class PromissoryNoteStatuses
{
    /// <summary><c>ISSUED</c></summary>
    public const string Issued = "ISSUED";
    /// <summary><c>PAID</c></summary>
    public const string Paid = "PAID";
    /// <summary><c>PROTESTED</c></summary>
    public const string Protested = "PROTESTED";
    /// <summary><c>CANCELLED</c></summary>
    public const string Cancelled = "CANCELLED";
}

/// <summary>Document type of the issued notes, from your provider connection settings.</summary>
public static class PromissoryNoteDocumentTypes
{
    /// <summary><c>PROMISSORY_NOTE</c></summary>
    public const string PromissoryNote = "PROMISSORY_NOTE";
    /// <summary><c>PAYMENT_ORDER</c></summary>
    public const string PaymentOrder = "PAYMENT_ORDER";
}

/// <summary>Per-note result of <see cref="PaymentsClient.CancelPromissoryNotesAsync"/> (<c>cancelResults[].result</c>).</summary>
public static class PromissoryNoteCancelResults
{
    /// <summary><c>CANCELLED</c></summary>
    public const string Cancelled = "CANCELLED";
    /// <summary><c>ALREADY_CLOSED</c>: the note was already cancelled.</summary>
    public const string AlreadyClosed = "ALREADY_CLOSED";
    /// <summary><c>NOT_CANCELLABLE</c>: the note was collected or protested.</summary>
    public const string NotCancellable = "NOT_CANCELLABLE";
    /// <summary><c>FAILED</c>: the note was not cancelled; <c>errorCode</c> tells whether to try again.</summary>
    public const string Failed = "FAILED";
}

/// <summary><c>cancelResults[].errorCode</c>: a Lunixi code; null for <c>CANCELLED</c> and <c>ALREADY_CLOSED</c>.</summary>
public static class PromissoryNoteCancelErrorCodes
{
    /// <summary><c>payment.promissory_note.not_cancellable</c></summary>
    public const string NotCancellable = "payment.promissory_note.not_cancellable";
    /// <summary><c>payment.promissory_note.provider_unavailable</c>: temporary; cancel again later.</summary>
    public const string ProviderUnavailable = "payment.promissory_note.provider_unavailable";
    /// <summary><c>payment.promissory_note.cancel_rejected</c>: permanent.</summary>
    public const string CancelRejected = "payment.promissory_note.cancel_rejected";
}

/// <summary>Language of the printable notes (<see cref="PaymentsClient.DownloadPromissoryNoteDocumentAsync"/>).</summary>
public static class PromissoryNoteDocumentLocales
{
    /// <summary><c>tr-TR</c></summary>
    public const string Turkish = "tr-TR";
    /// <summary><c>en-GB</c></summary>
    public const string English = "en-GB";
}

/// <summary>
/// Webhook events of a note (<c>payment.*</c> envelope; the payload adds <c>promissoryNote</c>). Statuses are polled
/// from the provider, so an event arrives some time after the due date.
/// </summary>
public static class PromissoryNoteEvents
{
    /// <summary><c>payment.promissory_note.paid</c></summary>
    public const string Paid = "payment.promissory_note.paid";
    /// <summary><c>payment.promissory_note.protested</c></summary>
    public const string Protested = "payment.promissory_note.protested";
    /// <summary><c>payment.promissory_note.cancelled</c></summary>
    public const string Cancelled = "payment.promissory_note.cancelled";
}
