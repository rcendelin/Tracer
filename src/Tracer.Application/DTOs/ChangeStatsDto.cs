namespace Tracer.Application.DTOs;

/// <summary>
/// Aggregated statistics over all change events in the system.
/// </summary>
public sealed record ChangeStatsDto
{
    /// <summary>Total change events recorded.</summary>
    public required int TotalCount { get; init; }

    /// <summary>Events with <c>ChangeSeverity.Critical</c> (entity dissolved, insolvency).</summary>
    public required int CriticalCount { get; init; }

    /// <summary>Events with <c>ChangeSeverity.Major</c> (address / officer / name change).</summary>
    public required int MajorCount { get; init; }

    /// <summary>Events with <c>ChangeSeverity.Minor</c> (phone / email / website change).</summary>
    public required int MinorCount { get; init; }

    /// <summary>Events with <c>ChangeSeverity.Cosmetic</c> (confidence / formatting updates).</summary>
    public required int CosmeticCount { get; init; }
}
