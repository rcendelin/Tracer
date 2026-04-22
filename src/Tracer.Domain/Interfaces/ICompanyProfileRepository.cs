using Tracer.Domain.Entities;

namespace Tracer.Domain.Interfaces;

/// <summary>
/// Repository for the <see cref="CompanyProfile"/> aggregate root (CKB).
/// </summary>
public interface ICompanyProfileRepository
{
    /// <summary>
    /// Finds a company profile by its normalised lookup key.
    /// </summary>
    /// <param name="normalizedKey">The unique key, e.g. <c>"CZ:12345678"</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The profile, or <see langword="null"/> if not found.</returns>
    Task<CompanyProfile?> FindByKeyAsync(string normalizedKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a company profile by registration ID and country.
    /// </summary>
    /// <param name="registrationId">Business registration ID (e.g. IČO).</param>
    /// <param name="country">ISO 3166-1 alpha-2 country code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The profile, or <see langword="null"/> if not found.</returns>
    Task<CompanyProfile?> FindByRegistrationIdAsync(string registrationId, string country, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new profile or updates an existing one.
    /// </summary>
    /// <param name="profile">The profile to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpsertAsync(CompanyProfile profile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets profiles that need re-validation, ordered by priority (TraceCount descending, oldest first).
    /// </summary>
    /// <param name="maxCount">Maximum number of profiles to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Profiles that have at least one expired field TTL.</returns>
    Task<IReadOnlyCollection<CompanyProfile>> GetRevalidationQueueAsync(int maxCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a company profile by its ID.
    /// </summary>
    Task<CompanyProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists company profiles with pagination and optional filters.
    /// </summary>
    /// <param name="page">Zero-based page index.</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <param name="search">Optional free-text search against registration ID and legal name.</param>
    /// <param name="country">Optional country code filter.</param>
    /// <param name="minConfidence">Optional minimum overall confidence filter.</param>
    /// <param name="maxConfidence">Optional maximum overall confidence filter.</param>
    /// <param name="validatedBefore">Optional filter: profiles not validated since this date.</param>
    /// <param name="includeArchived">Whether to include archived profiles. Default: false.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyCollection<CompanyProfile>> ListAsync(
        int page, int pageSize,
        string? search = null,
        string? country = null,
        double? minConfidence = null,
        double? maxConfidence = null,
        DateTimeOffset? validatedBefore = null,
        bool includeArchived = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns <see langword="true"/> if a company profile with the specified ID exists.
    /// </summary>
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists non-archived company profiles for a given country, capped at <paramref name="maxCount"/>.
    /// Ordered by <c>TraceCount</c> descending so that business-important profiles are prioritised
    /// as fuzzy-match candidates.
    /// </summary>
    /// <param name="country">ISO 3166-1 alpha-2 country code.</param>
    /// <param name="maxCount">Maximum number of profiles to return. Caller is expected to cap this (e.g. ≤ 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyCollection<CompanyProfile>> ListByCountryAsync(
        string country,
        int maxCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts company profiles matching the specified filters.
    /// </summary>
    Task<int> CountAsync(
        string? search = null,
        string? country = null,
        double? minConfidence = null,
        double? maxConfidence = null,
        DateTimeOffset? validatedBefore = null,
        bool includeArchived = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Archives non-archived profiles whose <c>LastEnrichedAt</c> is strictly
    /// before <paramref name="enrichedBefore"/> and whose <c>TraceCount</c>
    /// is at most <paramref name="maxTraceCount"/>. Profiles with
    /// <c>LastEnrichedAt == null</c> are never archived — without an
    /// enrichment timestamp we cannot reason about age.
    /// </summary>
    /// <remarks>
    /// Implementations must update a bounded batch of rows in a single
    /// round-trip (no per-row domain event dispatch). The archival policy
    /// is monotonic — running twice is idempotent; a returned count of 0
    /// means the CKB is already in the steady state.
    /// </remarks>
    /// <param name="enrichedBefore">
    /// Rows with <c>LastEnrichedAt &lt; enrichedBefore</c> become candidates.
    /// </param>
    /// <param name="maxTraceCount">Inclusive upper bound on <c>TraceCount</c>.</param>
    /// <param name="batchSize">Maximum rows archived per call.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of rows that transitioned from active to archived.</returns>
    Task<int> ArchiveStaleAsync(
        DateTimeOffset enrichedBefore,
        int maxTraceCount,
        int batchSize,
        CancellationToken cancellationToken = default);
}
