using MediatR;
using Tracer.Application.DTOs;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Queries.GetCoverage;

/// <summary>
/// Handles <see cref="GetCoverageQuery"/> by delegating to
/// <see cref="ICompanyProfileRepository.GetCoverageByCountryAsync"/>
/// and computing averages from the returned sums / sample counts.
/// </summary>
public sealed class GetCoverageHandler : IRequestHandler<GetCoverageQuery, CoverageDto>
{
    // DoS guard against unbounded country sets; the current planet has ~200 ISO 3166-1 alpha-2 codes.
    internal const int MaxCountries = 500;

    private readonly ICompanyProfileRepository _repository;

    public GetCoverageHandler(ICompanyProfileRepository repository)
    {
        _repository = repository;
    }

    // Testing seam: unit tests can substitute a deterministic "now".
    internal Func<DateTimeOffset> NowProvider { get; init; } = static () => DateTimeOffset.UtcNow;

    public async Task<CoverageDto> Handle(GetCoverageQuery request, CancellationToken cancellationToken)
    {
        var now = NowProvider();

        var rows = await _repository
            .GetCoverageByCountryAsync(now, MaxCountries, cancellationToken)
            .ConfigureAwait(false);

        var entries = new List<CoverageEntryDto>(rows.Count);
        foreach (var row in rows)
        {
            var avgConfidence = row.ConfidenceSampleCount > 0
                ? row.ConfidenceSum / row.ConfidenceSampleCount
                : 0d;
            var avgDataAgeDays = row.EnrichedSampleCount > 0
                ? (double)row.EnrichedSumDays / row.EnrichedSampleCount
                : 0d;

            entries.Add(new CoverageEntryDto
            {
                Group = string.IsNullOrEmpty(row.Country) ? null : row.Country,
                ProfileCount = row.ProfileCount,
                AvgConfidence = avgConfidence,
                AvgDataAgeDays = avgDataAgeDays,
            });
        }

        return new CoverageDto
        {
            GroupBy = request.GroupBy,
            Entries = entries,
        };
    }
}
