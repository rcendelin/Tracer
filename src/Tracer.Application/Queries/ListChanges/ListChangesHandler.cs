using MediatR;
using Tracer.Application.DTOs;
using Tracer.Application.Mapping;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Queries.ListChanges;

/// <summary>
/// Handles <see cref="ListChangesQuery"/> by querying the change event repository.
/// </summary>
public sealed class ListChangesHandler : IRequestHandler<ListChangesQuery, PagedResult<ChangeEventDto>>
{
    private readonly IChangeEventRepository _repository;

    public ListChangesHandler(IChangeEventRepository repository)
    {
        _repository = repository;
    }

    public async Task<PagedResult<ChangeEventDto>> Handle(
        ListChangesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var page = Math.Max(0, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var items = await _repository.ListAsync(
            page, pageSize,
            request.Severity,
            request.ProfileId,
            cancellationToken).ConfigureAwait(false);

        var total = await _repository.CountAsync(
            request.Severity,
            request.ProfileId,
            cancellationToken).ConfigureAwait(false);

        return new PagedResult<ChangeEventDto>
        {
            Items = items.Select(e => e.ToDto()).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
        };
    }
}
