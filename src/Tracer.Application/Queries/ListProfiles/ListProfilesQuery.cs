using MediatR;
using Tracer.Application.DTOs;

namespace Tracer.Application.Queries.ListProfiles;

/// <summary>
/// Query to list CKB company profiles with pagination and optional filters.
/// </summary>
public sealed record ListProfilesQuery : IRequest<PagedResult<CompanyProfileDto>>
{
    public int Page { get; init; }
    public int PageSize { get; init; } = 20;
    public string? Search { get; init; }
    public string? Country { get; init; }
    public double? MinConfidence { get; init; }
    public double? MaxConfidence { get; init; }
    public DateTimeOffset? ValidatedBefore { get; init; }
    public bool IncludeArchived { get; init; }
}
