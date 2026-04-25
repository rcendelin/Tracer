using MediatR;
using Tracer.Application.DTOs;

namespace Tracer.Application.Queries.GetProfileHistory;

/// <summary>
/// Query to get the change history and validation records for a company profile.
/// </summary>
public sealed record GetProfileHistoryQuery : IRequest<GetProfileHistoryResult?>
{
    public required Guid ProfileId { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; } = 20;
}

/// <summary>
/// Result containing paged change events and validation records for a profile.
/// </summary>
public sealed record GetProfileHistoryResult
{
    public required PagedResult<ChangeEventDto> Changes { get; init; }
    public required IReadOnlyCollection<ValidationRecordDto> Validations { get; init; }
}
