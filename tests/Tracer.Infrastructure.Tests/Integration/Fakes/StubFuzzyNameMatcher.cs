using Tracer.Application.Services;

namespace Tracer.Infrastructure.Tests.Integration.Fakes;

/// <summary>
/// Deterministic <see cref="IFuzzyNameMatcher"/> used by E2E tests for the entity
/// resolution flow. Returns scores from a configurable lookup so tests don't have to
/// fight the real Jaro-Winkler / Jaccard thresholds and the upstream normalizer.
/// </summary>
/// <remarks>
/// Callers register a score indexed by the candidate's normalized name (second argument
/// to <see cref="Score"/>). Unknown candidates return <see cref="DefaultScore"/>.
/// </remarks>
internal sealed class StubFuzzyNameMatcher : IFuzzyNameMatcher
{
    private readonly Dictionary<string, double> _scoresByNormalizedCandidate =
        new(StringComparer.Ordinal);

    /// <summary>Score returned for any candidate not in <see cref="SetScore"/>.</summary>
    public double DefaultScore { get; init; }

    public StubFuzzyNameMatcher SetScore(string normalizedCandidate, double score)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedCandidate);
        if (score is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(score), score, "Score must be in [0, 1].");

        _scoresByNormalizedCandidate[normalizedCandidate] = score;
        return this;
    }

    public double Score(string normalizedName1, string normalizedName2)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedName1);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedName2);

        // Look up by candidate (name2) first — tests key on candidate-specific scores.
        if (_scoresByNormalizedCandidate.TryGetValue(normalizedName2, out var score))
            return score;
        if (_scoresByNormalizedCandidate.TryGetValue(normalizedName1, out score))
            return score;
        return DefaultScore;
    }
}
