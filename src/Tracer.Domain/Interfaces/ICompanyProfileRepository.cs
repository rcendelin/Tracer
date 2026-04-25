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
    /// B-95: Same as <see cref="ListByCountryAsync(string, int, CancellationToken)"/>
    /// but with an explicit blocking pre-filter on <c>TraceCount</c>. The entity
    /// resolver uses this to do a cheap first pass over the most business-important
    /// profiles before falling back to a wider search — keeping fuzzy scoring O(small).
    /// </summary>
    /// <param name="country">ISO 3166-1 alpha-2 country code.</param>
    /// <param name="maxCount">Maximum number of profiles to return.</param>
    /// <param name="minTraceCount">Inclusive lower bound on <c>TraceCount</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyCollection<CompanyProfile>> ListByCountryAsync(
        string country,
        int maxCount,
        int minTraceCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the top non-archived profiles across all countries, ordered by
    /// <c>TraceCount</c> descending. Used by the B-79 cache-warming background
    /// service to pre-populate the distributed cache with the hottest profiles.
    /// </summary>
    /// <param name="maxCount">
    /// Maximum number of profiles to return. Caller is expected to cap this
    /// (the cache-warming service caps at 10_000 via <c>CacheWarmingOptions</c>).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyCollection<CompanyProfile>> ListTopByTraceCountAsync(
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
    /// Returns the average overall confidence across profiles. Profiles whose
    /// <c>OverallConfidence</c> has not been set are excluded from the average
    /// (they do not pull the average down). Returns <c>0.0</c> when no profiles
    /// have a confidence value — callers should treat that as "no data".
    /// </summary>
    /// <param name="includeArchived">Whether to include archived profiles. Default: <see langword="false"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<double> GetAverageConfidenceAsync(
        bool includeArchived = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts non-archived company profiles considered candidates for the
    /// re-validation queue. Mirrors the filter used by
    /// <see cref="GetRevalidationQueueAsync"/>; a caller still has to apply
    /// per-field TTL filtering from <c>IFieldTtlPolicy</c>, but this count
    /// is a reasonable upper bound for dashboard purposes and avoids loading
    /// profile payloads just to count them.
    /// </summary>
    Task<int> CountRevalidationCandidatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the average age (in fractional days) of the <see cref="CompanyProfile.LastValidatedAt"/>
    /// timestamp across non-archived profiles. Profiles that have never been
    /// validated fall back to <see cref="CompanyProfile.CreatedAt"/>. Returns
    /// <c>0</c> when there are no non-archived profiles.
    /// </summary>
    /// <param name="now">Reference moment used to compute the age.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<double> AverageDaysSinceLastValidationAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams up to <paramref name="maxRows"/> profiles matching the specified filters,
    /// ordered by <c>TraceCount DESC, CreatedAt DESC</c>. Intended for batch export
    /// (B-81) — caller is responsible for enforcing the absolute row cap.
    /// </summary>
    /// <remarks>
    /// Implementations must use <c>AsNoTracking()</c> and SQL-side <c>Take(maxRows)</c>
    /// so rows are streamed from the reader rather than materialised to a list.
    /// The returned enumerable MUST be consumed within the same DbContext scope.
    /// </remarks>
    /// <param name="maxRows">Absolute row cap (1 ≤ maxRows ≤ 10_000).</param>
    IAsyncEnumerable<CompanyProfile> StreamAsync(
        int maxRows,
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

    /// <summary>
    /// Returns per-country aggregates of non-archived profiles for coverage analytics.
    /// Sums and sample counts are projected from the database; callers compute
    /// averages in-memory so that null samples (missing <c>OverallConfidence</c> /
    /// <c>LastEnrichedAt</c>) do not skew the result.
    /// </summary>
    /// <param name="now">Reference "now" used by the repository to compute per-row data age in days.</param>
    /// <param name="maxCountries">Hard cap on the number of groups returned (DoS guard).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<CoverageByCountryRow>> GetCoverageByCountryAsync(
        DateTimeOffset now,
        int maxCountries,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Per-country aggregate row returned by
/// <see cref="ICompanyProfileRepository.GetCoverageByCountryAsync"/>.
/// </summary>
public sealed record CoverageByCountryRow
{
    public required string Country { get; init; }
    public required int ProfileCount { get; init; }
    public required int ConfidenceSampleCount { get; init; }
    public required double ConfidenceSum { get; init; }
    public required int EnrichedSampleCount { get; init; }
    public required long EnrichedSumDays { get; init; }
}
