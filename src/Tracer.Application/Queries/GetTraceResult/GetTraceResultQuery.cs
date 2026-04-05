using MediatR;
using Tracer.Application.DTOs;

namespace Tracer.Application.Queries.GetTraceResult;

/// <summary>
/// Query to get a trace result by its ID.
/// </summary>
public sealed record GetTraceResultQuery(Guid TraceId) : IRequest<TraceResultDto?>;
