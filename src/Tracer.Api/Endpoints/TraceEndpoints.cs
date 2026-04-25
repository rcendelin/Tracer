using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Tracer.Application.Commands.SubmitBatchTrace;
using Tracer.Application.Commands.SubmitTrace;
using Tracer.Application.DTOs;
using Tracer.Application.Queries.GetTraceResult;
using Tracer.Application.Queries.ListTraces;
using Tracer.Domain.Enums;

namespace Tracer.Api.Endpoints;

/// <summary>
/// Minimal API endpoints for trace operations.
/// </summary>
internal static class TraceEndpoints
{
    public static RouteGroupBuilder MapTraceEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/trace")
            .WithTags("Trace")
            .WithOpenApi();

        group.MapPost("/", SubmitTraceAsync)
            .WithName("SubmitTrace")
            .WithSummary("Submit an enrichment request")
            .WithDescription("Runs the waterfall synchronously and returns the enriched profile with per-field confidence scores. Depth controls provider coverage (Quick ≤ 5 s, Standard ≤ 15 s, Deep ≤ 30 s).")
            .Produces<TraceResultDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapGet("/{traceId:guid}", GetTraceResultAsync)
            .WithName("GetTraceResult")
            .WithSummary("Get trace result by ID")
            .WithDescription("Poll endpoint for async (batch) submissions; also returns the stored result for sync traces.")
            .Produces<TraceResultDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", ListTracesAsync)
            .WithName("ListTraces")
            .WithSummary("List trace requests with filters")
            .WithDescription("Paged listing; filter by status, date range or free-text search over the original input.")
            .Produces<PagedResult<TraceResultDto>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/batch", SubmitBatchTraceAsync)
            .WithName("SubmitBatchTrace")
            .WithSummary("Submit a batch of enrichment requests for async processing via Service Bus")
            .WithDescription("Persists all items in a single transaction, then publishes each to the `tracer-request` queue. Returns 202 with per-item TraceIds; callers poll GET /api/trace/{traceId}. Rate-limited: 5 req/min/IP.")
            .Produces<BatchTraceResultDto>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .RequireRateLimiting("batch");

        return group;
    }

    private static async Task<Results<Created<TraceResultDto>, ValidationProblem>> SubmitTraceAsync(
        [FromBody] TraceRequestDto input,
        [FromQuery] string? source,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        try
        {
            var command = new SubmitTraceCommand
            {
                Input = input,
                Source = source ?? "rest-api",
            };

            var result = await mediator.Send(command, cancellationToken).ConfigureAwait(false);

            return TypedResults.Created($"/api/trace/{result.TraceId}", result);
        }
        catch (ValidationException ex)
        {
            var errors = ex.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.ErrorMessage).ToArray());

            return TypedResults.ValidationProblem(errors);
        }
    }

    private static async Task<Results<Ok<TraceResultDto>, NotFound>> GetTraceResultAsync(
        Guid traceId,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetTraceResultQuery(traceId), cancellationToken)
            .ConfigureAwait(false);

        return result is not null
            ? TypedResults.Ok(result)
            : TypedResults.NotFound();
    }

    private static async Task<Results<Accepted<BatchTraceResultDto>, ValidationProblem>> SubmitBatchTraceAsync(
        [FromBody] IReadOnlyCollection<TraceRequestDto> items,
        [FromQuery] string? source,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        try
        {
            var command = new SubmitBatchTraceCommand
            {
                Items = items,
                Source = source ?? "rest-api-batch",
            };

            var result = await mediator.Send(command, cancellationToken).ConfigureAwait(false);

            return TypedResults.Accepted("/api/trace/batch", result);
        }
        catch (ValidationException ex)
        {
            var errors = ex.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.ErrorMessage).ToArray());

            return TypedResults.ValidationProblem(errors);
        }
    }

    private static async Task<Ok<PagedResult<TraceResultDto>>> ListTracesAsync(
        [FromQuery] int page,
        [FromQuery] int pageSize,
        [FromQuery] TraceStatus? status,
        [FromQuery] DateTimeOffset? dateFrom,
        [FromQuery] DateTimeOffset? dateTo,
        [FromQuery] string? search,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var query = new ListTracesQuery
        {
            Page = page,
            PageSize = pageSize > 0 ? Math.Min(pageSize, 100) : 20,
            Status = status,
            From = dateFrom,
            To = dateTo,
            Search = search,
        };

        var result = await mediator.Send(query, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(result);
    }
}
