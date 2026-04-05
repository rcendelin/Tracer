using MediatR;
using Tracer.Application.DTOs;

namespace Tracer.Application.Queries.GetProfile;

/// <summary>
/// Query to get a company profile by its ID, including the last 10 change events.
/// </summary>
public sealed record GetProfileQuery(Guid ProfileId) : IRequest<GetProfileResult?>;

/// <summary>
/// Result of a <see cref="GetProfileQuery"/>, containing the profile and recent changes.
/// </summary>
public sealed record GetProfileResult
{
    public required CompanyProfileDto Profile { get; init; }
    public required IReadOnlyCollection<ChangeEventDto> RecentChanges { get; init; }
}
