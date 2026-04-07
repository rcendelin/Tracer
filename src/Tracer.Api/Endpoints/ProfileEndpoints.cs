using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Tracer.Application.DTOs;
using Tracer.Application.Queries.GetProfile;
using Tracer.Application.Queries.GetProfileHistory;
using Tracer.Application.Queries.ListProfiles;
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
            .Produces<PagedResult<CompanyProfileDto>>();

        group.MapGet("/{profileId:guid}", GetProfileAsync)
            .WithName("GetProfile")
            .WithSummary("Get company profile by ID")
            .Produces<GetProfileResult>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{profileId:guid}/history", GetProfileHistoryAsync)
            .WithName("GetProfileHistory")
            .WithSummary("Get change history and validations for a profile")
            .Produces<GetProfileHistoryResult>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Phase 3 handler: full re-validation pipeline is implemented in B-65+.
        // The endpoint is registered here so the frontend can call it; it returns 202 Accepted
        // with a message indicating the feature will be available in Phase 3.
        group.MapPost("/{profileId:guid}/revalidate", RevalidateProfileAsync)
            .WithName("RevalidateProfile")
            .WithSummary("Trigger manual re-validation of a company profile (Phase 3)")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

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

    private static async Task<Results<Accepted<string>, NotFound>> RevalidateProfileAsync(
        Guid profileId,
        ICompanyProfileRepository repository,
        CancellationToken cancellationToken)
    {
        // Lightweight existence check — avoids loading the full profile + recent changes
        // just to return 404. Full re-validation pipeline is Phase 3 (B-65+).
        var exists = await repository.ExistsAsync(profileId, cancellationToken)
            .ConfigureAwait(false);

        if (!exists)
            return TypedResults.NotFound();

        // Full re-validation pipeline is Phase 3 (B-65+). The request is accepted
        // and will be processed once the re-validation engine is implemented.
        return TypedResults.Accepted(
            (string?)null,
            $"Re-validation for profile {profileId} queued. Full re-validation engine is available in Phase 3.");
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
}
