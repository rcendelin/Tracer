using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Tracer.Application.DTOs;
using Tracer.Application.Queries.GetChangeStats;
using Tracer.Application.Queries.ListChanges;
using Tracer.Domain.Enums;

namespace Tracer.Api.Endpoints;

/// <summary>
/// Minimal API endpoints for change event feed and statistics.
/// </summary>
internal static class ChangesEndpoints
{
    public static RouteGroupBuilder MapChangesEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/changes")
            .WithTags("Changes")
            .WithOpenApi();

        group.MapGet("/", ListChangesAsync)
            .WithName("ListChanges")
            .WithSummary("List change events (paged, filterable by severity and profile)")
            .Produces<PagedResult<ChangeEventDto>>();

        group.MapGet("/stats", GetChangeStatsAsync)
            .WithName("GetChangeStats")
            .WithSummary("Get aggregated change event counts by severity")
            .Produces<ChangeStatsDto>();

        return group;
    }

    private static async Task<Ok<PagedResult<ChangeEventDto>>> ListChangesAsync(
        IMediator mediator,
        CancellationToken cancellationToken,
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = 20,
        [FromQuery] ChangeSeverity? severity = null,
        [FromQuery] Guid? profileId = null)
    {
        var result = await mediator.Send(
            new ListChangesQuery
            {
                Page = page,
                PageSize = pageSize,
                Severity = severity,
                ProfileId = profileId,
            },
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(result);
    }

    private static async Task<Ok<ChangeStatsDto>> GetChangeStatsAsync(
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetChangeStatsQuery(), cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(result);
    }
}
