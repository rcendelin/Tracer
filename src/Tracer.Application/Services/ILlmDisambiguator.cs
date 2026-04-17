using Tracer.Domain.Entities;

namespace Tracer.Application.Services;

/// <summary>
/// Disambiguates ambiguous fuzzy-match candidates (fuzzy score 0.70–0.85) using an LLM.
/// Called from <see cref="IEntityResolver.ResolveAsync"/> as a tie-breaker between character-level
/// similarity and semantic identity.
/// </summary>
public interface ILlmDisambiguator
{
    /// <summary>
    /// Picks the best-matching candidate for <paramref name="queryName"/>, or returns
    /// <see langword="null"/> if the LLM judges none of the candidates to be a real match.
    /// </summary>
    /// <param name="queryName">The caller-supplied company name (non-empty).</param>
    /// <param name="country">Optional ISO 3166-1 alpha-2 country code for LLM context.</param>
    /// <param name="candidates">
    /// Mid-tier fuzzy candidates from <see cref="IEntityResolver.FindCandidatesAsync"/>.
    /// Implementations cap the list at a small N (e.g. 5) before calling the LLM.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The chosen candidate's <see cref="CompanyProfile"/>, or <see langword="null"/> if:
    /// <list type="bullet">
    ///   <item>The candidate list is empty.</item>
    ///   <item>The underlying LLM client returned <see langword="null"/>
    ///         (disabled or error).</item>
    ///   <item>The LLM returned <c>index == -1</c> (no match).</item>
    ///   <item>The calibrated confidence (raw × 0.7) is below the 0.5 match threshold.</item>
    /// </list>
    /// </returns>
    Task<CompanyProfile?> PickBestMatchAsync(
        string queryName,
        string? country,
        IReadOnlyList<FuzzyMatchCandidate> candidates,
        CancellationToken cancellationToken);
}
