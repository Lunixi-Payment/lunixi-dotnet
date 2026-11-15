using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lunixi;

/// <summary>JSON kind of one physical POS body field.</summary>
internal enum PhysicalPosKind { Text, Int, Money, Bool, Texts, RowVersion, Target, SaleItems, RefundItems }

/// <summary>One body field: its kind, whether it must be present, and whether an explicit null is sent (a clear).</summary>
internal readonly record struct PhysicalPosField(string Name, PhysicalPosKind Kind, bool Required = false, bool Nullable = false);

/// <summary>The closed request shapes and filter lists of the physical POS routes (the gateway rejects anything else with 400).</summary>
internal static class PhysicalPosShapes
{
    private static PhysicalPosField Text(string name) => new(name, PhysicalPosKind.Text);
    private static PhysicalPosField RequiredText(string name) => new(name, PhysicalPosKind.Text, Required: true);
    private static PhysicalPosField NullableText(string name) => new(name, PhysicalPosKind.Text, Nullable: true);
    private static PhysicalPosField RowVersion() => new("rowVersion", PhysicalPosKind.RowVersion, Required: true);

    internal static readonly PhysicalPosField[] Target = [Text("deviceId"), Text("vendorCode"), Text("serialNumber"), Text("externalRef")];
    internal static readonly PhysicalPosField[] SaleItem =
    [
        new("lineNo", PhysicalPosKind.Int, Required: true), RequiredText("name"), new("quantityMilli", PhysicalPosKind.Int, Required: true),
        RequiredText("unitCode"), new("unitPriceMinor", PhysicalPosKind.Money, Required: true), RequiredText("taxGroupCode"),
        new("lineTotalMinor", PhysicalPosKind.Money, Required: true), Text("barcode"),
    ];
    internal static readonly PhysicalPosField[] RefundItem =
        [new("lineNo", PhysicalPosKind.Int, Required: true), new("quantityMilli", PhysicalPosKind.Int, Required: true), new("amountMinor", PhysicalPosKind.Money)];
    internal static readonly PhysicalPosField[] CreatePayment =
    [
        RequiredText("operation"), new("target", PhysicalPosKind.Target, Required: true), new("amountMinor", PhysicalPosKind.Money, Required: true),
        RequiredText("currency"), new("installmentCount", PhysicalPosKind.Int, Nullable: true), NullableText("instrument"),
        NullableText("merchantReference"), new("allowDuplicateReference", PhysicalPosKind.Bool),
        new("startTimeoutSeconds", PhysicalPosKind.Int, Nullable: true), NullableText("terminalAcquirerId"),
        new("items", PhysicalPosKind.SaleItems), NullableText("interaction"), NullableText("terminalProviderId"),
    ];
    internal static readonly PhysicalPosField[] CreateReversal =
    [
        RequiredText("operation"), new("amountMinor", PhysicalPosKind.Money), RequiredText("currency"), NullableText("merchantReference"),
        new("startTimeoutSeconds", PhysicalPosKind.Int, Nullable: true), new("target", PhysicalPosKind.Target, Nullable: true),
        new("items", PhysicalPosKind.RefundItems),
    ];
    internal static readonly PhysicalPosField[] CreateStore =
        [RequiredText("code"), RequiredText("name"), Text("city"), Text("district"), Text("addressLine"), Text("timezone"), Text("externalRef")];
    internal static readonly PhysicalPosField[] UpdateStore =
    [
        RowVersion(), Text("name"), NullableText("city"), NullableText("district"), NullableText("addressLine"), Text("timezone"),
        NullableText("externalRef"),
    ];
    internal static readonly PhysicalPosField[] StatusChange = [RowVersion(), RequiredText("reasonCode")];
    internal static readonly PhysicalPosField[] CreateRegister = [RequiredText("code"), RequiredText("name")];
    internal static readonly PhysicalPosField[] UpdateRegister = [RowVersion(), Text("name")];
    internal static readonly PhysicalPosField[] CreateTerminal =
        [RequiredText("code"), RequiredText("displayName"), Text("vendorCode"), Text("virtualSerial"), Text("vendorTerminalRef")];
    internal static readonly PhysicalPosField[] UpdateTerminal = [RowVersion(), Text("displayName"), Text("vendorCode")];
    internal static readonly PhysicalPosField[] AddTerminalAcquirer =
    [
        RequiredText("vendorAcquirerRef"), RequiredText("merchantNumber"), RequiredText("terminalNumber"), Text("label"),
        new("currencies", PhysicalPosKind.Texts, Required: true),
    ];
    internal static readonly PhysicalPosField[] UpdateTerminalAcquirer =
        [RowVersion(), NullableText("label"), new("currencies", PhysicalPosKind.Texts), Text("status")];
    internal static readonly PhysicalPosField[] AddTerminalProvider =
        [RequiredText("vendorAccountId"), RequiredText("merchantCode"), RequiredText("terminalCode"), Text("productCode"), Text("label")];
    internal static readonly PhysicalPosField[] UpdateTerminalProvider = [RowVersion(), NullableText("label"), Text("status")];
    internal static readonly PhysicalPosField[] ChangeTerminalDevice = [RowVersion(), RequiredText("deviceId"), RequiredText("reasonCode")];
    internal static readonly PhysicalPosField[] CreateDevice =
    [
        RequiredText("vendorCode"), RequiredText("deviceModelId"), RequiredText("serialNumber"), Text("displayName"), Text("externalRef"),
        Text("storeId"), Text("registerId"), RequiredText("integrationChannel"), Text("channelVariant"), Text("appMode"),
    ];
    internal static readonly PhysicalPosField[] UpdateDevice =
    [
        RowVersion(), NullableText("displayName"), NullableText("externalRef"), Text("integrationChannel"), NullableText("channelVariant"),
        NullableText("appMode"),
    ];
    internal static readonly PhysicalPosField[] AssignDevice = [RowVersion(), RequiredText("storeId"), NullableText("registerId"), RequiredText("reasonCode")];
    internal static readonly PhysicalPosField[] ChangeDeviceLifecycle = [RowVersion(), RequiredText("target"), RequiredText("reasonCode")];
    internal static readonly PhysicalPosField[] RevokeDeviceKey = [RowVersion(), RequiredText("reasonCode")];
    internal static readonly PhysicalPosField[] RevertDeviceEnrollment = [RequiredText("reasonCode")];
    internal static readonly PhysicalPosField[] RevokeActivationCode = [RequiredText("reasonCode")];
    internal static readonly PhysicalPosField[] SetSettlementPolicy =
    [
        RequiredText("mode"), Text("scheduleTime"), Text("timeZone"), new("scheduleDays", PhysicalPosKind.Texts),
        new("retryWindowMinutes", PhysicalPosKind.Int), new("retryIntervalMinutes", PhysicalPosKind.Int), Text("offlineBehavior"),
        new("overdueAlertHours", PhysicalPosKind.Int), new("expectedRowVersion", PhysicalPosKind.RowVersion),
    ];

    internal static readonly IReadOnlyDictionary<string, PhysicalPosField[]> Management = new Dictionary<string, PhysicalPosField[]>(StringComparer.Ordinal)
    {
        ["createStore"] = CreateStore,
        ["updateStore"] = UpdateStore,
        ["statusChange"] = StatusChange,
        ["createRegister"] = CreateRegister,
        ["updateRegister"] = UpdateRegister,
        ["createTerminal"] = CreateTerminal,
        ["updateTerminal"] = UpdateTerminal,
        ["addTerminalAcquirer"] = AddTerminalAcquirer,
        ["updateTerminalAcquirer"] = UpdateTerminalAcquirer,
        ["addTerminalProvider"] = AddTerminalProvider,
        ["updateTerminalProvider"] = UpdateTerminalProvider,
        ["changeTerminalDevice"] = ChangeTerminalDevice,
        ["createDevice"] = CreateDevice,
        ["updateDevice"] = UpdateDevice,
        ["assignDevice"] = AssignDevice,
        ["changeDeviceLifecycle"] = ChangeDeviceLifecycle,
        ["revokeDeviceKey"] = RevokeDeviceKey,
        ["revertDeviceEnrollment"] = RevertDeviceEnrollment,
        ["revokeActivationCode"] = RevokeActivationCode,
        ["setSettlementPolicy"] = SetSettlementPolicy,
    };

    private static readonly string[] Page = ["limit", "cursor"];
    private static readonly string[] EntityPage = [.. Page, "includeTotal"];
    private static readonly string[] Report = ["dateFrom", "dateTo", "deviceId", "storeId"];

    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Filters = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        ["listPayments"] = [.. Page, "deviceId", "paymentStatus", "createdFrom", "createdTo", "merchantReference"],
        ["listTransactions"] =
        [
            .. Page, "deviceId", "storeId", "registerId", "paymentStatus", "createdFrom", "createdTo", "merchantReference",
            "amountFromMinor", "amountToMinor", "origin", "operation",
        ],
        ["listAmbiguousPayments"] = [.. Page, "deviceId", "paymentStatus"],
        ["getSalesSummary"] = [.. Report, "groupBy"],
        ["getTaxSummary"] = [.. Report],
        ["listSettlements"] = [.. Page, "deviceId", "lineStatus", "createdFrom", "createdTo"],
        ["listSettlementRuns"] = [.. EntityPage, "deviceId", "source", "runStatus", "createdFrom", "createdTo"],
        ["listDeviceModels"] = ["vendorCode", "deviceType", "status"],
        ["listStores"] = [.. EntityPage, "status", "q"],
        ["listRegisters"] = [.. EntityPage, "status"],
        ["listTerminals"] = [.. EntityPage, "status", "q"],
        ["listTerminalAcquirers"] = [.. EntityPage],
        ["listTerminalProviders"] = [.. EntityPage],
        ["listDevices"] =
        [
            .. EntityPage, "storeId", "registerId", "vendorCode", "deviceModelId", "deviceType", "lifecycleStatus", "ownershipStatus",
            "integrationChannel", "q",
        ],
        ["history"] = [.. Page],
    };

    internal static IReadOnlyList<string> Names(PhysicalPosField[] shape) => shape.Select(field => field.Name).ToArray();
}

/// <summary>
/// Request checks shared by the physical POS clients: card data (names and card-number values), closed field lists, JSON
/// types, filters, path segments and <c>Idempotency-Key</c>. Only shape is checked; value rules (operation sets, ranges,
/// totals, reason codes) belong to the gateway.
/// </summary>
internal static class PhysicalPosRequest
{
    internal const string BasePath = "/api/v1/physical-pos";
    private static readonly string[] FreeTextFilters = ["merchantReference", "q"];

    /// <summary>
    /// Builds a body from its shape: card-data names first (<see cref="PhysicalPosCardDataRejectedException"/>), then unknown
    /// fields, required fields and JSON types (<see cref="LunixiConfigurationException"/>), then card numbers in the scanned
    /// free-text values. Fields are sent in shape order. <c>null</c> is sent only for a nullable field (an explicit clear) and
    /// left out otherwise; an empty optional string is left out. Nothing is sent when a check fails.
    /// </summary>
    internal static JsonObject Body(object? request, PhysicalPosField[] shape, string label)
    {
        if (request is null) throw new LunixiConfigurationException($"{label} must be an object.");
        var element = request is JsonElement json ? json : CanonicalJson.Element(request);
        var cardField = PhysicalPosCardData.FindCardDataField(element);
        if (cardField is not null) throw new PhysicalPosCardDataRejectedException(cardField);
        if (element.ValueKind != JsonValueKind.Object) throw new LunixiConfigurationException($"{label} must be an object.");
        var body = Fields(element, shape, label, "");
        RejectCardNumbers(body);
        return body;
    }

    private static JsonObject Fields(JsonElement source, PhysicalPosField[] shape, string label, string path)
    {
        var unknown = source.EnumerateObject().Select(p => p.Name).Where(name => shape.All(field => field.Name != name)).ToArray();
        if (unknown.Length > 0)
            throw new LunixiConfigurationException(
                $"Unknown {label} field(s): {string.Join(", ", unknown.Select(name => Join(path, name)))}. Allowed: {string.Join(", ", shape.Select(f => f.Name))}. The gateway rejects unknown fields with 400.");
        var output = new JsonObject();
        foreach (var field in shape)
        {
            var fieldPath = Join(path, field.Name);
            if (!source.TryGetProperty(field.Name, out var value))
            {
                if (field.Required) throw new LunixiConfigurationException($"'{fieldPath}' is required.");
                continue;
            }
            if (Value(field, value, fieldPath, out var checkedValue)) output[field.Name] = checkedValue;
        }
        return output;
    }

    /// <summary>Checks one value; false leaves the field out.</summary>
    private static bool Value(PhysicalPosField field, JsonElement value, string path, out JsonNode? result)
    {
        result = null;
        if (field.Kind == PhysicalPosKind.Money) { result = Money(value, path); return true; }
        if (value.ValueKind == JsonValueKind.Null)
        {
            if (field.Nullable) return true;
            if (field.Required) throw new LunixiConfigurationException($"'{path}' is required.");
            return false;
        }
        if (!field.Required && value.ValueKind == JsonValueKind.String && value.GetString()!.Length == 0) return false;
        switch (field.Kind)
        {
            case PhysicalPosKind.Text:
                if (value.ValueKind != JsonValueKind.String || value.GetString()!.Length == 0)
                    throw new LunixiConfigurationException($"'{path}' must be a non-empty string.");
                result = value.GetString();
                return true;
            case PhysicalPosKind.Int:
                result = Integer(value, path);
                return true;
            case PhysicalPosKind.RowVersion:
                result = RowVersion(value, path);
                return true;
            case PhysicalPosKind.Bool:
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new LunixiConfigurationException($"'{path}' must be a boolean.");
                result = value.GetBoolean();
                return true;
            case PhysicalPosKind.Texts:
                if (value.ValueKind != JsonValueKind.Array || value.EnumerateArray().Any(e => e.ValueKind != JsonValueKind.String || e.GetString()!.Length == 0))
                    throw new LunixiConfigurationException($"'{path}' must be an array of non-empty strings.");
                result = new JsonArray(value.EnumerateArray().Select(e => (JsonNode?)JsonValue.Create(e.GetString())).ToArray());
                return true;
            case PhysicalPosKind.Target:
                if (value.ValueKind != JsonValueKind.Object) throw new LunixiConfigurationException($"'{path}' must be an object.");
                result = Fields(value, PhysicalPosShapes.Target, path, path);
                return true;
            case PhysicalPosKind.SaleItems:
                result = Items(value, PhysicalPosShapes.SaleItem, path);
                return true;
            case PhysicalPosKind.RefundItems:
                result = Items(value, PhysicalPosShapes.RefundItem, path);
                return true;
            default:
                throw new LunixiConfigurationException($"Unsupported field kind for '{path}'.");
        }
    }

    private static JsonArray Items(JsonElement value, PhysicalPosField[] shape, string path)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
            throw new LunixiConfigurationException($"'{path}' must be a non-empty array; leave the field out for a request without items.");
        var items = new JsonArray();
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            var itemPath = path + "[" + index++ + "]";
            if (item.ValueKind != JsonValueKind.Object) throw new LunixiConfigurationException($"'{itemPath}' must be an object.");
            var checkedItem = Fields(item, shape, itemPath, itemPath);
            foreach (var field in shape.Where(f => f.Required && !checkedItem.ContainsKey(f.Name)))
                throw new LunixiConfigurationException($"'{itemPath}.{field.Name}' is required.");
            items.Add(checkedItem);
        }
        return items;
    }

    /// <summary>A positive canonical minor-unit string in the signed 64-bit range.</summary>
    private static string Money(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.String) throw MoneyError(path);
        var text = value.GetString()!;
        try { Lunixi.Money.Parse(text, positive: true); }
        catch (LunixiConfigurationException) { throw MoneyError(path); }
        return text;
    }

    private static LunixiConfigurationException MoneyError(string path)
        => new($"'{path}' must be a canonical string in minor units (for example \"4500\").");

    private static long Integer(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number) || Math.Abs(number) > Validation.MaxSafeInteger)
            throw new LunixiConfigurationException($"'{path}' must be an integer (a JSON number, not a string).");
        return number;
    }

    private static long RowVersion(JsonElement value, string path)
    {
        var number = Integer(value, path);
        return CheckRowVersion(number, path);
    }

    internal static long CheckRowVersion(long number, string path)
    {
        if (number < 1 || number > PhysicalPosValues.RowVersionMax)
            throw new LunixiConfigurationException($"'{path}' must be between 1 and {PhysicalPosValues.RowVersionMax}: send the rowVersion you last read.");
        return number;
    }

    /// <summary>Card numbers in the free-text values the gateway scans; only the field path is reported.</summary>
    private static void RejectCardNumbers(JsonObject body)
    {
        if (body["merchantReference"] is JsonValue reference && PhysicalPosCardData.ContainsPanLikeValue(reference.GetValue<string>()))
            throw new PhysicalPosCardDataRejectedException("merchantReference");
        if (body["items"] is not JsonArray items) return;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is not JsonObject item) continue;
            if (item["name"] is JsonValue name && PhysicalPosCardData.ContainsPanLikeValue(name.GetValue<string>(), itemText: true))
                throw new PhysicalPosCardDataRejectedException($"items[{i}].name");
            if (item["taxGroupCode"] is JsonValue taxGroup && PhysicalPosCardData.ContainsPanLikeValue(taxGroup.GetValue<string>()))
                throw new PhysicalPosCardDataRejectedException($"items[{i}].taxGroupCode");
        }
    }

    private static string Join(string path, string name) => path.Length == 0 ? name : path + "." + name;

    /// <summary>
    /// The query string of a list or report route (with its leading <c>?</c>, or empty). Card-data filter names, unknown
    /// filters and wrong value types are refused before sending; null and empty values are left out. Values: text as given,
    /// <c>limit</c> an integer 1–100, <c>includeTotal</c> a boolean, <c>amountFromMinor</c> / <c>amountToMinor</c> positive
    /// canonical minor-unit text, <c>dateFrom</c> / <c>dateTo</c> a <see cref="DateOnly"/> or a date-time (its own calendar day,
    /// <c>YYYY-MM-DD</c>), any other date-time filter an ISO-8601 UTC instant with milliseconds.
    /// </summary>
    internal static string Query(IReadOnlyDictionary<string, object?>? filters, string route)
    {
        if (filters is null || filters.Count == 0) return "";
        foreach (var key in filters.Keys)
        {
            if (key is null) throw new LunixiConfigurationException("Physical POS filter names must not be null.");
            if (PhysicalPosCardData.FindCardDataField(new JsonObject { [key] = null }, "(query)") is { } field)
                throw new PhysicalPosCardDataRejectedException(field);
        }
        var allowed = PhysicalPosShapes.Filters[route];
        var unknown = filters.Keys.Where(key => !allowed.Contains(key)).ToArray();
        if (unknown.Length > 0)
            throw new LunixiConfigurationException(
                $"Unknown physical POS filter(s): {string.Join(", ", unknown)}. Allowed: {string.Join(", ", allowed)}. The gateway rejects unknown query fields with 400.");
        var pairs = new List<string>();
        foreach (var key in allowed)
        {
            if (!filters.TryGetValue(key, out var value)) continue;
            string text;
            if (key is "amountFromMinor" or "amountToMinor")
            {
                if (value is not string amount) throw MoneyError(key);
                try { Lunixi.Money.Parse(amount, positive: true); }
                catch (LunixiConfigurationException) { throw MoneyError(key); }
                text = amount;
            }
            else
            {
                if (value is null || value is "") continue;
                text = FilterValue(key, value);
                if (FreeTextFilters.Contains(key) && PhysicalPosCardData.ContainsPanLikeValue(text))
                    throw new PhysicalPosCardDataRejectedException(key);
            }
            pairs.Add(Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(text));
        }
        return pairs.Count == 0 ? "" : "?" + string.Join('&', pairs);
    }

    private static string FilterValue(string key, object value)
    {
        if (key == "limit")
        {
            long? limit = value switch { int n => n, long n => n, short n => n, byte n => n, _ => null };
            if (limit is not { } number || number < 1 || number > PhysicalPosValues.MaxLimit)
                throw new LunixiConfigurationException($"'limit' must be an integer between 1 and {PhysicalPosValues.MaxLimit}.");
            return number.ToString(CultureInfo.InvariantCulture);
        }
        if (key == "includeTotal")
        {
            if (value is not bool flag) throw new LunixiConfigurationException("'includeTotal' must be a boolean.");
            return flag ? "true" : "false";
        }
        var day = key is "dateFrom" or "dateTo";
        return value switch
        {
            string text => text,
            DateOnly date when day => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset time => day ? time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : Instant(time),
            DateTime time => day ? time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : Instant(new DateTimeOffset(time)),
            _ => throw new LunixiConfigurationException($"'{key}' must be a string."),
        };
    }

    private static string Instant(DateTimeOffset time)
        => time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>A path segment: non-empty, not <c>.</c> or <c>..</c>, URL-encoded.</summary>
    internal static string PathId(string? value, string field)
    {
        if (string.IsNullOrEmpty(value)) throw new LunixiConfigurationException($"'{field}' must be a non-empty string.");
        if (value is "." or "..") throw new LunixiConfigurationException($"'{field}' is not a valid identifier.");
        return Uri.EscapeDataString(value);
    }

    /// <summary>A new <c>Idempotency-Key</c> (a random UUID).</summary>
    internal static string NewIdempotencyKey() => Guid.NewGuid().ToString();

    /// <summary>8–200 printable ASCII characters without spaces, checked exactly as given (never trimmed).</summary>
    internal static string CheckIdempotencyKey(string key)
    {
        if (key.Length is < PhysicalPosValues.IdempotencyKeyMinLength or > PhysicalPosValues.IdempotencyKeyMaxLength || key.Any(c => c < 0x21 || c > 0x7e))
            throw new LunixiConfigurationException(
                "The Idempotency-Key must be 8–200 printable ASCII characters without spaces; it is sent exactly as given. Use a new UUID for every operation and the same key to retry it.");
        return key;
    }

    /// <summary>The caller's key (checked) or a new UUID for null.</summary>
    internal static string IdempotencyKeyOrNew(string? key) => key is null ? NewIdempotencyKey() : CheckIdempotencyKey(key);

    /// <summary>Payment start and reversal: the caller must pass the key; nothing is generated.</summary>
    internal static string RequiredIdempotencyKey(string? key, string operation)
    {
        if (key is null)
            throw new LunixiConfigurationException(
                $"A stable Idempotency-Key is required for {operation}. Create one with PhysicalPosClient.NewIdempotencyKey(), store it with your order, then call; when the outcome is unknown call again with the SAME key.");
        return CheckIdempotencyKey(key);
    }

    /// <summary><c>?expectedRowVersion=</c> of a policy clear, or empty when null.</summary>
    internal static string ExpectedRowVersionQuery(long? expectedRowVersion)
        => expectedRowVersion is { } version
            ? "?expectedRowVersion=" + CheckRowVersion(version, "expectedRowVersion").ToString(CultureInfo.InvariantCulture)
            : "";
}

/// <summary>Strict physical POS response money: declared minor-unit fields must be canonical strings; counts and metadata stay untouched.</summary>
internal static class PhysicalPosMoney
{
    private static readonly HashSet<string> Fields =
    [
        "amountFromMinor", "amountMinor", "amountToMinor", "approvedAmountMinor", "baseMinor", "capturedMinor", "lineTotalMinor",
        "netBaseMinor", "netMinor", "netTaxMinor", "refundBaseMinor", "refundMinor", "refundTaxMinor", "refundedAmountMinor",
        "refundedMinor", "reservedAmountMinor", "reservedMinor", "reversalReservedMinor", "reversedMinor", "saleBaseMinor",
        "saleMinor", "saleTaxMinor", "unitAmountMinor", "unitPriceMinor", "voidBaseMinor", "voidMinor", "voidTaxMinor",
    ];
    private static readonly HashSet<string> Containers =
    [
        "data", "payment", "payments", "items", "refundItems", "taxes", "rows", "transaction", "transactions", "root", "children",
        "itemSummary", "amountTotals", "reported", "system", "difference", "totals", "lines",
    ];
    private static readonly HashSet<string> Nullable = ["approvedAmountMinor", "unitAmountMinor"];

    /// <summary>True when every declared money field is canonical (signed inside <c>difference</c> and for <c>net…</c> fields).</summary>
    internal static bool IsValid(JsonElement value, bool difference = false)
    {
        if (value.ValueKind == JsonValueKind.Array) return value.EnumerateArray().All(item => IsValid(item, difference));
        if (value.ValueKind != JsonValueKind.Object) return true;
        foreach (var property in value.EnumerateObject())
        {
            if (Fields.Contains(property.Name))
            {
                if (property.Value.ValueKind == JsonValueKind.Null && Nullable.Contains(property.Name)) continue;
                if (property.Value.ValueKind != JsonValueKind.String) return false;
                try { Money.Parse(property.Value.GetString()!, signed: difference || property.Name.StartsWith("net", StringComparison.Ordinal)); }
                catch (LunixiConfigurationException) { return false; }
            }
            else if (Containers.Contains(property.Name) && !IsValid(property.Value, difference || property.Name == "difference")) return false;
        }
        return true;
    }
}
