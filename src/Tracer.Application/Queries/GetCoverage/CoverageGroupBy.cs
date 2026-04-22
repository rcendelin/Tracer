namespace Tracer.Application.Queries.GetCoverage;

/// <summary>
/// Supported grouping dimensions for <see cref="GetCoverageQuery"/>.
/// Only <see cref="Country"/> is implemented in B-84; Industry / Severity are reserved.
/// </summary>
public enum CoverageGroupBy
{
    Country = 0,
}
