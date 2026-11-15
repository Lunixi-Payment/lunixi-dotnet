using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lunixi;

/// <summary>
/// Physical POS management: stores, registers, logical terminals with their bank rows, payment-provider rows and device
/// links, devices with their identity keys, the end-of-day policy, the catalog and the overview (<c>/api/v1/physical-pos</c>).
/// Reads need <c>physical_pos:view</c>, writes <c>physical_pos:manage</c>, device identity writes (key revocation, enrollment
/// revert, activation-code revocation) <c>physical_pos:identity</c>; a TEST key can only read. Activation codes are issued in
/// the panel only.
/// <para>
/// Updates and status changes take the <c>rowVersion</c> you last read (409 <c>physical_pos.version_conflict</c> when the record
/// changed). Reason codes are closed sets checked by the gateway (400 <c>physical_pos.input.reason_code_invalid</c>). Routes that
/// require an <c>Idempotency-Key</c> get a new UUID when you pass null; a key you pass is sent exactly as given and comes back as
/// <see cref="PhysicalPosWriteResult.IdempotencyKey"/>. A key belongs to one operation: reusing it for another body or route
/// is 409 <c>physical_pos.idempotency_mismatch</c>. Bodies and filters follow <see cref="PhysicalPosClient"/>; list pages carry
/// <see cref="PhysicalPosPage.TotalCount"/> only with <c>includeTotal: true</c>.
/// </para>
/// </summary>
public sealed class PhysicalPosManagementClient
{
    private readonly LunixiClient client;
    internal PhysicalPosManagementClient(LunixiClient client) => this.client = client;

    /// <summary>Store, register, terminal and device counts (<c>GET /overview</c>).</summary>
    public async Task<JsonElement> GetOverviewAsync(CancellationToken cancellationToken = default)
        => PhysicalPosTransport.Record(await Read("/overview", "", cancellationToken).ConfigureAwait(false), "overview");

    // Catalog

    /// <summary>Device vendors (<c>GET /catalog/vendors</c>).</summary>
    public async Task<JsonElement> ListVendorsAsync(CancellationToken cancellationToken = default)
        => (await Read("/catalog/vendors", "", cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>Device models; filters <c>vendorCode</c>, <c>deviceType</c>, <c>status</c> (<c>GET /catalog/device-models</c>).</summary>
    public async Task<JsonElement> ListDeviceModelsAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => (await Read("/catalog/device-models", PhysicalPosRequest.Query(filters, "listDeviceModels"), cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>Banks a terminal row can belong to (<c>GET /catalog/acquirers</c>).</summary>
    public async Task<JsonElement> ListAcquirersAsync(CancellationToken cancellationToken = default)
        => (await Read("/catalog/acquirers", "", cancellationToken).ConfigureAwait(false)).Data;

    // Stores

    /// <summary>Stores (<c>GET /stores</c>); filters <c>limit</c>, <c>cursor</c>, <c>includeTotal</c>, <c>status</c>, <c>q</c>.</summary>
    public Task<PhysicalPosPage> ListStoresAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => Page("/stores", filters, "listStores", cancellationToken);

    /// <summary>Every store of <see cref="ListStoresAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateStoresAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => PhysicalPosTransport.IterateAsync(ListStoresAsync, filters, cancellationToken);

    /// <summary>Creates a store: <c>{ code, name, city?, district?, addressLine?, timezone?, externalRef? }</c>; entity <c>store</c>, 201 new / 200 replay.</summary>
    public Task<PhysicalPosWriteResult> CreateStoreAsync(object request, string? idempotencyKey = null, CancellationToken cancellationToken = default)
        => KeyedWrite("/stores", Body(request, PhysicalPosShapes.CreateStore, "createStore"), idempotencyKey, "store", cancellationToken);

    /// <summary><c>{ store, registerSummary: { activeCount, archivedCount }, assignedDeviceCount }</c> (<c>GET /stores/{storeId}</c>).</summary>
    public async Task<JsonElement> GetStoreAsync(string storeId, CancellationToken cancellationToken = default)
        => (await Read(StorePath(storeId), "", cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>Changes a store: <c>{ rowVersion, name?, city?, district?, addressLine?, timezone?, externalRef? }</c>; null clears <c>city</c>, <c>district</c>, <c>addressLine</c>, <c>externalRef</c>.</summary>
    public Task<JsonElement> UpdateStoreAsync(string storeId, object request, CancellationToken cancellationToken = default)
        => RecordWrite(HttpMethod.Patch, StorePath(storeId), Body(request, PhysicalPosShapes.UpdateStore, "updateStore"), "store", cancellationToken);

    /// <summary>Archives a store and its active registers: <c>{ rowVersion, reasonCode }</c>; returns <c>{ store, archivedRegisterCount }</c>.</summary>
    public async Task<JsonElement> ArchiveStoreAsync(string storeId, object request, CancellationToken cancellationToken = default)
    {
        var path = StorePath(storeId) + "/archive";
        return (await Post(path, StatusChange(request, "archiveStore"), cancellationToken).ConfigureAwait(false)).Data;
    }

    /// <summary>Restores an archived store: <c>{ rowVersion, reasonCode }</c>.</summary>
    public Task<JsonElement> RestoreStoreAsync(string storeId, object request, CancellationToken cancellationToken = default)
        => RecordWrite(HttpMethod.Post, StorePath(storeId) + "/restore", StatusChange(request, "restoreStore"), "store", cancellationToken);

    /// <summary>The store's change history (<c>GET /stores/{storeId}/audit</c>; filters <c>limit</c>, <c>cursor</c>).</summary>
    public Task<PhysicalPosPage> ListStoreAuditAsync(string storeId, IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => Page(StorePath(storeId) + "/audit", filters, "history", cancellationToken);

    /// <summary>Every entry of <see cref="ListStoreAuditAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateStoreAuditAsync(string storeId, IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => PhysicalPosTransport.IterateAsync((page, token) => ListStoreAuditAsync(storeId, page, token), filters, cancellationToken);

    // Registers

    /// <summary>The store's registers (<c>GET /stores/{storeId}/registers</c>); filters <c>limit</c>, <c>cursor</c>, <c>includeTotal</c>, <c>status</c>.</summary>
    public Task<PhysicalPosPage> ListRegistersAsync(string storeId, IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => Page(StorePath(storeId) + "/registers", filters, "listRegisters", cancellationToken);

    /// <summary>Every register of <see cref="ListRegistersAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateRegistersAsync(string storeId, IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => PhysicalPosTransport.IterateAsync((page, token) => ListRegistersAsync(storeId, page, token), filters, cancellationToken);

    /// <summary>Creates a register in a store: <c>{ code, name }</c>; entity <c>register</c>, 201 new / 200 replay.</summary>
    public Task<PhysicalPosWriteResult> CreateRegisterAsync(string storeId, object request, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        var path = StorePath(storeId) + "/registers";
        return KeyedWrite(path, Body(request, PhysicalPosShapes.CreateRegister, "createRegister"), idempotencyKey, "register", cancellationToken);
    }

    /// <summary>Changes a register: <c>{ rowVersion, name? }</c>.</summary>
    public Task<JsonElement> UpdateRegisterAsync(string registerId, object request, CancellationToken cancellationToken = default)
        => RecordWrite(HttpMethod.Patch, RegisterPath(registerId), Body(request, PhysicalPosShapes.UpdateRegister, "updateRegister"), "register", cancellationToken);

    /// <summary>Archives a register: <c>{ rowVersion, reasonCode }</c>.</summary>
    public Task<JsonElement> ArchiveRegisterAsync(string registerId, object request, CancellationToken cancellationToken = default)
        => RecordWrite(HttpMethod.Post, RegisterPath(registerId) + "/archive", StatusChange(request, "archiveRegister"), "register", cancellationToken);

    /// <summary>Restores an archived register: <c>{ rowVersion, reasonCode }</c>.</summary>
    public Task<JsonElement> RestoreRegisterAsync(string registerId, object request, CancellationToken cancellationToken = default)
        => RecordWrite(HttpMethod.Post, RegisterPath(registerId) + "/restore", StatusChange(request, "restoreRegister"), "register", cancellationToken);

    /// <summary>The register's change history (<c>GET /registers/{registerId}/audit</c>).</summary>
    public Task<PhysicalPosPage> ListRegisterAuditAsync(string registerId, IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => Page(RegisterPath(registerId) + "/audit", filters, "history", cancellationToken);

    /// <summary>Every entry of <see cref="ListRegisterAuditAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateRegisterAuditAsync(string registerId, IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => PhysicalPosTransport.IterateAsync((page, token) => ListRegisterAuditAsync(registerId, page, token), filters, cancellationToken);

    // Logical terminals

    /// <summary>Logical terminals (<c>GET /terminals</c>); filters <c>limit</c>, <c>cursor</c>, <c>includeTotal</c>, <c>status</c>, <c>q</c>.</summary>
    public Task<PhysicalPosPage> ListTerminalsAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => Page("/terminals", filters, "listTerminals", cancellationToken);

    /// <summary>Every terminal of <see cref="ListTerminalsAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateTerminalsAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => PhysicalPosTransport.IterateAsync(ListTerminalsAsync, filters, cancellationToken);

    /// <summary>Creates a logical terminal: <c>{ code, displayName, vendorCode?, virtualSerial?, vendorTerminalRef? }</c>; entity <c>terminal</c>.</summary>
    public Task<PhysicalPosWriteResult> CreateTerminalAsync(object request, string? idempotencyKey = null, CancellationToken cancellationToken = default)
        => KeyedWrite("/terminals", Body(request, PhysicalPosShapes.CreateTerminal, "createTerminal"), idempotencyKey, "terminal", cancellationToken);

    /// <summary><c>{ terminal, acquirers: { items, pageInfo } }</c> (<c>GET /terminals/{terminalId}</c>).</summary>
    public async Task<JsonElement> GetTerminalAsync(string terminalId, CancellationToken cancellationToken = default)
        => (await Read(TerminalPath(terminalId), "", cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>Changes a terminal: <c>{ rowVersion, displayName?, vendorCode? }</c>.</summary>
    public Task<JsonElement> UpdateTerminalAsync(string terminalId, object request, CancellationToken cancellationToken = default)
        => RecordWrite(HttpMethod.Patch, TerminalPath(terminalId), Body(request, PhysicalPosShapes.UpdateTerminal, "updateTerminal"), "terminal", cancellationToken);

    /// <summary>Closes a terminal: <c>{ rowVersion, reasonCode }</c>.</summary>
    public Task<JsonElement> CloseTerminalAsync(string terminalId, object request, CancellationToken cancellationToken = default)
        => RecordWrite(HttpMethod.Post, TerminalPath(terminalId) + "/close", StatusChange(request, "closeTerminal"), "terminal", cancellationToken);

    /// <summary>Bank rows of a terminal; a row's <c>id</c> is a payment's <c>terminalAcquirerId</c>.</summary>
    public Task<PhysicalPosPage> ListTerminalAcquirersAsync(string terminalId, IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => Page(TerminalPath(terminalId) + "/acquirers", filters, "listTerminalAcquirers", cancellationToken);

    /// <summary>Every bank row of <see cref="ListTerminalAcquirersAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateTerminalAcquirersAsync(string terminalId, IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => PhysicalPosTransport.IterateAsync((page, token) => ListTerminalAcquirersAsync(terminalId, page, token), filters, cancellationToken);

    /// <summary>Adds a bank row: <c>{ vendorAcquirerRef, merchantNumber, terminalNumber, label?, currencies }</c>; entity <c>acquirer</c>.</summary>
    public Task<PhysicalPosWriteResult> AddTerminalAcquirerAsync(string terminalId, object request, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        var path = TerminalPath(terminalId) + "/acquirers";
        return KeyedWrite(path, Body(request, PhysicalPosShapes.AddTerminalAcquirer, "addTerminalAcquirer"), idempotencyKey, "acquirer", cancellationToken);
    }

    /// <summary>Changes a bank row: <c>{ rowVersion, label?, currencies?, status? }</c>; null clears <c>label</c>.</summary>
    public Task<JsonElement> UpdateTerminalAcquirerAsync(string terminalAcquirerId, object request, CancellationToken cancellationToken = default)
    {
        var path = "/terminal-acquirers/" + PhysicalPosRequest.PathId(terminalAcquirerId, "terminalAcquirerId");
        return RecordWrite(HttpMethod.Patch, path, Body(request, PhysicalPosShapes.UpdateTerminalAcquirer, "updateTerminalAcquirer"), "acquirer", cancellationToken);
    }

    /// <summary>Payment-provider rows of a terminal (merchant / terminal / product code); a row's <c>id</c> is a payment's <c>terminalProviderId</c>.</summary>
    public Task<PhysicalPosPage> ListTerminalProvidersAsync(string terminalId, IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => Page(TerminalPath(terminalId) + "/providers", filters, "listTerminalProviders", cancellationToken);

    /// <summary>Every provider row of <see cref="ListTerminalProvidersAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateTerminalProvidersAsync(string terminalId, IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => PhysicalPosTransport.IterateAsync((page, token) => ListTerminalProvidersAsync(terminalId, page, token), filters, cancellationToken);

    /// <summary>
    /// Adds a provider row: <c>{ vendorAccountId, merchantCode, terminalCode, productCode?, label? }</c>; the provider comes from
    /// the vendor account. Entity <c>provider</c>, 201 new / 200 replay.
    /// </summary>
    public Task<PhysicalPosWriteResult> AddTerminalProviderAsync(string terminalId, object request, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        var path = TerminalPath(terminalId) + "/providers";
        return KeyedWrite(path, Body(request, PhysicalPosShapes.AddTerminalProvider, "addTerminalProvider"), idempotencyKey, "provider", cancellationToken);
    }

    /// <summary>Changes a provider row: <c>{ rowVersion, label?, status? }</c>; null clears <c>label</c>, <c>status</c> is <c>ACTIVE</c> or <c>DISABLED</c>.</summary>
    public Task<JsonElement> UpdateTerminalProviderAsync(string terminalProviderId, object request, CancellationToken cancellationToken = default)
    {
        var path = "/terminal-providers/" + PhysicalPosRequest.PathId(terminalProviderId, "terminalProviderId");
        return RecordWrite(HttpMethod.Patch, path, Body(request, PhysicalPosShapes.UpdateTerminalProvider, "updateTerminalProvider"), "provider", cancellationToken);
    }

    /// <summary>Links the terminal to a device, closing the previous link: <c>{ rowVersion, deviceId, reasonCode }</c>; returns <c>{ terminal, link }</c>.</summary>
    public async Task<JsonElement> ChangeTerminalDeviceAsync(string terminalId, object request, CancellationToken cancellationToken = default)
    {
        var path = TerminalPath(terminalId) + "/device-link";
        return (await Post(path, Body(request, PhysicalPosShapes.ChangeTerminalDevice, "changeTerminalDevice"), cancellationToken).ConfigureAwait(false)).Data;
    }

    /// <summary>Closes the terminal's device link: <c>{ rowVersion, reasonCode }</c>; entity <c>terminal</c>, <c>link</c> is the closed link or null.</summary>
    public Task<PhysicalPosWriteResult> CloseTerminalDeviceLinkAsync(string terminalId, object request, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        var path = TerminalPath(terminalId) + "/device-link/close";
        return KeyedWrite(path, StatusChange(request, "closeTerminalDeviceLink"), idempotencyKey, "terminal", cancellationToken);
    }

    /// <summary>The terminal's device-link history (<c>GET /terminals/{terminalId}/links</c>).</summary>
    public Task<PhysicalPosPage> ListTerminalLinksAsync(string terminalId, IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => Page(TerminalPath(terminalId) + "/links", filters, "history", cancellationToken);

    /// <summary>Every link of <see cref="ListTerminalLinksAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateTerminalLinksAsync(string terminalId, IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => PhysicalPosTransport.IterateAsync((page, token) => ListTerminalLinksAsync(terminalId, page, token), filters, cancellationToken);

    /// <summary>The terminal's change history (<c>GET /terminals/{terminalId}/audit</c>).</summary>
    public Task<PhysicalPosPage> ListTerminalAuditAsync(string terminalId, IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => Page(TerminalPath(terminalId) + "/audit", filters, "history", cancellationToken);

    /// <summary>Every entry of <see cref="ListTerminalAuditAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateTerminalAuditAsync(string terminalId, IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => PhysicalPosTransport.IterateAsync((page, token) => ListTerminalAuditAsync(terminalId, page, token), filters, cancellationToken);

    // Devices

    /// <summary>
    /// Devices (<c>GET /devices</c>); filters <c>limit</c>, <c>cursor</c>, <c>includeTotal</c>, <c>storeId</c>, <c>registerId</c>,
    /// <c>vendorCode</c>, <c>deviceModelId</c>, <c>deviceType</c>, <c>lifecycleStatus</c>, <c>ownershipStatus</c>, <c>integrationChannel</c>, <c>q</c>.
    /// </summary>
    public Task<PhysicalPosPage> ListDevicesAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => Page("/devices", filters, "listDevices", cancellationToken);

    /// <summary>Every device of <see cref="ListDevicesAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateDevicesAsync(IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => PhysicalPosTransport.IterateAsync(ListDevicesAsync, filters, cancellationToken);

    /// <summary>
    /// Registers a device: <c>{ vendorCode, deviceModelId, serialNumber, integrationChannel, displayName?, externalRef?, storeId?,
    /// registerId?, channelVariant?, appMode? }</c>; entity <c>device</c>, 201 new / 200 replay.
    /// </summary>
    public Task<PhysicalPosWriteResult> CreateDeviceAsync(object request, string? idempotencyKey = null, CancellationToken cancellationToken = default)
        => KeyedWrite("/devices", Body(request, PhysicalPosShapes.CreateDevice, "createDevice"), idempotencyKey, "device", cancellationToken);

    /// <summary>One device (<c>GET /devices/{deviceId}</c>).</summary>
    public async Task<JsonElement> GetDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
        => PhysicalPosTransport.Record(await Read(DevicePath(deviceId), "", cancellationToken).ConfigureAwait(false), "device");

    /// <summary>Changes a device: <c>{ rowVersion, displayName?, externalRef?, integrationChannel?, channelVariant?, appMode? }</c>.</summary>
    public Task<JsonElement> UpdateDeviceAsync(string deviceId, object request, CancellationToken cancellationToken = default)
        => RecordWrite(HttpMethod.Patch, DevicePath(deviceId), Body(request, PhysicalPosShapes.UpdateDevice, "updateDevice"), "device", cancellationToken);

    /// <summary>Assigns the device to a store (and register): <c>{ rowVersion, storeId, registerId?, reasonCode }</c>; returns <c>{ device, assignment, previousAssignmentId }</c>.</summary>
    public async Task<JsonElement> AssignDeviceAsync(string deviceId, object request, CancellationToken cancellationToken = default)
    {
        var path = DevicePath(deviceId) + "/assignment";
        return (await Post(path, Body(request, PhysicalPosShapes.AssignDevice, "assignDevice"), cancellationToken).ConfigureAwait(false)).Data;
    }

    /// <summary>Moves the device to storage: <c>{ rowVersion, reasonCode }</c>; entity <c>device</c>, with <c>closedAssignment</c> in <see cref="PhysicalPosWriteResult.Data"/>.</summary>
    public Task<PhysicalPosWriteResult> CloseDeviceAssignmentAsync(string deviceId, object request, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        var path = DevicePath(deviceId) + "/assignment/close";
        return KeyedWrite(path, StatusChange(request, "closeDeviceAssignment"), idempotencyKey, "device", cancellationToken);
    }

    /// <summary>Changes the device lifecycle: <c>{ rowVersion, target (ACTIVE | SUSPENDED | MAINTENANCE), reasonCode }</c>.</summary>
    public Task<JsonElement> ChangeDeviceLifecycleAsync(string deviceId, object request, CancellationToken cancellationToken = default)
        => RecordWrite(HttpMethod.Post, DevicePath(deviceId) + "/lifecycle", Body(request, PhysicalPosShapes.ChangeDeviceLifecycle, "changeDeviceLifecycle"), "device", cancellationToken);

    /// <summary>Retires the device for good: <c>{ rowVersion, reasonCode }</c>; entity <c>device</c>. Retirement is final.</summary>
    public Task<PhysicalPosWriteResult> RetireDeviceAsync(string deviceId, object request, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        var path = DevicePath(deviceId) + "/retire";
        return KeyedWrite(path, StatusChange(request, "retireDevice"), idempotencyKey, "device", cancellationToken);
    }

    /// <summary>The device's store assignments (<c>GET /devices/{deviceId}/assignments</c>).</summary>
    public Task<PhysicalPosPage> ListDeviceAssignmentsAsync(string deviceId, IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => Page(DevicePath(deviceId) + "/assignments", filters, "history", cancellationToken);

    /// <summary>Every assignment of <see cref="ListDeviceAssignmentsAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateDeviceAssignmentsAsync(string deviceId, IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => PhysicalPosTransport.IterateAsync((page, token) => ListDeviceAssignmentsAsync(deviceId, page, token), filters, cancellationToken);

    /// <summary>The device's change history (<c>GET /devices/{deviceId}/audit</c>).</summary>
    public Task<PhysicalPosPage> ListDeviceAuditAsync(string deviceId, IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => Page(DevicePath(deviceId) + "/audit", filters, "history", cancellationToken);

    /// <summary>Every entry of <see cref="ListDeviceAuditAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateDeviceAuditAsync(string deviceId, IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => PhysicalPosTransport.IterateAsync((page, token) => ListDeviceAuditAsync(deviceId, page, token), filters, cancellationToken);

    // Device identity (physical_pos:identity for writes)

    /// <summary>
    /// The device's identity keys, fingerprints only (<c>GET /devices/{deviceId}/keys</c>): <c>keyId</c>, <c>algorithm</c>,
    /// <c>keyStatus</c> (ACTIVE | SUPERSEDED | REVOKED), <c>enrolledVia</c> (PAIRING | ACTIVATION_CODE | ROTATION), <c>fingerprint</c>,
    /// <c>lineageRootKeyId</c>, <c>rotatedFromKeyId</c>, <c>activatedAt</c>, <c>supersededAt</c>, <c>revokedAt</c>, <c>revokeReason</c>, <c>rowVersion</c>.
    /// </summary>
    public Task<PhysicalPosPage> ListDeviceKeysAsync(string deviceId, IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => Page(DevicePath(deviceId) + "/keys", filters, "history", cancellationToken);

    /// <summary>Every key of <see cref="ListDeviceKeysAsync"/>, page by page.</summary>
    public IAsyncEnumerable<JsonElement> IterateDeviceKeysAsync(string deviceId, IReadOnlyDictionary<string, object?>? filters = null, CancellationToken cancellationToken = default)
        => PhysicalPosTransport.IterateAsync((page, token) => ListDeviceKeysAsync(deviceId, page, token), filters, cancellationToken);

    /// <summary>
    /// Revokes a device key: <c>{ rowVersion (the key's), reasonCode }</c>; reason <c>DEVICE_LOST</c>, <c>DEVICE_STOLEN</c>,
    /// <c>KEY_COMPROMISED</c>, <c>DEVICE_REPLACED</c>, <c>MERCHANT_REQUEST</c> or <c>OTHER</c>. Returns <c>{ key, lifecycleStatus, replayed }</c>.
    /// </summary>
    public async Task<JsonElement> RevokeDeviceKeyAsync(string deviceId, string keyId, object request, CancellationToken cancellationToken = default)
    {
        var path = KeyPath(deviceId, keyId) + "/revoke";
        return (await Post(path, Body(request, PhysicalPosShapes.RevokeDeviceKey, "revokeDeviceKey"), cancellationToken).ConfigureAwait(false)).Data;
    }

    /// <summary>
    /// Reverts an activation-code enrollment (revokes the key and suspends the device): <c>{ reasonCode }</c>; reason
    /// <c>NOT_MY_DEVICE</c>, <c>SUSPECTED_PHISHING</c>, <c>WRONG_DEVICE</c> or <c>OTHER</c>. Returns <c>{ key, lifecycleStatus, replayed }</c>.
    /// </summary>
    public async Task<JsonElement> RevertDeviceEnrollmentAsync(string deviceId, string keyId, object request, CancellationToken cancellationToken = default)
    {
        var path = KeyPath(deviceId, keyId) + "/revert-enrollment";
        return (await Post(path, Body(request, PhysicalPosShapes.RevertDeviceEnrollment, "revertDeviceEnrollment"), cancellationToken).ConfigureAwait(false)).Data;
    }

    /// <summary>
    /// Revokes the device's open activation code: <c>{ reasonCode }</c>; reason <c>MERCHANT_REVOKED</c>, <c>DEVICE_LOST</c>,
    /// <c>DEVICE_STOLEN</c> or <c>OTHER</c>. Returns <c>{ deviceId, revokedAt, revokeReason }</c>.
    /// </summary>
    public async Task<JsonElement> RevokeActivationCodeAsync(string deviceId, object request, CancellationToken cancellationToken = default)
    {
        var path = DevicePath(deviceId) + "/activation-code/revoke";
        return (await Post(path, Body(request, PhysicalPosShapes.RevokeActivationCode, "revokeActivationCode"), cancellationToken).ConfigureAwait(false)).Data;
    }

    // End-of-day policy (account, store, device). A level's policy replaces the level above it entirely.

    /// <summary>The account's end-of-day policy (<c>GET /settlement-policy</c>).</summary>
    public Task<JsonElement> GetAccountSettlementPolicyAsync(CancellationToken cancellationToken = default)
        => Policy(HttpMethod.Get, "/settlement-policy", null, cancellationToken);

    /// <summary>
    /// Sets the account's end-of-day policy with the full setting: <c>{ mode (MANUAL | SCHEDULED | DEVICE_AUTO), scheduleTime?,
    /// timeZone?, scheduleDays?, retryWindowMinutes?, retryIntervalMinutes?, offlineBehavior?, overdueAlertHours?,
    /// expectedRowVersion? }</c>. <c>expectedRowVersion</c> is the policy's <c>rowVersion</c> you read (409
    /// <c>physical_pos.version_conflict</c> when it changed); leave it out for last write wins. Returns the <c>policy</c>.
    /// </summary>
    public Task<JsonElement> SetAccountSettlementPolicyAsync(object request, CancellationToken cancellationToken = default)
        => Policy(HttpMethod.Put, "/settlement-policy", SettlementPolicy(request, "setAccountSettlementPolicy"), cancellationToken);

    /// <summary>Resets the account policy to <c>MANUAL</c> (<c>DELETE /settlement-policy</c>); <paramref name="expectedRowVersion"/> is sent as a query parameter.</summary>
    public Task<JsonElement> ClearAccountSettlementPolicyAsync(long? expectedRowVersion = null, CancellationToken cancellationToken = default)
        => Policy(HttpMethod.Delete, "/settlement-policy" + PhysicalPosRequest.ExpectedRowVersionQuery(expectedRowVersion), null, cancellationToken);

    /// <summary>The store's end-of-day policy override (<c>GET /stores/{storeId}/settlement-policy</c>).</summary>
    public Task<JsonElement> GetStoreSettlementPolicyAsync(string storeId, CancellationToken cancellationToken = default)
        => Policy(HttpMethod.Get, StorePath(storeId) + "/settlement-policy", null, cancellationToken);

    /// <summary>Sets the store's override; same body as <see cref="SetAccountSettlementPolicyAsync"/>.</summary>
    public Task<JsonElement> SetStoreSettlementPolicyAsync(string storeId, object request, CancellationToken cancellationToken = default)
    {
        var path = StorePath(storeId) + "/settlement-policy";
        return Policy(HttpMethod.Put, path, SettlementPolicy(request, "setStoreSettlementPolicy"), cancellationToken);
    }

    /// <summary>Removes the store's override; the store then inherits the account policy.</summary>
    public Task<JsonElement> ClearStoreSettlementPolicyAsync(string storeId, long? expectedRowVersion = null, CancellationToken cancellationToken = default)
    {
        var path = StorePath(storeId) + "/settlement-policy";
        return Policy(HttpMethod.Delete, path + PhysicalPosRequest.ExpectedRowVersionQuery(expectedRowVersion), null, cancellationToken);
    }

    /// <summary>The device's end-of-day policy override (<c>GET /devices/{deviceId}/settlement-policy</c>).</summary>
    public Task<JsonElement> GetDeviceSettlementPolicyAsync(string deviceId, CancellationToken cancellationToken = default)
        => Policy(HttpMethod.Get, DevicePath(deviceId) + "/settlement-policy", null, cancellationToken);

    /// <summary>Sets the device's override; <c>DEVICE_AUTO</c> only on devices that report their end-of-day results.</summary>
    public Task<JsonElement> SetDeviceSettlementPolicyAsync(string deviceId, object request, CancellationToken cancellationToken = default)
    {
        var path = DevicePath(deviceId) + "/settlement-policy";
        return Policy(HttpMethod.Put, path, SettlementPolicy(request, "setDeviceSettlementPolicy"), cancellationToken);
    }

    /// <summary>Removes the device's override; the device then inherits the store or account policy.</summary>
    public Task<JsonElement> ClearDeviceSettlementPolicyAsync(string deviceId, long? expectedRowVersion = null, CancellationToken cancellationToken = default)
    {
        var path = DevicePath(deviceId) + "/settlement-policy";
        return Policy(HttpMethod.Delete, path + PhysicalPosRequest.ExpectedRowVersionQuery(expectedRowVersion), null, cancellationToken);
    }

    /// <summary>
    /// The policy a device follows (<c>GET /devices/{deviceId}/settlement-policy/effective</c>): <c>{ deviceId, sourceScopeType
    /// (ACCOUNT | STORE | DEVICE), policy, serverSettlementSupported, deviceAutoSupported, nextScheduledAt, lastSettlementAt,
    /// overdue, scheduleWarning }</c>.
    /// </summary>
    public async Task<JsonElement> GetEffectiveSettlementPolicyAsync(string deviceId, CancellationToken cancellationToken = default)
        => PhysicalPosTransport.Record(await Read(DevicePath(deviceId) + "/settlement-policy/effective", "", cancellationToken).ConfigureAwait(false), "effective");

    // Paths and requests

    private static string StorePath(string storeId) => "/stores/" + PhysicalPosRequest.PathId(storeId, "storeId");
    private static string RegisterPath(string registerId) => "/registers/" + PhysicalPosRequest.PathId(registerId, "registerId");
    private static string TerminalPath(string terminalId) => "/terminals/" + PhysicalPosRequest.PathId(terminalId, "terminalId");
    private static string DevicePath(string deviceId) => "/devices/" + PhysicalPosRequest.PathId(deviceId, "deviceId");
    private static string KeyPath(string deviceId, string keyId) => DevicePath(deviceId) + "/keys/" + PhysicalPosRequest.PathId(keyId, "keyId");

    private static JsonObject Body(object request, PhysicalPosField[] shape, string operation) => PhysicalPosRequest.Body(request, shape, operation + " request");
    private static JsonObject StatusChange(object request, string operation) => Body(request, PhysicalPosShapes.StatusChange, operation);
    private static JsonObject SettlementPolicy(object request, string operation) => Body(request, PhysicalPosShapes.SetSettlementPolicy, operation);

    private Task<LunixiResponse> Read(string path, string query, CancellationToken cancellationToken)
        => PhysicalPosTransport.SendAsync(client, HttpMethod.Get, path + query, null, null, cancellationToken);

    private Task<LunixiResponse> Post(string path, JsonObject body, CancellationToken cancellationToken)
        => PhysicalPosTransport.SendAsync(client, HttpMethod.Post, path, body, null, cancellationToken);

    private async Task<PhysicalPosPage> Page(string path, IReadOnlyDictionary<string, object?>? filters, string route, CancellationToken cancellationToken)
        => new(await Read(path, PhysicalPosRequest.Query(filters, route), cancellationToken).ConfigureAwait(false));

    private async Task<JsonElement> RecordWrite(HttpMethod method, string path, JsonObject body, string name, CancellationToken cancellationToken)
        => PhysicalPosTransport.Record(await PhysicalPosTransport.SendAsync(client, method, path, body, null, cancellationToken).ConfigureAwait(false), name);

    private async Task<JsonElement> Policy(HttpMethod method, string path, JsonObject? body, CancellationToken cancellationToken)
        => PhysicalPosTransport.Record(await PhysicalPosTransport.SendAsync(client, method, path, body, null, cancellationToken).ConfigureAwait(false), "policy");

    private async Task<PhysicalPosWriteResult> KeyedWrite(string path, JsonObject body, string? idempotencyKey, string entity, CancellationToken cancellationToken)
    {
        var key = PhysicalPosRequest.IdempotencyKeyOrNew(idempotencyKey);
        var response = await PhysicalPosTransport.SendAsync(client, HttpMethod.Post, path, body, key, cancellationToken).ConfigureAwait(false);
        return new PhysicalPosWriteResult(response, entity, key);
    }
}
