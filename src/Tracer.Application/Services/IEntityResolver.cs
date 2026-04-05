using Tracer.Application.DTOs;
using Tracer.Domain.Entities;

namespace Tracer.Application.Services;

/// <summary>
/// Resolves whether a trace request matches an existing company profile in CKB,
/// or needs a new profile created. Handles entity deduplication via normalized keys.
/// </summary>
public interface IEntityResolver
{
    /// <summary>
    /// Attempts to find an existing company profile matching the input.
    /// </summary>
    /// <returns>The existing profile, or <see langword="null"/> if no match (new company).</returns>
    Task<CompanyProfile?> ResolveAsync(TraceRequestDto input, CancellationToken cancellationToken);

    /// <summary>
    /// Generates a normalized lookup key for deduplication.
    /// </summary>
    string GenerateNormalizedKey(string? name, string? country, string? registrationId);
}
