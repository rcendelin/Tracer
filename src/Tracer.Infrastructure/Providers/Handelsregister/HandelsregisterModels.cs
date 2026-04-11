namespace Tracer.Infrastructure.Providers.Handelsregister;

/// <summary>
/// A single row from the Handelsregister search result table.
/// One search may return multiple companies with similar names.
/// </summary>
internal sealed record HandelsregisterSearchResult
{
    /// <summary>Gets the company's legal name as registered.</summary>
    public required string CompanyName { get; init; }

    /// <summary>Gets the register type (e.g. "HRB", "HRA", "GnR", "VR", "PR").</summary>
    public required string RegisterType { get; init; }

    /// <summary>Gets the register number (numeric portion only, e.g. "6324").</summary>
    public required string RegisterNumber { get; init; }

    /// <summary>Gets the registration court name (e.g. "Amtsgericht München").</summary>
    public required string RegisterCourt { get; init; }

    /// <summary>Gets the entity status ("aktiv", "gelöscht", etc.), or <see langword="null"/> if not shown in the result row.</summary>
    public string? Status { get; init; }
}

/// <summary>
/// Detailed company information extracted from a Handelsregister detail page.
/// </summary>
internal sealed record HandelsregisterCompanyDetail
{
    /// <summary>Gets the company's legal name.</summary>
    public required string CompanyName { get; init; }

    /// <summary>Gets the full registration identifier (e.g. "HRB 6324").</summary>
    public required string RegistrationId { get; init; }

    /// <summary>Gets the registration court (e.g. "Amtsgericht München").</summary>
    public required string RegisterCourt { get; init; }

    /// <summary>Gets the company's legal form (e.g. "Gesellschaft mit beschränkter Haftung").</summary>
    public string? LegalForm { get; init; }

    /// <summary>Gets the entity status (e.g. "aktiv", "gelöscht", "aufgelöst").</summary>
    public string? Status { get; init; }

    /// <summary>Gets the registered street address.</summary>
    public string? Street { get; init; }

    /// <summary>Gets the postal code.</summary>
    public string? PostalCode { get; init; }

    /// <summary>Gets the city.</summary>
    public string? City { get; init; }

    /// <summary>Gets the list of officers (Geschäftsführer / Vorstand) names.</summary>
    public IReadOnlyList<string> Officers { get; init; } = [];
}
