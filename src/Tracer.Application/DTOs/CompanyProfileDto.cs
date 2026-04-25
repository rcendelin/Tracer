namespace Tracer.Application.DTOs;

/// <summary>
/// DTO for a CKB company profile (for directory/detail views).
/// </summary>
public sealed record CompanyProfileDto
{
    /// <summary>CKB primary key.</summary>
    public required Guid Id { get; init; }

    /// <summary>Stable identifier: <c>{CountryCode}:{RegistrationId}</c> (e.g. <c>"CZ:00177041"</c>). Survives renames.</summary>
    public required string NormalizedKey { get; init; }

    /// <summary>ISO 3166-1 alpha-2 country code.</summary>
    public required string Country { get; init; }

    /// <summary>Official registration ID (IČO, CRN, CNPJ, ...). Nullable when only a free-text name is known.</summary>
    public string? RegistrationId { get; init; }

    /// <summary>Enriched field set (legal name, address, phone, ...). Null for list views.</summary>
    public EnrichedCompanyDto? Enriched { get; init; }

    /// <summary>UTC timestamp when the profile was first created in CKB.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>UTC timestamp of the most recent successful enrichment.</summary>
    public DateTimeOffset? LastEnrichedAt { get; init; }

    /// <summary>UTC timestamp of the most recent re-validation run.</summary>
    public DateTimeOffset? LastValidatedAt { get; init; }

    /// <summary>Total number of trace requests that mapped to this profile.</summary>
    public int TraceCount { get; init; }

    /// <summary>Weighted aggregate confidence across all enriched fields (0.0–1.0).</summary>
    public double? OverallConfidence { get; init; }

    /// <summary>When <c>true</c>, the profile is excluded from the default list view and re-validation sweeps.</summary>
    public bool IsArchived { get; init; }
}
