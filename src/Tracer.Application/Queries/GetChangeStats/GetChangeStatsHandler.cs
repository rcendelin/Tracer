using MediatR;
using Tracer.Application.DTOs;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Queries.GetChangeStats;

/// <summary>
/// Handles <see cref="GetChangeStatsQuery"/> by counting events per severity in parallel.
/// </summary>
public sealed class GetChangeStatsHandler : IRequestHandler<GetChangeStatsQuery, ChangeStatsDto>
{
    private readonly IChangeEventRepository _repository;

    public GetChangeStatsHandler(IChangeEventRepository repository)
    {
        _repository = repository;
    }

    public async Task<ChangeStatsDto> Handle(
        GetChangeStatsQuery request,
        CancellationToken cancellationToken)
    {
        // EF Core DbContext is not thread-safe — run counts sequentially.
        // These are cheap indexed COUNT queries on a small enum cardinality, so
        // the latency difference vs. fan-out is negligible.
        var critical = await _repository.CountAsync(ChangeSeverity.Critical, null, cancellationToken).ConfigureAwait(false);
        var major    = await _repository.CountAsync(ChangeSeverity.Major,    null, cancellationToken).ConfigureAwait(false);
        var minor    = await _repository.CountAsync(ChangeSeverity.Minor,    null, cancellationToken).ConfigureAwait(false);
        var cosmetic = await _repository.CountAsync(ChangeSeverity.Cosmetic, null, cancellationToken).ConfigureAwait(false);

        return new ChangeStatsDto
        {
            TotalCount    = critical + major + minor + cosmetic,
            CriticalCount = critical,
            MajorCount    = major,
            MinorCount    = minor,
            CosmeticCount = cosmetic,
        };
    }

}
