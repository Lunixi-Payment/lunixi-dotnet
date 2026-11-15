using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lunixi;

/// <summary>
/// The card-data rules the physical POS clients apply before sending (the same rules as the gateway). A field name that
/// looks like card data (<c>pan</c>, <c>cardNumber</c>, <c>cvv</c>, <c>track2Data</c>, <c>expiryDate</c> …) or a stand-alone,
/// Luhn-valid card number in a scanned free-text value raises <see cref="PhysicalPosCardDataRejectedException"/>. Use these
/// helpers to check your own data first; values are never echoed.
/// </summary>
public static class PhysicalPosCardData
{
    /// <summary>Card-number terms; a masked variant (<c>maskedPan</c>) is not card data.</summary>
    public static IReadOnlyList<string> NumberTerms { get; } =
        ["pan", "pannumber", "cardpan", "cardnumber", "cardno", "cardnum", "cardnr", "ccnumber", "ccnum", "primaryaccountnumber"];
    /// <summary>Security code, track, PIN and expiry terms; masked or not, they are card data.</summary>
    public static IReadOnlyList<string> SecretTerms { get; } =
    [
        "cvv", "cvv2", "cvc", "cvc2", "cvn", "cvn2", "cav2", "cvvcode", "cvccode", "cardcode", "securitycode",
        "cardverificationcode", "cardverificationvalue", "track1", "track2", "track3", "tracki", "trackii", "trackiii",
        "trackdata", "magstripe", "magneticstripe", "pinblock", "pincode", "pinnumber", "pindata", "cardpin",
        "cardholderpin", "encryptedpin", "expdate", "expmonth", "expyear", "expmmyy", "expirydate", "expirymonth",
        "expiryyear", "expirymmyy", "expirationdate", "expirationmonth", "expirationyear", "expiremonth", "expireyear",
        "cardexpiry", "cardexpiration",
    ];
    /// <summary>Terms that make a key card data when it ends with them (<c>customerCardNumber</c>).</summary>
    public static IReadOnlyList<string> SuffixTerms { get; } =
    [
        "cardnumber", "pannumber", "primaryaccountnumber", "cvv", "cvv2", "cvc", "cvc2", "cvvcode", "securitycode",
        "track1", "track2", "trackdata", "pinblock", "expmonth", "expyear", "expirydate", "expirymonth", "expiryyear",
        "expirationdate", "cardexpiry",
    ];
    /// <summary>Terms that are card data only as the whole key (<c>track</c>, not <c>trackUrl</c>).</summary>
    public static IReadOnlyList<string> WholeKeyTerms { get; } = ["track", "expiry", "expiration", "pin", "cid", "csc"];
    /// <summary>Tokens that mark a card number as masked.</summary>
    public static IReadOnlyList<string> MaskTokens { get; } = ["masked", "mask"];

    private static readonly HashSet<string> Terms = [.. NumberTerms, .. SecretTerms];
    private static readonly HashSet<string> Numbers = [.. NumberTerms];
    private static readonly HashSet<string> WholeKeys = [.. WholeKeyTerms];
    private static readonly HashSet<string> Masks = [.. MaskTokens];
    private static readonly Regex DigitRunKey = new("[0-9](?:[ -]?[0-9]){12,}", RegexOptions.CultureInvariant);
    private static readonly Regex IdentifierKey = new(@"\A[A-Za-z_$][A-Za-z0-9_$]*\z", RegexOptions.CultureInvariant);
    private static readonly Regex PanCandidate = new(
        @"(?<![\p{L}\p{N}])(?<![\p{L}\p{N}]-)[0-9](?:[ -]?[0-9]){12,18}(?![\p{L}\p{N}])(?!-[\p{L}\p{N}])", RegexOptions.CultureInvariant);
    private static readonly Regex Gtin = new(@"\A(?:[0-9]{8}|[0-9]{12,14})\z", RegexOptions.CultureInvariant);

    /// <summary>True when a field name is card data (<c>pan</c>, <c>cardNumber</c>, <c>cvv</c>, <c>track2Data</c>, <c>expiryDate</c> …).</summary>
    public static bool IsCardDataKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var tokens = KeyTokens(key);
        if (tokens.Count == 0) return false;
        if (WholeKeys.Contains(string.Concat(tokens))) return true;
        var masked = tokens.Any(Masks.Contains);
        for (var start = 0; start < tokens.Count; start++)
        {
            var joined = "";
            for (var end = start; end < tokens.Count; end++)
            {
                joined += tokens[end];
                if (Terms.Contains(joined) && !(masked && Numbers.Contains(joined))) return true;
                foreach (var term in SuffixTerms)
                    if (joined.EndsWith(term, StringComparison.Ordinal) && !(masked && Numbers.Contains(term))) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The path of the first card-data field name in a body or filter object (<c>items[1].cvv</c>), or null. A key holding a
    /// run of 13 or more digits is reported at its parent path, so the number is never echoed. Values are not read.
    /// </summary>
    public static string? FindCardDataField(object? value, string root = "(body)")
    {
        if (value is null) return null;
        var element = value is JsonElement json ? json : CanonicalJson.Element(value);
        return Find(element, "", root);
    }

    /// <summary>
    /// True when the text holds a stand-alone, Luhn-valid 13–19 digit number (plain, space or dash separated). With
    /// <paramref name="itemText"/> (item names only) an unseparated 13 or 14 digit number with a valid GTIN check digit is a
    /// barcode, not a card number.
    /// </summary>
    public static bool ContainsPanLikeValue(string? text, bool itemText = false)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (Match match in PanCandidate.Matches(text))
        {
            var digits = match.Value.Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
            if (digits.Length is < 13 or > 19) continue;
            if (itemText && digits == match.Value && digits.Length <= 14 && GtinValid(digits)) continue;
            if (LuhnValid(digits)) return true;
        }
        return false;
    }

    private static List<string> KeyTokens(string key)
    {
        var spaced = Regex.Replace(key, "([a-z0-9])([A-Z])", "$1 $2", RegexOptions.CultureInvariant);
        spaced = Regex.Replace(spaced, "([A-Z]+)([A-Z][a-z])", "$1 $2", RegexOptions.CultureInvariant);
        spaced = Regex.Replace(spaced, "([A-Za-z])([0-9])", "$1 $2", RegexOptions.CultureInvariant);
        spaced = Regex.Replace(spaced, "([0-9])([A-Za-z])", "$1 $2", RegexOptions.CultureInvariant);
        return Regex.Split(spaced.ToLowerInvariant(), "[^a-z0-9]+", RegexOptions.CultureInvariant).Where(token => token.Length > 0).ToList();
    }

    private static string? Find(JsonElement node, string path, string root)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in node.EnumerateArray())
            {
                var found = Find(item, path + "[" + index++ + "]", root);
                if (found is not null) return found;
            }
            return null;
        }
        if (node.ValueKind != JsonValueKind.Object) return null;
        foreach (var property in node.EnumerateObject())
        {
            if (DigitRunKey.IsMatch(property.Name)) return path.Length == 0 ? root : path;
            if (IsCardDataKey(property.Name)) return Child(path, property.Name);
        }
        foreach (var property in node.EnumerateObject())
        {
            var found = Find(property.Value, Child(path, property.Name), root);
            if (found is not null) return found;
        }
        return null;
    }

    private static string Child(string path, string key)
    {
        if (!IdentifierKey.IsMatch(key)) return path + "[?]";
        return path.Length == 0 ? key : path + "." + key;
    }

    private static bool LuhnValid(string digits)
    {
        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            var digit = digits[digits.Length - 1 - i] - '0';
            if (i % 2 == 1) { digit *= 2; if (digit > 9) digit -= 9; }
            sum += digit;
        }
        return sum % 10 == 0;
    }

    private static bool GtinValid(string digits)
    {
        if (!Gtin.IsMatch(digits)) return false;
        var sum = 0; var weight = 3;
        for (var i = digits.Length - 2; i >= 0; i--)
        {
            sum += (digits[i] - '0') * weight;
            weight = weight == 3 ? 1 : 3;
        }
        return (10 - sum % 10) % 10 == digits[^1] - '0';
    }
}
