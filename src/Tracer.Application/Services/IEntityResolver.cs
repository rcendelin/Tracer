using Tracer.Application.DTOs;
using Tracer.Domain.Entities;

namespace Tracer.Application.Services;

/// <summary>
/// Resolves whether a trace request matches an existing company profile in CKB,
/// or needs a new profile created. Handles entity deduplication via normalized keys
/// and fuzzy name matching.
/// </summary>
public interface IEntityResolver
{
    /// <summary>
    /// Attempts to find an existing company profile matching the input.
    /// <para>
    /// Matching pipeline:
    /// <list type="number">
    ///   <item>Exact <c>RegistrationId</c>+<c>Country</c></item>
    ///   <item>Exact <c>NormalizedKey</c> ("{Country}:{RegistrationId}")</item>
    ///   <item>Exact <c>NormalizedKey</c> hash of the normalized name</item>
    ///   <item>Fuzzy name match at score ≥ 0.85 (auto-match)</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <returns>The existing profile, or <see langword="null"/> if no match (new company).</returns>
    Task<CompanyProfile?> ResolveAsync(TraceRequestDto input, CancellationToken cancellationToken);

    /// <summary>
    /// Generates a normalized lookup key for deduplication.
    /// </summary>
    string GenerateNormalizedKey(string? name, string? country, string? registrationId);

    /// <summary>
    /// Finds candidate profiles for a trace request by fuzzy name similarity, sorted by score descending.
    /// Used by downstream disambiguation (e.g. B-64 LLM) when no high-confidence auto-match exists.
    /// </summary>
    /// <param name="input">The trace request. Requires <c>CompanyName</c> and <c>Country</c>; otherwise returns empty.</param>
    /// <param name="maxCandidates">Maximum number of candidates to return (e.g. 5).</param>
    /// <param name="minScore">Minimum combined score (e.g. 0.70) — candidates below this are rejected.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Candidates sorted by score descending. Empty if country or name missing.</returns>
    Task<IReadOnlyList<FuzzyMatchCandidate>> FindCandidatesAsync(
        TraceRequestDto input,
        int maxCandidates,
        double minScore,
        CancellationToken cancellationToken);
}
