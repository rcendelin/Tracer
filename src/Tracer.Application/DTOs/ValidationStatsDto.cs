namespace Tracer.Application.DTOs;

/// <summary>
/// DTO for re-validation engine statistics.
/// </summary>
public sealed record ValidationStatsDto
{
    public required int PendingCount { get; init; }
    public required int ProcessedToday { get; init; }
    public required int ChangesDetectedToday { get; init; }
    public required double AverageDataAgeDays { get; init; }
}
