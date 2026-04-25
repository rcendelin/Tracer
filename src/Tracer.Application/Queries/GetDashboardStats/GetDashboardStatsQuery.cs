using MediatR;

namespace Tracer.Application.Queries.GetDashboardStats;

/// <summary>
/// Query for dashboard statistics.
/// </summary>
public sealed record GetDashboardStatsQuery : IRequest<DashboardStatsDto>;

/// <summary>
/// Dashboard statistics DTO.
/// </summary>
public sealed record DashboardStatsDto
{
    /// <summary>Trace requests accepted since the start of the current UTC day.</summary>
    public required int TracesToday { get; init; }

    /// <summary>Trace requests accepted since the start of the current UTC week (Sunday rollover).</summary>
    public required int TracesThisWeek { get; init; }

    /// <summary>Total non-archived CKB profiles.</summary>
    public required int TotalProfiles { get; init; }

    /// <summary>Mean <c>OverallConfidence</c> across all profiles (0.0–1.0).</summary>
    public required double AverageConfidence { get; init; }
}
