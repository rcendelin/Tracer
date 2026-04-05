using MediatR;
using Tracer.Application.Mapping;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Queries.GetProfile;

/// <summary>
/// Handles <see cref="GetProfileQuery"/> by loading the profile and its recent change events.
/// </summary>
public sealed class GetProfileHandler : IRequestHandler<GetProfileQuery, GetProfileResult?>
{
    private readonly ICompanyProfileRepository _profileRepository;
    private readonly IChangeEventRepository _changeEventRepository;

    public GetProfileHandler(
        ICompanyProfileRepository profileRepository,
        IChangeEventRepository changeEventRepository)
    {
        _profileRepository = profileRepository;
        _changeEventRepository = changeEventRepository;
    }

    public async Task<GetProfileResult?> Handle(GetProfileQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var profile = await _profileRepository.GetByIdAsync(request.ProfileId, cancellationToken)
            .ConfigureAwait(false);

        if (profile is null)
            return null;

        var recentChanges = await _changeEventRepository.ListByProfileAsync(
            request.ProfileId, page: 0, pageSize: 10, cancellationToken).ConfigureAwait(false);

        return new GetProfileResult
        {
            Profile = profile.ToDto(),
            RecentChanges = recentChanges.Select(c => c.ToDto()).ToList(),
        };
    }
}
