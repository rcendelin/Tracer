namespace Tracer.Application.Queries.GetChangeTrend;

/// <summary>
/// Supported time-series bucketing periods for <see cref="GetChangeTrendQuery"/>.
/// Only <see cref="Monthly"/> is implemented in B-84; Weekly/Daily are reserved
/// for future extensions and will fail validation until wired up.
/// </summary>
public enum TrendPeriod
{
    Monthly = 0,
}
