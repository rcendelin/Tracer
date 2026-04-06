using Tracer.Domain.Entities;
using Tracer.Domain.Enums;

namespace Tracer.Application.Services;

/// <summary>
/// Holds the outcome of a change detection run against a single <see cref="CompanyProfile"/>.
/// </summary>
public sealed record ChangeDetectionResult
{
    public required IReadOnlyCollection<ChangeEvent> Changes { get; init; }

    /// <summary>Any change with severity <see cref="ChangeSeverity.Critical"/>.</summary>
    public bool HasCriticalChanges => Changes.Any(c => c.Severity == ChangeSeverity.Critical);

    /// <summary>Any change with severity ≥ <see cref="ChangeSeverity.Major"/>.</summary>
    public bool HasMajorChanges => Changes.Any(c => c.Severity >= ChangeSeverity.Major);

    /// <summary>Total number of detected changes.</summary>
    public int TotalChanges => Changes.Count;

    /// <summary>
    /// Filters changes by minimum severity (inclusive).
    /// </summary>
    public IReadOnlyCollection<ChangeEvent> GetBySeverity(ChangeSeverity minSeverity) =>
        Changes.Where(c => c.Severity >= minSeverity).ToList();

    /// <summary>An empty result — no changes detected.</summary>
    public static ChangeDetectionResult Empty { get; } = new() { Changes = [] };
}
