using MediatR;
using Tracer.Application.DTOs;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Queries.GetValidationStats;

/// <summary>
/// Computes aggregate counters for the re-validation dashboard by querying
/// company profiles, validation records, and change events sequentially.
/// </summary>
/// <remarks>
/// EF Core <c>DbContext</c> is not thread-safe; the dependencies are scoped
/// repositories sharing one context, so all reads are awaited sequentially.
/// </remarks>
public sealed class GetValidationStatsHandler : IRequestHandler<GetValidationStatsQuery, ValidationStatsDto>
{
    private readonly ICompanyProfileRepository _profiles;
    private readonly IValidationRecordRepository _validations;
    private readonly IChangeEventRepository _changes;

    public GetValidationStatsHandler(
        ICompanyProfileRepository profiles,
        IValidationRecordRepository validations,
        IChangeEventRepository changes)
    {
        _profiles = profiles;
        _validations = validations;
        _changes = changes;
    }

    public async Task<ValidationStatsDto> Handle(
        GetValidationStatsQuery request,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var startOfDayUtc = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);

        var pending = await _profiles
            .CountRevalidationCandidatesAsync(cancellationToken)
            .ConfigureAwait(false);

        var processedToday = await _validations
            .CountSinceAsync(startOfDayUtc, cancellationToken)
            .ConfigureAwait(false);

        var changesToday = await _changes
            .CountSinceAsync(startOfDayUtc, cancellationToken)
            .ConfigureAwait(false);

        var averageAge = await _profiles
            .AverageDaysSinceLastValidationAsync(now, cancellationToken)
            .ConfigureAwait(false);

        return new ValidationStatsDto
        {
            PendingCount = pending,
            ProcessedToday = processedToday,
            ChangesDetectedToday = changesToday,
            AverageDataAgeDays = Math.Round(averageAge, 2, MidpointRounding.AwayFromZero),
        };
    }
}
