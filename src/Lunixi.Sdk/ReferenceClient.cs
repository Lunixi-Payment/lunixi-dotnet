namespace Lunixi;

/// <summary>
/// Lunixi address dictionary for Türkiye (province → district → neighborhood). The codes fill an <c>ADDRESS</c> input
/// field: <c>{ provinceCode, districtCode, neighborhoodCode, line }</c>. Responses are the gateway envelope with
/// <c>data.items: [{ code, name }]</c>; an empty list means that level is not published yet.
/// </summary>
public sealed class ReferenceClient
{
    private const string BasePath = "/api/v1/reference/tr-address";
    private readonly LunixiClient client;
    internal ReferenceClient(LunixiClient client) => this.client = client;

    /// <summary>Lists provinces with their Lunixi code (<c>01</c>–<c>81</c>).</summary>
    public Task<LunixiResponse> ListProvincesAsync(CancellationToken cancellationToken = default)
        => client.SendAsync(HttpMethod.Get, BasePath + "/provinces", signed: false, cancellationToken: cancellationToken);

    /// <summary>Lists the districts of a province; <paramref name="provinceCode"/> is a two-digit code from <c>01</c> to <c>81</c>.</summary>
    public Task<LunixiResponse> ListDistrictsAsync(string provinceCode, CancellationToken cancellationToken = default)
    {
        if (provinceCode is not { Length: 2 } || !provinceCode.All(char.IsAsciiDigit) || provinceCode is "00" || string.CompareOrdinal(provinceCode, "81") > 0)
            throw new LunixiConfigurationException("provinceCode must be a two-digit province code from 01 to 81.");
        return client.SendAsync(HttpMethod.Get, BasePath + "/districts?provinceCode=" + provinceCode, signed: false, cancellationToken: cancellationToken);
    }

    /// <summary>Lists the neighborhoods of a district; <paramref name="districtCode"/> comes from <see cref="ListDistrictsAsync"/>.</summary>
    public Task<LunixiResponse> ListNeighborhoodsAsync(string districtCode, CancellationToken cancellationToken = default)
    {
        if (districtCode is not { Length: >= 1 and <= 12 } || !districtCode.All(char.IsAsciiDigit))
            throw new LunixiConfigurationException("districtCode must be a district code (1 to 12 digits) from ListDistrictsAsync.");
        return client.SendAsync(HttpMethod.Get, BasePath + "/neighborhoods?districtCode=" + districtCode, signed: false, cancellationToken: cancellationToken);
    }
}
