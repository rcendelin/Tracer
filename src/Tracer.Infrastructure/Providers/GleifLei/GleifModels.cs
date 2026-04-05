using System.Text.Json.Serialization;

namespace Tracer.Infrastructure.Providers.GleifLei;

/// <summary>
/// Root response from GLEIF LEI API: <c>GET /api/v1/lei-records</c>.
/// Follows JSON:API format.
/// </summary>
internal sealed class GleifSearchResponse
{
    [JsonPropertyName("data")]
    public IReadOnlyCollection<GleifLeiRecord>? Data { get; init; }
}

/// <summary>
/// A single LEI record from the GLEIF API.
/// </summary>
internal sealed class GleifLeiRecord
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("attributes")]
    public GleifAttributes? Attributes { get; init; }
}

internal sealed class GleifAttributes
{
    [JsonPropertyName("lei")]
    public string? Lei { get; init; }

    [JsonPropertyName("entity")]
    public GleifEntity? Entity { get; init; }

    [JsonPropertyName("registration")]
    public GleifRegistration? Registration { get; init; }
}

internal sealed class GleifEntity
{
    [JsonPropertyName("legalName")]
    public GleifLocalizedName? LegalName { get; init; }

    [JsonPropertyName("legalAddress")]
    public GleifAddress? LegalAddress { get; init; }

    [JsonPropertyName("headquartersAddress")]
    public GleifAddress? HeadquartersAddress { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("jurisdiction")]
    public string? Jurisdiction { get; init; }

    [JsonPropertyName("registeredAs")]
    public string? RegisteredAs { get; init; }

    [JsonPropertyName("legalForm")]
    public GleifLegalForm? LegalForm { get; init; }

    [JsonPropertyName("associatedEntity")]
    public GleifAssociatedEntity? AssociatedEntity { get; init; }
}

internal sealed class GleifLocalizedName
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("language")]
    public string? Language { get; init; }
}

internal sealed class GleifAddress
{
    [JsonPropertyName("addressLines")]
    public IReadOnlyCollection<string>? AddressLines { get; init; }

    [JsonPropertyName("addressNumber")]
    public string? AddressNumber { get; init; }

    [JsonPropertyName("city")]
    public string? City { get; init; }

    [JsonPropertyName("region")]
    public string? Region { get; init; }

    [JsonPropertyName("country")]
    public string? Country { get; init; }

    [JsonPropertyName("postalCode")]
    public string? PostalCode { get; init; }
}

internal sealed class GleifLegalForm
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("other")]
    public string? Other { get; init; }
}

internal sealed class GleifAssociatedEntity
{
    [JsonPropertyName("lei")]
    public string? Lei { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

internal sealed class GleifRegistration
{
    [JsonPropertyName("managingLou")]
    public string? ManagingLou { get; init; }

    [JsonPropertyName("corroborationLevel")]
    public string? CorroborationLevel { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

/// <summary>
/// Response for relationship queries (direct-parent, ultimate-parent).
/// </summary>
internal sealed class GleifRelationshipResponse
{
    [JsonPropertyName("data")]
    public IReadOnlyCollection<GleifRelationship>? Data { get; init; }
}

internal sealed class GleifRelationship
{
    [JsonPropertyName("attributes")]
    public GleifRelationshipAttributes? Attributes { get; init; }
}

internal sealed class GleifRelationshipAttributes
{
    [JsonPropertyName("relationship")]
    public GleifRelationshipDetail? Relationship { get; init; }
}

internal sealed class GleifRelationshipDetail
{
    [JsonPropertyName("startNode")]
    public GleifRelationshipNode? StartNode { get; init; }

    [JsonPropertyName("endNode")]
    public GleifRelationshipNode? EndNode { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }
}

internal sealed class GleifRelationshipNode
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

/// <summary>
/// Wrapper for single LEI record response (JSON:API format).
/// </summary>
internal sealed class GleifSingleResponse
{
    [JsonPropertyName("data")]
    public GleifLeiRecord? Data { get; init; }
}
