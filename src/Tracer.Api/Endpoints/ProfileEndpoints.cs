using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Tracer.Api.Middleware;
using Tracer.Application.Commands.OverrideField;
using Tracer.Application.DTOs;
using Tracer.Application.Queries.GetProfile;
using Tracer.Application.Queries.GetProfileHistory;
using Tracer.Application.Queries.ListProfiles;
using Tracer.Application.Services;
using Tracer.Application.Services.Export;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;

namespace Tracer.Api.Endpoints;

/// <summary>
/// Minimal API endpoints for CKB company profile operations.
/// </summary>
internal static class ProfileEndpoints
{
    public static RouteGroupBuilder MapProfileEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/profiles")
            .WithTags("Profiles")
            .WithOpenApi();

        group.MapGet("/", ListProfilesAsync)
            .WithName("ListProfiles")
            .WithSummary("List CKB company profiles")
            .WithDescription("Paged listing of Company Knowledge Base profiles. Supports free-text search (on RegistrationId / NormalizedKey), country filter, confidence range and archived-profile toggle.")
            .Produces<PagedResult<CompanyProfileDto>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/{profileId:guid}", GetProfileAsync)
            .WithName("GetProfile")
            .WithSummary("Get company profile by ID")
            .WithDescription("Returns the golden record including per-field enrichment metadata (source, confidence, EnrichedAt).")
            .Produces<GetProfileResult>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{profileId:guid}/history", GetProfileHistoryAsync)
            .WithName("GetProfileHistory")
            .WithSummary("Get change history and validations for a profile")
            .WithDescription("Paged change events plus validation records. Change events carry severity classification used for notification routing.")
            .Produces<GetProfileHistoryResult>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // B-65: enqueues the profile into IRevalidationQueue, drained by RevalidationScheduler.
        // The actual lightweight/deep pipeline is implemented in B-66/B-67.
        group.MapPost("/{profileId:guid}/revalidate", RevalidateProfileAsync)
            .WithName("RevalidateProfile")
            .WithSummary("Enqueue a CKB profile for immediate re-validation")
            .WithDescription("Submits the profile into the bounded revalidation queue drained by RevalidationScheduler. Returns 429 if the queue is full.")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        // B-81: Batch export. Rate-limited (10 req/min/IP) because the worst case writes
        // up to 10 000 rows to the response body.
        group.MapGet("/export", ExportProfilesAsync)
            .WithName("ExportProfiles")
            .WithSummary("Stream CKB profiles as CSV or XLSX")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .RequireRateLimiting("export");

        // B-85: Manual field override with full audit trail via ChangeEvent
        // (ChangeType = ManualOverride, DetectedBy carries caller fingerprint).
        group.MapPut("/{profileId:guid}/fields/{field}", OverrideFieldAsync)
            .WithName("OverrideProfileField")
            .WithSummary("Manually override a single string-typed CKB field with audit")
            .WithDescription("Operator-driven correction for fields that automated enrichment cannot reach (e.g. Phone, Email, EntityStatus). Caller identity is server-derived from the API key; the request body never carries it. Returns 204 on success / no-change, 400 for unsupported fields, 404 for unknown profiles. The change appears in GET /api/profiles/{id}/history with ChangeType = ManualOverride.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    /// <summary>
    /// Body shape for <c>PUT /api/profiles/{id}/fields/{field}</c>. Caller
    /// identity is intentionally absent from the body — it is read server-side
    /// from <see cref="ApiKeyAuthMiddleware.CallerFingerprintItemKey"/>.
    /// </summary>
    public sealed record OverrideFieldRequest(string Value, string Reason);

    private static async Task<Ok<PagedResult<CompanyProfileDto>>> ListProfilesAsync(
        [FromQuery] int page,
        [FromQuery] int pageSize,
        [FromQuery] string? search,
        [FromQuery] string? country,
        [FromQuery] double? minConfidence,
        [FromQuery] double? maxConfidence,
        [FromQuery] DateTimeOffset? validatedBefore,
        [FromQuery] bool includeArchived,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var query = new ListProfilesQuery
        {
            Page = page,
            PageSize = pageSize > 0 ? Math.Min(pageSize, 100) : 20,
            Search = search,
            Country = country,
            MinConfidence = minConfidence,
            MaxConfidence = maxConfidence,
            ValidatedBefore = validatedBefore,
            IncludeArchived = includeArchived,
        };

        var result = await mediator.Send(query, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<GetProfileResult>, NotFound>> GetProfileAsync(
        Guid profileId,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetProfileQuery(profileId), cancellationToken)
            .ConfigureAwait(false);

        return result is not null
            ? TypedResults.Ok(result)
            : TypedResults.NotFound();
    }

    private static async Task<Results<Accepted<string>, NotFound, ProblemHttpResult>> RevalidateProfileAsync(
        Guid profileId,
        ICompanyProfileRepository repository,
        IRevalidationQueue queue,
        CancellationToken cancellationToken)
    {
        // Existence check avoids enqueuing phantom IDs that would later log a missing-profile
        // warning in the scheduler.
        var exists = await repository.ExistsAsync(profileId, cancellationToken)
            .ConfigureAwait(false);

        if (!exists)
            return TypedResults.NotFound();

        var enqueued = await queue.TryEnqueueAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (!enqueued)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Revalidation queue full",
                detail: "The re-validation queue is at capacity. Retry in a moment.");
        }

        return TypedResults.Accepted(
            (string?)null,
            $"Re-validation for profile {profileId} queued.");
    }

    private static async Task<IResult> ExportProfilesAsync(
        HttpContext http,
        ICompanyProfileExporter exporter,
        [FromQuery] string? format,
        [FromQuery] string? search,
        [FromQuery] string? country,
        [FromQuery] double? minConfidence,
        [FromQuery] double? maxConfidence,
        [FromQuery] DateTimeOffset? validatedBefore,
        [FromQuery] bool includeArchived,
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

        if (!string.IsNullOrEmpty(country) && country.Length != 2)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid country",
                detail: "Country must be a 2-letter ISO 3166-1 alpha-2 code.");
        }

        if (minConfidence is < 0 or > 1 || maxConfidence is < 0 or > 1)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid confidence range",
                detail: "Confidence values must be within [0, 1].");
        }

        if (minConfidence.HasValue && maxConfidence.HasValue && minConfidence > maxConfidence)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid confidence range",
                detail: "minConfidence must be less than or equal to maxConfidence.");
        }

        if (maxRows is < 1 or > ExportLimits.MaxRows)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid maxRows",
                detail: $"maxRows must be within [1, {ExportLimits.MaxRows}].");
        }

        var filter = new CompanyProfileExportFilter
        {
            MaxRows = ExportLimits.Clamp(maxRows),
            Search = search,
            Country = country?.ToUpperInvariant(),
            MinConfidence = minConfidence,
            MaxConfidence = maxConfidence,
            ValidatedBefore = validatedBefore,
            IncludeArchived = includeArchived,
        };

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var extension = ExportFormatParser.FileExtension(parsedFormat);
        var fileName = $"tracer-profiles-{timestamp}.{extension}";
        http.Response.ContentType = ExportFormatParser.ContentType(parsedFormat);
        http.Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";

        if (parsedFormat == ExportFormat.Csv)
        {
            // Disable response buffering so CSV rows flush to the client as they are written.
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

    private static async Task<Results<Ok<GetProfileHistoryResult>, NotFound>> GetProfileHistoryAsync(
        Guid profileId,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var query = new GetProfileHistoryQuery
        {
            ProfileId = profileId,
            Page = page,
            PageSize = pageSize > 0 ? Math.Min(pageSize, 100) : 20,
        };

        var result = await mediator.Send(query, cancellationToken).ConfigureAwait(false);

        return result is not null
            ? TypedResults.Ok(result)
            : TypedResults.NotFound();
    }

    private static async Task<IResult> OverrideFieldAsync(
        [FromRoute] Guid profileId,
        [FromRoute] string field,
        [FromBody] OverrideFieldRequest body,
        IMediator mediator,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (body is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Body required",
                detail: "Request body must contain { value, reason }.");
        }

        if (!Enum.TryParse<FieldName>(field, ignoreCase: true, out var parsedField)
            || !Enum.IsDefined(typeof(FieldName), parsedField))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Unknown field",
                detail: $"'{field}' is not a recognised FieldName.");
        }

        // Caller fingerprint is server-derived (set by ApiKeyAuthMiddleware on
        // successful auth). The body NEVER carries caller identity — that
        // would be untrusted input (audit trail integrity, B-85 plan §3.3).
        var fingerprint = http.Items[ApiKeyAuthMiddleware.CallerFingerprintItemKey] as string;
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Caller fingerprint unavailable",
                detail: "Manual override requires an authenticated API key.");
        }

        var label = http.Items[ApiKeyAuthMiddleware.CallerLabelItemKey] as string;

        var outcome = await mediator.Send(
            new OverrideFieldCommand(
                ProfileId: profileId,
                Field: parsedField,
                NewValue: body.Value ?? string.Empty,
                Reason: body.Reason ?? string.Empty,
                CallerFingerprint: fingerprint,
                CallerLabel: label),
            cancellationToken).ConfigureAwait(false);

        return outcome switch
        {
            OverrideFieldResult.Overridden => TypedResults.NoContent(),
            OverrideFieldResult.NoChange => TypedResults.NoContent(),
            OverrideFieldResult.ProfileNotFound => TypedResults.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Profile not found",
                detail: $"No company profile with id {profileId}."),
            OverrideFieldResult.FieldNotOverridable => TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Field not overridable",
                detail: $"Field '{parsedField}' cannot be overridden via this endpoint. Address / Location / Officers / RegistrationId are out of scope."),
            _ => TypedResults.Problem(statusCode: StatusCodes.Status500InternalServerError),
        };
    }
}
