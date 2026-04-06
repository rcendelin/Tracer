using System.Text.Json.Serialization;

namespace Tracer.Infrastructure.Providers.SecEdgar;

/// <summary>
/// Response from EDGAR full-text search: <c>GET efts.sec.gov/LATEST/search-index?q={name}&amp;dateRange=custom&amp;forms=10-K</c>.
/// </summary>
internal sealed class EdgarSearchResponse
{
    [JsonPropertyName("hits")]
    public EdgarSearchHits? Hits { get; init; }
}

internal sealed class EdgarSearchHits
{
    [JsonPropertyName("hits")]
    public IReadOnlyCollection<EdgarSearchHit>? Items { get; init; }

    [JsonPropertyName("total")]
    public EdgarTotal? Total { get; init; }
}

internal sealed class EdgarTotal
{
    [JsonPropertyName("value")]
    public int Value { get; init; }
}

internal sealed class EdgarSearchHit
{
    [JsonPropertyName("_source")]
    public EdgarSearchSource? Source { get; init; }
}

internal sealed class EdgarSearchSource
{
    [JsonPropertyName("entity_name")]
    public string? EntityName { get; init; }

    [JsonPropertyName("entity_id")]
    public string? EntityId { get; init; }
}

/// <summary>
/// Company submissions from <c>GET data.sec.gov/submissions/CIK{cik}.json</c>.
/// </summary>
internal sealed class EdgarSubmissions
{
    [JsonPropertyName("cik")]
    public string? Cik { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("sic")]
    public string? Sic { get; init; }

    [JsonPropertyName("sicDescription")]
    public string? SicDescription { get; init; }

    [JsonPropertyName("stateOfIncorporation")]
    public string? StateOfIncorporation { get; init; }

    [JsonPropertyName("entityType")]
    public string? EntityType { get; init; }

    [JsonPropertyName("addresses")]
    public EdgarAddresses? Addresses { get; init; }

    [JsonPropertyName("tickers")]
    public IReadOnlyCollection<string>? Tickers { get; init; }

    [JsonPropertyName("exchanges")]
    public IReadOnlyCollection<string>? Exchanges { get; init; }

    [JsonPropertyName("ein")]
    public string? Ein { get; init; }
}

internal sealed class EdgarAddresses
{
    [JsonPropertyName("business")]
    public EdgarAddress? Business { get; init; }

    [JsonPropertyName("mailing")]
    public EdgarAddress? Mailing { get; init; }
}

internal sealed class EdgarAddress
{
    [JsonPropertyName("street1")]
    public string? Street1 { get; init; }

    [JsonPropertyName("street2")]
    public string? Street2 { get; init; }

    [JsonPropertyName("city")]
    public string? City { get; init; }

    [JsonPropertyName("stateOrCountry")]
    public string? StateOrCountry { get; init; }

    [JsonPropertyName("zipCode")]
    public string? ZipCode { get; init; }
}
