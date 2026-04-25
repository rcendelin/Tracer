using MediatR;
using Tracer.Application.DTOs;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Queries.GetChangeTrend;

/// <summary>
/// Handles <see cref="GetChangeTrendQuery"/> by calling
/// <see cref="IChangeEventRepository.GetMonthlyTrendAsync"/> and pivoting
/// the (year, month, severity) rows into a dense series of monthly buckets.
/// Missing buckets are explicit zeros so the caller can draw a continuous line chart.
/// </summary>
public sealed class GetChangeTrendHandler : IRequestHandler<GetChangeTrendQuery, ChangeTrendDto>
{
    private readonly IChangeEventRepository _repository;

    public GetChangeTrendHandler(IChangeEventRepository repository)
    {
        _repository = repository;
    }

    // Testing seam: unit tests can substitute a deterministic "now".
    internal Func<DateTimeOffset> NowProvider { get; init; } = static () => DateTimeOffset.UtcNow;

    public async Task<ChangeTrendDto> Handle(GetChangeTrendQuery request, CancellationToken cancellationToken)
    {
        // Validator enforces Period == Monthly and Months ∈ [1..36]; we stay defensive anyway.
        var months = Math.Clamp(request.Months, 1, 36);

        var now = NowProvider();
        // Exclusive upper bound = start of the month AFTER the current one (UTC).
        var toExclusive = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
        var fromInclusive = toExclusive.AddMonths(-months);

        var rows = await _repository
            .GetMonthlyTrendAsync(fromInclusive, toExclusive, cancellationToken)
            .ConfigureAwait(false);

        // Pivot rows into a dense per-month series. Use a dictionary keyed by (year, month)
        // to accumulate counts per severity.
        var buckets = new List<ChangeTrendBucketDto>(months);
        var counts = new Dictionary<(int Year, int Month), SeverityCounts>(months);

        foreach (var row in rows)
        {
            var key = (row.Year, row.Month);
            if (!counts.TryGetValue(key, out var c))
                c = new SeverityCounts();

            c = row.Severity switch
            {
                ChangeSeverity.Critical => c with { Critical = c.Critical + row.Count },
                ChangeSeverity.Major => c with { Major = c.Major + row.Count },
                ChangeSeverity.Minor => c with { Minor = c.Minor + row.Count },
                ChangeSeverity.Cosmetic => c with { Cosmetic = c.Cosmetic + row.Count },
                _ => c,
            };

            counts[key] = c;
        }

        var cursor = fromInclusive;
        for (var i = 0; i < months; i++)
        {
            var key = (cursor.Year, cursor.Month);
            var c = counts.TryGetValue(key, out var found) ? found : default;
            buckets.Add(new ChangeTrendBucketDto
            {
                PeriodStart = DateOnly.FromDateTime(cursor.UtcDateTime),
                Critical = c.Critical,
                Major = c.Major,
                Minor = c.Minor,
                Cosmetic = c.Cosmetic,
                Total = c.Critical + c.Major + c.Minor + c.Cosmetic,
            });
            cursor = cursor.AddMonths(1);
        }

        return new ChangeTrendDto
        {
            Period = request.Period,
            Months = months,
            Buckets = buckets,
        };
    }

    private readonly record struct SeverityCounts(int Critical, int Major, int Minor, int Cosmetic);
}
