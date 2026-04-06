using System.Text.Json.Serialization;

namespace Tracer.Infrastructure.Providers.CompaniesHouse;

/// <summary>
/// Search response from Companies House API: <c>GET /search/companies?q={name}</c>.
/// </summary>
internal sealed class CompanySearchResponse
{
    [JsonPropertyName("items")]
    public IReadOnlyCollection<CompanySearchItem>? Items { get; init; }

    [JsonPropertyName("total_results")]
    public int TotalResults { get; init; }
}

internal sealed class CompanySearchItem
{
    [JsonPropertyName("company_number")]
    public string? CompanyNumber { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("company_status")]
    public string? CompanyStatus { get; init; }

    [JsonPropertyName("company_type")]
    public string? CompanyType { get; init; }

    [JsonPropertyName("address_snippet")]
    public string? AddressSnippet { get; init; }
}

/// <summary>
/// Company profile from <c>GET /company/{company_number}</c>.
/// </summary>
internal sealed class CompaniesHouseCompanyProfile
{
    [JsonPropertyName("company_name")]
    public string? CompanyName { get; init; }

    [JsonPropertyName("company_number")]
    public string? CompanyNumber { get; init; }

    [JsonPropertyName("company_status")]
    public string? CompanyStatus { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("registered_office_address")]
    public CompanyAddress? RegisteredOfficeAddress { get; init; }

    [JsonPropertyName("sic_codes")]
    public IReadOnlyCollection<string>? SicCodes { get; init; }

    [JsonPropertyName("date_of_creation")]
    public string? DateOfCreation { get; init; }

    [JsonPropertyName("date_of_cessation")]
    public string? DateOfCessation { get; init; }

    [JsonPropertyName("jurisdiction")]
    public string? Jurisdiction { get; init; }
}

internal sealed class CompanyAddress
{
    [JsonPropertyName("address_line_1")]
    public string? AddressLine1 { get; init; }

    [JsonPropertyName("address_line_2")]
    public string? AddressLine2 { get; init; }

    [JsonPropertyName("locality")]
    public string? Locality { get; init; }

    [JsonPropertyName("region")]
    public string? Region { get; init; }

    [JsonPropertyName("postal_code")]
    public string? PostalCode { get; init; }

    [JsonPropertyName("country")]
    public string? Country { get; init; }
}
