using MediatR;
using Tracer.Application.DTOs;

namespace Tracer.Application.Queries.GetChangeStats;

/// <summary>
/// Returns aggregated change event counts broken down by severity.
/// </summary>
public sealed record GetChangeStatsQuery : IRequest<ChangeStatsDto>
{
    /// <summary>
    /// Optional inclusive lower bound on <c>DetectedAt</c>. When set, counts include
    /// only change events detected at or after this timestamp. <see langword="null"/>
    /// returns all-time counts. Used by the B-73 Change Feed "last 7 days" header.
    /// </summary>
    public DateTimeOffset? Since { get; init; }
}
