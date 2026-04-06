using MediatR;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Queries.GetDashboardStats;

/// <summary>
/// Handles <see cref="GetDashboardStatsQuery"/> by querying trace and profile counts.
/// </summary>
public sealed class GetDashboardStatsHandler : IRequestHandler<GetDashboardStatsQuery, DashboardStatsDto>
{
    private readonly ITraceRequestRepository _traceRepo;
    private readonly ICompanyProfileRepository _profileRepo;

    public GetDashboardStatsHandler(
        ITraceRequestRepository traceRepo,
        ICompanyProfileRepository profileRepo)
    {
        _traceRepo = traceRepo;
        _profileRepo = profileRepo;
    }

    public async Task<DashboardStatsDto> Handle(GetDashboardStatsQuery request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var startOfDay = new DateTimeOffset(now.Date, TimeSpan.Zero);
        var startOfWeek = startOfDay.AddDays(-(int)now.DayOfWeek);

        var tracesToday = await _traceRepo.CountAsync(
            dateFrom: startOfDay, cancellationToken: cancellationToken).ConfigureAwait(false);

        var tracesThisWeek = await _traceRepo.CountAsync(
            dateFrom: startOfWeek, cancellationToken: cancellationToken).ConfigureAwait(false);

        var totalProfiles = await _profileRepo.CountAsync(
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return new DashboardStatsDto
        {
            TracesToday = tracesToday,
            TracesThisWeek = tracesThisWeek,
            TotalProfiles = totalProfiles,
            AverageConfidence = 0.0, // TODO: aggregate query on CompanyProfiles
        };
    }
}
