using MediatR;
using Tracer.Application.DTOs;
using Tracer.Application.Mapping;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Queries.ListProfiles;

/// <summary>
/// Handles <see cref="ListProfilesQuery"/> by loading paged company profiles.
/// </summary>
public sealed class ListProfilesHandler : IRequestHandler<ListProfilesQuery, PagedResult<CompanyProfileDto>>
{
    private readonly ICompanyProfileRepository _repository;

    public ListProfilesHandler(ICompanyProfileRepository repository)
    {
        _repository = repository;
    }

    public async Task<PagedResult<CompanyProfileDto>> Handle(ListProfilesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var page = Math.Max(0, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var items = await _repository.ListAsync(
            page, pageSize,
            request.Country, request.MinConfidence, request.MaxConfidence,
            request.ValidatedBefore, request.IncludeArchived,
            cancellationToken).ConfigureAwait(false);

        var totalCount = await _repository.CountAsync(
            request.Country, request.MinConfidence, request.MaxConfidence,
            request.ValidatedBefore, request.IncludeArchived,
            cancellationToken).ConfigureAwait(false);

        return new PagedResult<CompanyProfileDto>
        {
            Items = items.Select(p => p.ToDto()).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }
}
