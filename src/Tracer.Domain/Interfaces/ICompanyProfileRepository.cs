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
}
