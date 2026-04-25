using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Tracer.Application.Queries.GetDashboardStats;

namespace Tracer.Api.Endpoints;

/// <summary>
/// Minimal API endpoints for dashboard statistics.
/// </summary>
internal static class StatsEndpoints
{
    public static RouteGroupBuilder MapStatsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/stats")
            .WithTags("Stats")
            .WithOpenApi();

        group.MapGet("/", GetDashboardStatsAsync)
            .WithName("GetDashboardStats")
            .WithSummary("Get dashboard statistics")
            .WithDescription("Rolling KPIs used by the Tracer UI landing page: traces today / this week, total CKB profiles, mean confidence.")
            .Produces<DashboardStatsDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return group;
    }

    private static async Task<Ok<DashboardStatsDto>> GetDashboardStatsAsync(
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetDashboardStatsQuery(), cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(result);
    }
}
