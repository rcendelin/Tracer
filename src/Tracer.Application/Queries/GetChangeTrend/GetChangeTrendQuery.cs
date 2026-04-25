using MediatR;
using Tracer.Application.DTOs;

namespace Tracer.Application.Queries.GetChangeTrend;

/// <summary>
/// Rolling-window time-series query for change events.
/// Returns <paramref name="Months"/> consecutive monthly buckets ending at the current UTC month.
/// </summary>
/// <param name="Period">Bucketing period. Only <see cref="TrendPeriod.Monthly"/> is implemented.</param>
/// <param name="Months">Number of trailing months to include (1–36).</param>
public sealed record GetChangeTrendQuery(TrendPeriod Period, int Months) : IRequest<ChangeTrendDto>;
