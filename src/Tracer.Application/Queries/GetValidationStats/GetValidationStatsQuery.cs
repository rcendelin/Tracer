using MediatR;
using Tracer.Application.DTOs;

namespace Tracer.Application.Queries.GetValidationStats;

/// <summary>
/// Aggregate counters for the re-validation engine. Powers the Validation Dashboard.
/// </summary>
public sealed record GetValidationStatsQuery : IRequest<ValidationStatsDto>;
