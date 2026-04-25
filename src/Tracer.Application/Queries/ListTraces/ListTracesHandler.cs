using MediatR;
using Tracer.Application.DTOs;
using Tracer.Application.Mapping;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Queries.ListTraces;

/// <summary>
/// Handles <see cref="ListTracesQuery"/> by loading paged trace requests.
/// </summary>
public sealed class ListTracesHandler : IRequestHandler<ListTracesQuery, PagedResult<TraceResultDto>>
{
    private readonly ITraceRequestRepository _repository;

    public ListTracesHandler(ITraceRequestRepository repository)
    {
        _repository = repository;
    }

    public async Task<PagedResult<TraceResultDto>> Handle(ListTracesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var page = Math.Max(0, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var items = await _repository.ListAsync(
            page, pageSize,
            request.Status, request.From, request.To, request.Search,
            cancellationToken).ConfigureAwait(false);

        var totalCount = await _repository.CountAsync(
            request.Status, request.From, request.To, request.Search,
            cancellationToken).ConfigureAwait(false);

        return new PagedResult<TraceResultDto>
        {
            Items = items.Select(r => r.ToResultDto()).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }
}
