using MediatR;
using Tracer.Application.DTOs;

namespace Tracer.Application.Queries.GetChangeStats;

/// <summary>
/// Returns aggregated change event counts broken down by severity.
/// </summary>
public sealed record GetChangeStatsQuery : IRequest<ChangeStatsDto>;
