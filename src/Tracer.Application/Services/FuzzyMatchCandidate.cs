using Tracer.Domain.Entities;

namespace Tracer.Application.Services;

/// <summary>
/// A candidate match from fuzzy name search with its similarity score.
/// Produced by <see cref="IEntityResolver.FindCandidatesAsync"/>; consumed by
/// B-64 LLM disambiguation to pick the best match from ambiguous candidates.
/// </summary>
/// <param name="Profile">The candidate <see cref="CompanyProfile"/> from CKB.</param>
/// <param name="Score">
/// Combined fuzzy score in [0.0, 1.0] from <see cref="IFuzzyNameMatcher"/>.
/// ≥ 0.85 indicates a high-confidence auto-match, 0.70–0.85 indicates candidates
/// that warrant LLM disambiguation, &lt; 0.70 are rejected.
/// </param>
public sealed record FuzzyMatchCandidate(CompanyProfile Profile, double Score);
