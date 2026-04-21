using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Tracer.Application.DTOs;
using Tracer.Application.Queries.GetProfile;
using Tracer.Application.Queries.GetProfileHistory;
using Tracer.Application.Queries.ListProfiles;
using Tracer.Application.Services;
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

        // B-65: enqueues the profile into IRevalidationQueue, drained by RevalidationScheduler.
        // The actual lightweight/deep pipeline is implemented in B-66/B-67.
        group.MapPost("/{profileId:guid}/revalidate", RevalidateProfileAsync)
            .WithName("RevalidateProfile")
            .WithSummary("Enqueue a CKB profile for immediate re-validation")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

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
