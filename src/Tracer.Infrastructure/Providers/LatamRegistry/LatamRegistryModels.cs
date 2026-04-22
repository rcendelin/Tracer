namespace Tracer.Infrastructure.Providers.LatamRegistry;

/// <summary>
/// Normalized lookup result from any LATAM registry adapter.
/// All per-country adapters produce this common DTO regardless of the source HTML
/// structure so the provider layer can map fields uniformly.
/// </summary>
internal sealed record LatamRegistrySearchResult
{
    /// <summary>Gets the company's legal name as filed with the registry.</summary>
    public required string EntityName { get; init; }

    /// <summary>
    /// Gets the canonical registration identifier produced by the adapter.
    /// Must be provided without the country prefix — provider adds "<c>{CC}:</c>"
    /// when mapping to <see cref="Domain.Enums.FieldName.RegistrationId"/>.
    /// </summary>
    public required string RegistrationId { get; init; }

    /// <summary>Gets the ISO-3166-1 alpha-2 country code (e.g. "AR", "CL", "CO", "MX").</summary>
    public required string CountryCode { get; init; }

    /// <summary>Gets the raw entity status (e.g. "Activo", "Disuelta", "ACTIVO").</summary>
    public string? Status { get; init; }

    /// <summary>Gets the raw entity type (e.g. "S.A.", "LTDA", "PERSONA MORAL").</summary>
    public string? EntityType { get; init; }

    /// <summary>Gets a single-line address as reported by the registry, if any.</summary>
    public string? Address { get; init; }
}
