using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
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
            .Produces<TraceResultDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapGet("/{traceId:guid}", GetTraceResultAsync)
            .WithName("GetTraceResult")
            .WithSummary("Get trace result by ID")
            .Produces<TraceResultDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", ListTracesAsync)
            .WithName("ListTraces")
            .WithSummary("List trace requests with filters")
            .Produces<PagedResult<TraceResultDto>>();

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
