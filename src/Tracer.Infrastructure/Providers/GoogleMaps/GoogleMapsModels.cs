using System.Text.Json.Serialization;

namespace Tracer.Infrastructure.Providers.GoogleMaps;

/// <summary>
/// Response from Google Maps Places API (New): <c>POST /v1/places:searchText</c>.
/// </summary>
internal sealed class PlacesSearchResponse
{
    [JsonPropertyName("places")]
    public IReadOnlyCollection<PlaceResult>? Places { get; init; }
}

/// <summary>
/// A single place result from the Places API (New).
/// </summary>
internal sealed class PlaceResult
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("displayName")]
    public PlaceLocalizedText? DisplayName { get; init; }

    [JsonPropertyName("formattedAddress")]
    public string? FormattedAddress { get; init; }

    [JsonPropertyName("addressComponents")]
    public IReadOnlyCollection<AddressComponent>? AddressComponents { get; init; }

    [JsonPropertyName("location")]
    public PlaceLocation? Location { get; init; }

    [JsonPropertyName("nationalPhoneNumber")]
    public string? NationalPhoneNumber { get; init; }

    [JsonPropertyName("internationalPhoneNumber")]
    public string? InternationalPhoneNumber { get; init; }

    [JsonPropertyName("websiteUri")]
    public string? WebsiteUri { get; init; }

    [JsonPropertyName("types")]
    public IReadOnlyCollection<string>? Types { get; init; }

    [JsonPropertyName("businessStatus")]
    public string? BusinessStatus { get; init; }

    [JsonPropertyName("primaryType")]
    public string? PrimaryType { get; init; }
}

internal sealed class PlaceLocalizedText
{
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("languageCode")]
    public string? LanguageCode { get; init; }
}

internal sealed class AddressComponent
{
    [JsonPropertyName("longText")]
    public string? LongText { get; init; }

    [JsonPropertyName("shortText")]
    public string? ShortText { get; init; }

    [JsonPropertyName("types")]
    public IReadOnlyCollection<string>? Types { get; init; }
}

internal sealed class PlaceLocation
{
    [JsonPropertyName("latitude")]
    public double Latitude { get; init; }

    [JsonPropertyName("longitude")]
    public double Longitude { get; init; }
}
