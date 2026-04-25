using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Tracer.Application.DTOs;
using Tracer.Application.Queries.GetValidationStats;
using Tracer.Application.Queries.ListValidationQueue;

namespace Tracer.Api.Endpoints;

/// <summary>
/// Minimal API endpoints exposing the re-validation engine to the operator UI.
/// Read-only surface — manual enqueue happens via <c>POST /api/profiles/{id}/revalidate</c>.
/// </summary>
internal static class ValidationEndpoints
{
    public static RouteGroupBuilder MapValidationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/validation")
            .WithTags("Validation")
            .WithOpenApi();

        group.MapGet("/stats", GetValidationStatsAsync)
            .WithName("GetValidationStats")
            .WithSummary("Get re-validation engine statistics")
            .Produces<ValidationStatsDto>();

        group.MapGet("/queue", ListValidationQueueAsync)
            .WithName("ListValidationQueue")
            .WithSummary("List profiles pending re-validation")
            .Produces<PagedResult<ValidationQueueItemDto>>();

        return group;
    }

    private static async Task<Ok<ValidationStatsDto>> GetValidationStatsAsync(
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetValidationStatsQuery(), cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(result);
    }

    private static async Task<Ok<PagedResult<ValidationQueueItemDto>>> ListValidationQueueAsync(
        [FromQuery] int page,
        [FromQuery] int pageSize,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var query = new ListValidationQueueQuery
        {
            Page = page,
            PageSize = pageSize > 0 ? Math.Min(pageSize, 100) : 20,
        };

        var result = await mediator.Send(query, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(result);
    }
}
