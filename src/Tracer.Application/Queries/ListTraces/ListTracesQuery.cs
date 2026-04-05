using MediatR;
using Tracer.Application.DTOs;
using Tracer.Domain.Enums;

namespace Tracer.Application.Queries.ListTraces;

/// <summary>
/// Query to list trace requests with pagination and optional filters.
/// </summary>
public sealed record ListTracesQuery : IRequest<PagedResult<TraceResultDto>>
{
    public int Page { get; init; }
    public int PageSize { get; init; } = 20;
    public TraceStatus? Status { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public string? Search { get; init; }
}
