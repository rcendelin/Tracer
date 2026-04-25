using MediatR;
using Tracer.Application.DTOs;
using Tracer.Application.Services;
using Tracer.Domain.Entities;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Queries.ListValidationQueue;

/// <summary>
/// Page over the scheduler's re-validation queue, annotating each row with
/// the fields whose TTL has expired according to <see cref="IFieldTtlPolicy"/>.
/// </summary>
/// <remarks>
/// The underlying <see cref="ICompanyProfileRepository.GetRevalidationQueueAsync"/>
/// returns an ordered slice capped at <c>maxCount</c>. We overfetch
/// <c>(page+1) × pageSize</c> profiles and then filter in memory to those that
/// actually have at least one expired field, which keeps the scheduler's view
/// and the dashboard's view consistent without materialising the whole CKB.
/// The <see cref="PagedResult{T}.TotalCount"/> reflects the upper bound of
/// non-archived candidates from the repository — it's an approximation, not an
/// exact count of the expired-field subset, and is documented as such in the
/// implementation plan.
/// </remarks>
public sealed class ListValidationQueueHandler
    : IRequestHandler<ListValidationQueueQuery, PagedResult<ValidationQueueItemDto>>
{
    // Hard ceiling to prevent a pathological page=100/pageSize=100 request from
    // dragging the entire CKB into memory. Keeps the scheduler's sweep semantics
    // intact while bounding the cost of this dashboard read.
    private const int MaxQueueSweep = 500;

    private readonly ICompanyProfileRepository _profiles;
    private readonly IFieldTtlPolicy _ttlPolicy;

    public ListValidationQueueHandler(
        ICompanyProfileRepository profiles,
        IFieldTtlPolicy ttlPolicy)
    {
        _profiles = profiles;
        _ttlPolicy = ttlPolicy;
    }

    public async Task<PagedResult<ValidationQueueItemDto>> Handle(
        ListValidationQueueQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var page = Math.Max(0, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var sweepCap = Math.Min(MaxQueueSweep, (page + 1) * pageSize);

        var candidates = await _profiles
            .GetRevalidationQueueAsync(sweepCap, cancellationToken)
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;

        var expired = new List<(CompanyProfile Profile, IReadOnlyList<Tracer.Domain.Enums.FieldName> Fields)>();
        foreach (var candidate in candidates)
        {
            var fields = _ttlPolicy.GetExpiredFields(candidate, now);
            if (fields.Count > 0)
                expired.Add((candidate, fields));
        }

        var pageSlice = expired
            .Skip(page * pageSize)
            .Take(pageSize)
            .Select(entry => MapToDto(entry.Profile, entry.Fields, now))
            .ToList();

        var totalCount = await _profiles
            .CountRevalidationCandidatesAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<ValidationQueueItemDto>
        {
            Items = pageSlice,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    private ValidationQueueItemDto MapToDto(
        CompanyProfile profile,
        IReadOnlyList<Tracer.Domain.Enums.FieldName> expiredFields,
        DateTimeOffset now)
    {
        var nextExpiry = _ttlPolicy.GetNextExpirationDate(profile, now);

        return new ValidationQueueItemDto
        {
            ProfileId = profile.Id,
            NormalizedKey = profile.NormalizedKey,
            Country = profile.Country,
            RegistrationId = profile.RegistrationId,
            LegalName = profile.LegalName?.Value,
            TraceCount = profile.TraceCount,
            OverallConfidence = profile.OverallConfidence?.Value,
            LastValidatedAt = profile.LastValidatedAt,
            NextFieldExpiryDate = nextExpiry,
            ExpiredFields = expiredFields,
        };
    }
}
