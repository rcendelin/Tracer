using MediatR;
using Tracer.Application.DTOs;
using Tracer.Domain.Enums;

namespace Tracer.Application.Queries.ListChanges;

/// <summary>
/// Returns a paged list of change events, optionally filtered by severity and/or profile.
/// </summary>
public sealed record ListChangesQuery : IRequest<PagedResult<ChangeEventDto>>
{
    public int Page { get; init; }
    public int PageSize { get; init; } = 20;

    /// <summary>Exact severity filter. <see langword="null"/> returns all severities.</summary>
    public ChangeSeverity? Severity { get; init; }

    /// <summary>Optional profile ID filter to show changes for a single company.</summary>
    public Guid? ProfileId { get; init; }
}
