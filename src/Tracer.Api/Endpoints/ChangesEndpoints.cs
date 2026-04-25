using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Tracer.Application.DTOs;
using Tracer.Application.Queries.GetChangeStats;
using Tracer.Application.Queries.ListChanges;
using Tracer.Application.Services.Export;
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
            .WithDescription("Reverse-chronological feed of detected field changes. Filter by profile or severity (Critical, Major, Minor, Cosmetic).")
            .Produces<PagedResult<ChangeEventDto>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/stats", GetChangeStatsAsync)
            .WithName("GetChangeStats")
            .WithSummary("Get aggregated change event counts by severity")
            .WithDescription("Totals across the entire change-event history. Used by the Tracer UI change-feed header.")
            .Produces<ChangeStatsDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        // B-81: Batch export of change events (CSV / XLSX), capped at 10 000 rows.
        group.MapGet("/export", ExportChangesAsync)
            .WithName("ExportChanges")
            .WithSummary("Stream change events as CSV or XLSX")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .RequireRateLimiting("export");

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

    private static async Task<IResult> ExportChangesAsync(
        HttpContext http,
        IChangeEventExporter exporter,
        [FromQuery] string? format,
        [FromQuery] ChangeSeverity? severity,
        [FromQuery] Guid? profileId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int? maxRows,
        CancellationToken cancellationToken)
    {
        if (!ExportFormatParser.TryParse(format, out var parsedFormat))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Unsupported export format",
                detail: "Supported values: 'csv', 'xlsx'.");
        }

        if (from.HasValue && to.HasValue && from >= to)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid date range",
                detail: "'from' must be strictly less than 'to'.");
        }

        if (maxRows is < 1 or > ExportLimits.MaxRows)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid maxRows",
                detail: $"maxRows must be within [1, {ExportLimits.MaxRows}].");
        }

        var filter = new ChangeEventExportFilter
        {
            MaxRows = ExportLimits.Clamp(maxRows),
            Severity = severity,
            ProfileId = profileId,
            From = from,
            To = to,
        };

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var extension = ExportFormatParser.FileExtension(parsedFormat);
        var fileName = $"tracer-changes-{timestamp}.{extension}";
        http.Response.ContentType = ExportFormatParser.ContentType(parsedFormat);
        http.Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";

        if (parsedFormat == ExportFormat.Csv)
        {
            var bodyFeature = http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>();
            bodyFeature?.DisableBuffering();
            await exporter.WriteCsvAsync(http.Response.Body, filter, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await exporter.WriteXlsxAsync(http.Response.Body, filter, cancellationToken).ConfigureAwait(false);
        }

        return TypedResults.Empty;
    }
}
