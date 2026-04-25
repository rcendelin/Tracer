using Tracer.Application.DTOs;

namespace Tracer.Application.Services;

/// <summary>
/// Caches enriched company profile DTOs for fast retrieval on Quick depth traces.
/// Backed by <c>IDistributedCache</c> (in-memory for MVP, Redis in Phase 4).
/// </summary>
public interface IProfileCacheService
{
    /// <summary>
    /// Gets a cached profile by its normalized key.
    /// </summary>
    /// <returns>The cached profile DTO, or <see langword="null"/> if not in cache.</returns>
    Task<CompanyProfileDto?> GetAsync(string normalizedKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores or updates a profile in the cache.
    /// </summary>
    Task SetAsync(string normalizedKey, CompanyProfileDto profile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a profile from the cache (invalidation after CKB update).
    /// </summary>
    Task RemoveAsync(string normalizedKey, CancellationToken cancellationToken = default);
}
