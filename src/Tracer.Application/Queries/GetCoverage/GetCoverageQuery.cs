using MediatR;
using Tracer.Application.DTOs;

namespace Tracer.Application.Queries.GetCoverage;

/// <summary>
/// Aggregated CKB coverage query. Returns per-group row with
/// profile count, average overall confidence and average data age.
/// </summary>
/// <param name="GroupBy">Grouping dimension. Only <see cref="CoverageGroupBy.Country"/> is implemented.</param>
public sealed record GetCoverageQuery(CoverageGroupBy GroupBy) : IRequest<CoverageDto>;
