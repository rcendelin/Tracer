using Tracer.Domain.Entities;
using Tracer.Domain.Enums;

namespace Tracer.Application.Services;

/// <summary>
/// Central TTL authority for enriched fields. Combines the platform defaults
/// from <see cref="Tracer.Domain.ValueObjects.FieldTtl.For"/> with per-environment
/// overrides in <see cref="FieldTtlOptions"/>.
/// </summary>
/// <remarks>
/// Stateless, thread-safe, registered as Singleton. Used by the re-validation
/// scheduler (B-65) and lightweight / deep modes (B-66 / B-67) to decide which
/// <see cref="CompanyProfile"/> fields need refresh.
/// <para>
/// All time-based methods take an explicit <paramref name="now"/> so callers
/// get a consistent snapshot across successive calls and tests stay
/// deterministic without introducing an <c>IClock</c> abstraction.
/// </para>
/// <para>
/// <see cref="CompanyProfile.NeedsRevalidation"/> is the Domain-level baseline
/// that uses hardcoded defaults and is retained for domain invariants.
/// Application-layer code MUST use this service so configuration overrides
/// take effect.
/// </para>
/// </remarks>
public interface IFieldTtlPolicy
{
    /// <summary>
    /// Returns the effective TTL for the given field: configured override
    /// if present, otherwise <see cref="Tracer.Domain.ValueObjects.FieldTtl.For"/>.
    /// </summary>
    TimeSpan GetTtl(FieldName field);

    /// <summary>
    /// Returns the list of fields whose <c>EnrichedAt</c> timestamp is older
    /// than the configured TTL at <paramref name="now"/>. Only enriched fields
    /// are considered — fields that have never been populated are skipped.
    /// </summary>
    IReadOnlyList<FieldName> GetExpiredFields(CompanyProfile profile, DateTimeOffset now);

    /// <summary>
    /// Returns the earliest future expiration timestamp across all enriched
    /// fields, or <c>null</c> if the profile has no enriched fields. Useful
    /// for scheduling the next re-validation sweep.
    /// </summary>
    /// <remarks>
    /// Already-expired fields return a timestamp in the past (their original
    /// expiration moment). Callers that only care about upcoming work should
    /// filter with <c>result > now</c>.
    /// </remarks>
    DateTimeOffset? GetNextExpirationDate(CompanyProfile profile, DateTimeOffset now);

    /// <summary>
    /// Convenience predicate: returns <c>true</c> when <paramref name="profile"/>
    /// has at least one expired field at <paramref name="now"/>.
    /// </summary>
    bool IsRevalidationDue(CompanyProfile profile, DateTimeOffset now);
}
