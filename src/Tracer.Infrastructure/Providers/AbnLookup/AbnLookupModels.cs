using System.Text.Json.Serialization;

namespace Tracer.Infrastructure.Providers.AbnLookup;

/// <summary>
/// Response from ABN Lookup JSON endpoint: <c>GET /json/AbnDetails.aspx?abn={abn}&amp;guid={guid}</c>.
/// Note: ABN Lookup returns JSONP by default, we request JSON callback=none.
/// </summary>
internal sealed class AbnDetailsResponse
{
    [JsonPropertyName("Abn")]
    public string? Abn { get; init; }

    [JsonPropertyName("AbnStatus")]
    public string? AbnStatus { get; init; }

    [JsonPropertyName("AbnStatusEffectiveFrom")]
    public string? AbnStatusEffectiveFrom { get; init; }

    [JsonPropertyName("Gst")]
    public string? Gst { get; init; }

    [JsonPropertyName("EntityName")]
    public string? EntityName { get; init; }

    [JsonPropertyName("EntityTypeCode")]
    public string? EntityTypeCode { get; init; }

    [JsonPropertyName("EntityTypeName")]
    public string? EntityTypeName { get; init; }

    [JsonPropertyName("AddressState")]
    public string? AddressState { get; init; }

    [JsonPropertyName("AddressPostcode")]
    public string? AddressPostcode { get; init; }

    [JsonPropertyName("BusinessName")]
    public IReadOnlyCollection<string>? BusinessName { get; init; }

    [JsonPropertyName("Message")]
    public string? Message { get; init; }
}

/// <summary>
/// Response from ABN name search: <c>GET /json/MatchingNames.aspx?name={name}&amp;guid={guid}</c>.
/// </summary>
internal sealed class AbnSearchResponse
{
    [JsonPropertyName("Names")]
    public IReadOnlyCollection<AbnSearchResult>? Names { get; init; }

    [JsonPropertyName("Message")]
    public string? Message { get; init; }
}

internal sealed class AbnSearchResult
{
    [JsonPropertyName("Abn")]
    public string? Abn { get; init; }

    [JsonPropertyName("Name")]
    public string? Name { get; init; }

    [JsonPropertyName("NameType")]
    public string? NameType { get; init; }

    [JsonPropertyName("State")]
    public string? State { get; init; }

    [JsonPropertyName("Postcode")]
    public string? Postcode { get; init; }

    [JsonPropertyName("Score")]
    public int Score { get; init; }
}
