using MediatR;
using Tracer.Application.DTOs;
using Tracer.Application.Mapping;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Queries.GetProfileHistory;

/// <summary>
/// Handles <see cref="GetProfileHistoryQuery"/> by loading change events and validation records for a profile.
/// </summary>
public sealed class GetProfileHistoryHandler : IRequestHandler<GetProfileHistoryQuery, GetProfileHistoryResult?>
{
    private readonly ICompanyProfileRepository _profileRepository;
    private readonly IChangeEventRepository _changeEventRepository;
    private readonly IValidationRecordRepository _validationRecordRepository;

    public GetProfileHistoryHandler(
        ICompanyProfileRepository profileRepository,
        IChangeEventRepository changeEventRepository,
        IValidationRecordRepository validationRecordRepository)
    {
        _profileRepository = profileRepository;
        _changeEventRepository = changeEventRepository;
        _validationRecordRepository = validationRecordRepository;
    }

    public async Task<GetProfileHistoryResult?> Handle(GetProfileHistoryQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var profile = await _profileRepository.GetByIdAsync(request.ProfileId, cancellationToken)
            .ConfigureAwait(false);

        if (profile is null)
            return null;

        var page = Math.Max(0, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var changes = await _changeEventRepository.ListByProfileAsync(
            request.ProfileId, page, pageSize, cancellationToken).ConfigureAwait(false);
        var totalChanges = await _changeEventRepository.CountByProfileAsync(
            request.ProfileId, cancellationToken).ConfigureAwait(false);
        var validations = await _validationRecordRepository.ListByProfileAsync(
            request.ProfileId, page: 0, pageSize: 20, cancellationToken).ConfigureAwait(false);

        return new GetProfileHistoryResult
        {
            Changes = new PagedResult<ChangeEventDto>
            {
                Items = changes.Select(c => c.ToDto()).ToList(),
                TotalCount = totalChanges,
                Page = page,
                PageSize = pageSize,
            },
            Validations = validations.Select(v => v.ToDto()).ToList(),
        };
    }
}
