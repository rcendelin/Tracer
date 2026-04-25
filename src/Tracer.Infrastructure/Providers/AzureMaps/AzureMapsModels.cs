using System.Text.Json.Serialization;

namespace Tracer.Infrastructure.Providers.AzureMaps;

/// <summary>
/// Response from Azure Maps Geocoding API:
/// <c>GET /geocode?api-version=2023-06-01&amp;query={address}</c>
/// </summary>
internal sealed class GeocodeResponse
{
    [JsonPropertyName("features")]
    public IReadOnlyCollection<GeocodeFeature>? Features { get; init; }
}

internal sealed class GeocodeFeature
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("geometry")]
    public GeocodeGeometry? Geometry { get; init; }

    [JsonPropertyName("properties")]
    public GeocodeProperties? Properties { get; init; }
}

internal sealed class GeocodeGeometry
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("coordinates")]
    public IReadOnlyCollection<double>? Coordinates { get; init; }
}

internal sealed class GeocodeProperties
{
    [JsonPropertyName("address")]
    public GeocodeAddress? Address { get; init; }

    [JsonPropertyName("confidence")]
    public string? Confidence { get; init; }

    [JsonPropertyName("matchCodes")]
    public IReadOnlyCollection<string>? MatchCodes { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }
}

internal sealed class GeocodeAddress
{
    [JsonPropertyName("addressLine")]
    public string? AddressLine { get; init; }

    [JsonPropertyName("locality")]
    public string? Locality { get; init; }

    [JsonPropertyName("adminDistricts")]
    public IReadOnlyCollection<GeocodeAdminDistrict>? AdminDistricts { get; init; }

    [JsonPropertyName("postalCode")]
    public string? PostalCode { get; init; }

    [JsonPropertyName("countryRegion")]
    public GeocodeCountryRegion? CountryRegion { get; init; }

    [JsonPropertyName("formattedAddress")]
    public string? FormattedAddress { get; init; }
}

internal sealed class GeocodeAdminDistrict
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("shortName")]
    public string? ShortName { get; init; }
}

internal sealed class GeocodeCountryRegion
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("ISO")]
    public string? Iso { get; init; }
}
