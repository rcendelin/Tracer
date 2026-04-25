using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Tracer.Application.DTOs;
using Tracer.Application.Queries.GetChangeTrend;
using Tracer.Application.Queries.GetCoverage;

namespace Tracer.Api.Endpoints;

/// <summary>
/// Minimal API endpoints for aggregate trend analytics.
/// Responses are aggregate-only: no per-profile or per-event rows are returned here.
/// </summary>
internal static class AnalyticsEndpoints
{
    public static RouteGroupBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/analytics")
            .WithTags("Analytics")
            .WithOpenApi();

        group.MapGet("/changes", GetChangeTrendAsync)
            .WithName("GetChangeTrend")
            .WithSummary("Get monthly trend of change events across severities")
            .Produces<ChangeTrendDto>();

        group.MapGet("/coverage", GetCoverageAsync)
            .WithName("GetCoverage")
            .WithSummary("Get CKB coverage aggregated by country")
            .Produces<CoverageDto>();

        return group;
    }

    private static async Task<Ok<ChangeTrendDto>> GetChangeTrendAsync(
        IMediator mediator,
        CancellationToken cancellationToken,
        [FromQuery] TrendPeriod period = TrendPeriod.Monthly,
        [FromQuery] int months = 12)
    {
        var result = await mediator.Send(new GetChangeTrendQuery(period, months), cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(result);
    }

    private static async Task<Ok<CoverageDto>> GetCoverageAsync(
        IMediator mediator,
        CancellationToken cancellationToken,
        [FromQuery] CoverageGroupBy groupBy = CoverageGroupBy.Country)
    {
        var result = await mediator.Send(new GetCoverageQuery(groupBy), cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(result);
    }
}
