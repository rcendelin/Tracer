namespace Tracer.Application.DTOs;

/// <summary>
/// Aggregated statistics over all change events in the system.
/// </summary>
public sealed record ChangeStatsDto
{
    public required int TotalCount { get; init; }
    public required int CriticalCount { get; init; }
    public required int MajorCount { get; init; }
    public required int MinorCount { get; init; }
    public required int CosmeticCount { get; init; }
}
