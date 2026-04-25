using MediatR;
using Tracer.Application.DTOs;

namespace Tracer.Application.Queries.ListValidationQueue;

/// <summary>
/// Returns a paged view of the profiles the re-validation scheduler will
/// pick up next. Items that would be chosen but have no expired field TTL
/// are filtered out by the handler via <c>IFieldTtlPolicy</c>.
/// </summary>
public sealed record ListValidationQueueQuery : IRequest<PagedResult<ValidationQueueItemDto>>
{
    public int Page { get; init; }
    public int PageSize { get; init; } = 20;
}
