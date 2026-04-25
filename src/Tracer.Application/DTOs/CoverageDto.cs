using Tracer.Application.Queries.GetCoverage;

namespace Tracer.Application.DTOs;

/// <summary>
/// Aggregated CKB coverage statistics, grouped by <see cref="GroupBy"/>.
/// </summary>
public sealed record CoverageDto
{
    public required CoverageGroupBy GroupBy { get; init; }
    public required IReadOnlyList<CoverageEntryDto> Entries { get; init; }
}

/// <summary>
/// Single group in a <see cref="CoverageDto"/>. <see cref="Group"/> is
/// <see langword="null"/> for profiles whose group key is missing.
/// </summary>
public sealed record CoverageEntryDto
{
    public string? Group { get; init; }
    public required int ProfileCount { get; init; }
    public required double AvgConfidence { get; init; }
    public required double AvgDataAgeDays { get; init; }
}
