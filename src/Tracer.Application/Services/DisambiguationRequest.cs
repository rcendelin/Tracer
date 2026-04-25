namespace Tracer.Application.Services;

/// <summary>
/// Input to <see cref="ILlmDisambiguatorClient.DisambiguateAsync"/> — the original query plus
/// a short list of candidate profiles produced by fuzzy matching that the LLM must rank.
/// </summary>
/// <param name="QueryName">The caller-supplied company name (raw, not normalized).</param>
/// <param name="Country">Optional ISO 3166-1 alpha-2 country code for context.</param>
/// <param name="Candidates">
/// Up to <c>MaxCandidates</c> fuzzy-match candidates, pre-filtered to the ambiguous
/// score range (0.70 ≤ score &lt; 0.85).
/// </param>
internal sealed record DisambiguationRequest(
    string QueryName,
    string? Country,
    IReadOnlyList<FuzzyMatchCandidate> Candidates);
