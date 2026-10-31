using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lunixi;

/// <summary>Gateway-compatible compact JSON: integer-index keys first, other properties in insertion order.</summary>
public static class CanonicalJson
{
    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        MaxDepth = 64,
    };
    /// <summary>Serializes an object without whitespace or HTML escaping. Rejects duplicate keys and unsafe integer precision.</summary>
    public static byte[] Serialize(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        try
        {
            var element = value is JsonElement json ? json : JsonSerializer.SerializeToElement(value, SerializerOptions);
            var result = new StringBuilder();
            Append(element, result, 0);
            var bytes = Encoding.UTF8.GetBytes(result.ToString());
            if (bytes.Length > 1024 * 1024) throw new LunixiConfigurationException("Request JSON exceeds one MiB.");
            return bytes;
        }
        catch (JsonException) { throw new LunixiConfigurationException("Request data must be valid JSON of at most 64 levels."); }
        catch (NotSupportedException) { throw new LunixiConfigurationException("Request data contains an unsupported JSON value."); }
        catch (InvalidOperationException) { throw new LunixiConfigurationException("Request data contains an invalid JSON or Unicode value."); }
    }
    internal static JsonElement Element(object value)
    {
        using var doc = JsonDocument.Parse(Serialize(value));
        return doc.RootElement.Clone();
    }
    private static void Append(JsonElement value, StringBuilder output, int depth)
    {
        if (depth > 64) throw new LunixiConfigurationException("Request JSON is too deeply nested.");
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var properties = value.EnumerateObject().ToArray();
                if (properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
                    throw new LunixiConfigurationException("Duplicate JSON object properties are not supported.");
                var ordered = properties.Where(p => Index(p.Name) is not null).OrderBy(p => Index(p.Name))
                    .Concat(properties.Where(p => Index(p.Name) is null));
                output.Append('{'); var firstProperty = true;
                foreach (var property in ordered)
                {
                    if (!firstProperty) output.Append(','); firstProperty = false;
                    String(property.Name, output); output.Append(':'); Append(property.Value, output, depth + 1);
                }
                output.Append('}'); break;
            case JsonValueKind.Array:
                output.Append('['); var firstItem = true;
                foreach (var item in value.EnumerateArray())
                { if (!firstItem) output.Append(','); firstItem = false; Append(item, output, depth + 1); }
                output.Append(']'); break;
            case JsonValueKind.String: String(value.GetString()!, output); break;
            case JsonValueKind.Number: output.Append(Number(value)); break;
            case JsonValueKind.True: output.Append("true"); break;
            case JsonValueKind.False: output.Append("false"); break;
            case JsonValueKind.Null: output.Append("null"); break;
            default: throw new LunixiConfigurationException("Undefined JSON values cannot be signed.");
        }
    }
    private static uint? Index(string key) => uint.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
        && index < uint.MaxValue && key == index.ToString(CultureInfo.InvariantCulture) ? index : null;
    private static void String(string value, StringBuilder output)
    {
        output.Append('"');
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '"': output.Append("\\\""); break;
                case '\\': output.Append("\\\\"); break;
                case '\b': output.Append("\\b"); break;
                case '\f': output.Append("\\f"); break;
                case '\n': output.Append("\\n"); break;
                case '\r': output.Append("\\r"); break;
                case '\t': output.Append("\\t"); break;
                default:
                    if (c < 0x20) output.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else if (char.IsHighSurrogate(c))
                    {
                        if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                            throw new LunixiConfigurationException("JSON strings must contain valid Unicode scalar values.");
                        output.Append(c).Append(value[++i]);
                    }
                    else if (char.IsLowSurrogate(c)) throw new LunixiConfigurationException("JSON strings must contain valid Unicode scalar values.");
                    else output.Append(c);
                    break;
            }
        }
        output.Append('"');
    }
    private static string Number(JsonElement element)
    {
        if (!element.TryGetDouble(out var value) || !double.IsFinite(value))
            throw new LunixiConfigurationException("JSON numbers must be finite.");
        if (Math.Truncate(value) == value && Math.Abs(value) > Validation.MaxSafeInteger)
            throw new LunixiConfigurationException("JSON integers outside JavaScript's safe range must be sent as decimal strings.");
        if (value == 0) return "0";
        var sign = value < 0 ? "-" : "";
        var text = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture).ToLowerInvariant();
        var parts = text.Split('e');
        var mantissa = parts[0];
        var exponent = parts.Length == 2 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
        var point = mantissa.IndexOf('.');
        var position = (point < 0 ? mantissa.Length : point) + exponent;
        var digits = mantissa.Replace(".", "", StringComparison.Ordinal);
        while (digits.Length > 1 && digits[0] == '0') { digits = digits[1..]; position--; }
        digits = digits.TrimEnd('0');
        if (Math.Abs(value) >= 1e-6 && Math.Abs(value) < 1e21)
        {
            if (position <= 0) return sign + "0." + new string('0', -position) + digits;
            if (position >= digits.Length) return sign + digits + new string('0', position - digits.Length);
            return sign + digits[..position] + "." + digits[position..];
        }
        var scientificExponent = position - 1;
        return sign + digits[0] + (digits.Length > 1 ? "." + digits[1..] : "") + "e" +
            (scientificExponent >= 0 ? "+" : "") + scientificExponent.ToString(CultureInfo.InvariantCulture);
    }
}
