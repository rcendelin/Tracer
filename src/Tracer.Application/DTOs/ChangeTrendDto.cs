using Tracer.Application.Queries.GetChangeTrend;

namespace Tracer.Application.DTOs;

/// <summary>
/// Time-series trend of change events, bucketed by <see cref="TrendPeriod"/>.
/// </summary>
public sealed record ChangeTrendDto
{
    public required TrendPeriod Period { get; init; }
    public required int Months { get; init; }
    public required IReadOnlyList<ChangeTrendBucketDto> Buckets { get; init; }
}

/// <summary>
/// Single time bucket in a <see cref="ChangeTrendDto"/>.
/// </summary>
public sealed record ChangeTrendBucketDto
{
    /// <summary>First day of the bucket (UTC). For monthly buckets, always day 1.</summary>
    public required DateOnly PeriodStart { get; init; }

    public required int Critical { get; init; }
    public required int Major { get; init; }
    public required int Minor { get; init; }
    public required int Cosmetic { get; init; }
    public required int Total { get; init; }
}
