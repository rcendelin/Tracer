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
    public required int TracesToday { get; init; }
    public required int TracesThisWeek { get; init; }
    public required int TotalProfiles { get; init; }
    public required double AverageConfidence { get; init; }
}
