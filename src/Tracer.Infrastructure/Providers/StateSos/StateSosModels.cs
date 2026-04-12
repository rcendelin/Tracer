namespace Tracer.Infrastructure.Providers.StateSos;

/// <summary>
/// Normalized search result from any US Secretary of State registry.
/// All state adapters produce this common DTO regardless of the source HTML structure.
/// </summary>
internal sealed record StateSosSearchResult
{
    /// <summary>Gets the company's legal name as filed with the state.</summary>
    public required string EntityName { get; init; }

    /// <summary>Gets the state filing number / entity number (e.g. "C0806592" for CA).</summary>
    public required string FilingNumber { get; init; }

    /// <summary>Gets the two-letter US state code (e.g. "CA", "DE", "NY").</summary>
    public required string StateCode { get; init; }

    /// <summary>Gets the entity status (e.g. "Active", "Dissolved", "Suspended").</summary>
    public string? Status { get; init; }

    /// <summary>Gets the entity type (e.g. "Corporation", "LLC", "LP").</summary>
    public string? EntityType { get; init; }

    /// <summary>Gets the formation/registration date, or <see langword="null"/> if not available.</summary>
    public string? FormationDate { get; init; }
}
