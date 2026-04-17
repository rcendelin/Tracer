using System.Security.Cryptography;
using System.Text;
using Tracer.Application.DTOs;
using Tracer.Domain.Entities;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Services;

/// <summary>
/// Resolves entity identity for CKB deduplication.
/// Matching pipeline: RegistrationId+Country → NormalizedKey → fuzzy match (auto ≥ 0.85) →
/// LLM disambiguation (0.70 ≤ score &lt; 0.85) → null (new profile).
/// </summary>
public sealed class EntityResolver : IEntityResolver
{
    /// <summary>Score threshold above which the top fuzzy candidate is auto-matched.</summary>
    private const double HighConfidenceThreshold = 0.85;

    /// <summary>Lower bound for mid-tier candidates escalated to LLM disambiguation (B-64).</summary>
    private const double LowConfidenceThreshold = 0.70;

    /// <summary>Maximum mid-tier candidates sent to the LLM (cost control).</summary>
    private const int MaxLlmCandidates = 5;

    /// <summary>Maximum number of CKB profiles loaded as fuzzy-match candidates per request.</summary>
    private const int MaxFuzzyCandidates = 100;

    private readonly ICompanyProfileRepository _profileRepository;
    private readonly ICompanyNameNormalizer _normalizer;
    private readonly IFuzzyNameMatcher _fuzzyMatcher;
    private readonly ILlmDisambiguator _disambiguator;

    public EntityResolver(
        ICompanyProfileRepository profileRepository,
        ICompanyNameNormalizer normalizer,
        IFuzzyNameMatcher fuzzyMatcher,
        ILlmDisambiguator disambiguator)
    {
        _profileRepository = profileRepository;
        _normalizer = normalizer;
        _fuzzyMatcher = fuzzyMatcher;
        _disambiguator = disambiguator;
    }

    /// <inheritdoc />
    public async Task<CompanyProfile?> ResolveAsync(TraceRequestDto input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        // 1. Exact match: RegistrationId + Country
        if (!string.IsNullOrWhiteSpace(input.RegistrationId) &&
            !string.IsNullOrWhiteSpace(input.Country))
        {
            var profile = await _profileRepository.FindByRegistrationIdAsync(
                input.RegistrationId, input.Country, cancellationToken).ConfigureAwait(false);
            if (profile is not null)
                return profile;

            // Also try by normalized key
            var regKey = $"{input.Country}:{input.RegistrationId}";
            profile = await _profileRepository.FindByKeyAsync(regKey, cancellationToken).ConfigureAwait(false);
            if (profile is not null)
                return profile;
        }

        // 2. Name-based match via NormalizedKey (exact hash)
        if (!string.IsNullOrWhiteSpace(input.CompanyName))
        {
            var nameKey = GenerateNormalizedKey(input.CompanyName, input.Country, input.RegistrationId);
            var profile = await _profileRepository.FindByKeyAsync(nameKey, cancellationToken).ConfigureAwait(false);
            if (profile is not null)
                return profile;
        }

        // 3. Fuzzy match (auto ≥ 0.85) → LLM disambiguation (0.70–0.85) → null
        return await TryFuzzyOrLlmMatchAsync(input, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FuzzyMatchCandidate>> FindCandidatesAsync(
        TraceRequestDto input, int maxCandidates, double minScore, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCandidates, nameof(maxCandidates));

        // Fuzzy search requires both a company name to compare and a country to filter the candidate pool.
        if (string.IsNullOrWhiteSpace(input.CompanyName) || string.IsNullOrWhiteSpace(input.Country))
            return [];

        var scored = await ScoreCandidatesAsync(
            input.CompanyName!, input.Country!, cancellationToken).ConfigureAwait(false);

        return scored
            .Where(c => c.Score >= minScore)
            .OrderByDescending(c => c.Score)
            .Take(maxCandidates)
            .ToList();
    }

    /// <inheritdoc />
    public string GenerateNormalizedKey(string? name, string? country, string? registrationId)
    {
        // Prefer RegistrationId-based key
        if (!string.IsNullOrWhiteSpace(registrationId) && !string.IsNullOrWhiteSpace(country))
            return $"{country.Trim().ToUpperInvariant()}:{registrationId.Trim()}";

        // Fall back to name-based key
        if (!string.IsNullOrWhiteSpace(name))
        {
            var normalized = _normalizer.Normalize(name);
            var hash = ComputeHash(normalized);
            var countryPrefix = !string.IsNullOrWhiteSpace(country)
                ? country.Trim().ToUpperInvariant()
                : "XX";
            return $"NAME:{countryPrefix}:{hash}";
        }

        return $"UNKNOWN:{Guid.NewGuid():N}";
    }

    /// <summary>
    /// Normalizes a company name for deduplication.
    /// Delegates to <see cref="ICompanyNameNormalizer"/> for the enhanced pipeline.
    /// Kept as internal static convenience for backward-compatible test access via a default normalizer.
    /// </summary>
    internal static string NormalizeName(string name) =>
        new CompanyNameNormalizer().Normalize(name);

    // ── Fuzzy + LLM matching ─────────────────────────────────────────────────

    /// <summary>
    /// Loads candidates by country, scores them once, and applies the two-tier match policy:
    /// auto-match at ≥ <see cref="HighConfidenceThreshold"/>, otherwise escalate mid-tier
    /// candidates (≥ <see cref="LowConfidenceThreshold"/>) to the LLM disambiguator.
    /// </summary>
    private async Task<CompanyProfile?> TryFuzzyOrLlmMatchAsync(
        TraceRequestDto input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.CompanyName) || string.IsNullOrWhiteSpace(input.Country))
            return null;

        var scored = await ScoreCandidatesAsync(
            input.CompanyName!, input.Country!, cancellationToken).ConfigureAwait(false);

        if (scored.Count == 0)
            return null;

        var topByScore = scored.OrderByDescending(c => c.Score).ToList();

        // Tier 1: high-confidence auto-match
        var best = topByScore[0];
        if (best.Score >= HighConfidenceThreshold)
            return best.Profile;

        // Tier 2: LLM disambiguation on mid-tier candidates
        var midTier = topByScore
            .Where(c => c.Score >= LowConfidenceThreshold && c.Score < HighConfidenceThreshold)
            .Take(MaxLlmCandidates)
            .ToList();

        if (midTier.Count == 0)
            return null;

        return await _disambiguator
            .PickBestMatchAsync(input.CompanyName!, input.Country, midTier, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<List<FuzzyMatchCandidate>> ScoreCandidatesAsync(
        string companyName, string country, CancellationToken cancellationToken)
    {
        var candidates = await _profileRepository
            .ListByCountryAsync(country, MaxFuzzyCandidates, cancellationToken)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
            return [];

        // Defensive cap: the repository is expected to honour `MaxFuzzyCandidates`, but if a future
        // regression removes that cap, we still refuse to score an unbounded set in memory (DoS guard).
        var bounded = candidates.Count <= MaxFuzzyCandidates
            ? candidates
            : candidates.Take(MaxFuzzyCandidates).ToList();

        var normalizedQuery = _normalizer.Normalize(companyName);
        var scored = new List<FuzzyMatchCandidate>(bounded.Count);

        foreach (var candidate in bounded)
        {
            var candidateName = candidate.LegalName?.Value;
            if (string.IsNullOrWhiteSpace(candidateName))
                continue;

            var normalizedCandidate = _normalizer.Normalize(candidateName);
            var score = _fuzzyMatcher.Score(normalizedQuery, normalizedCandidate);
            scored.Add(new FuzzyMatchCandidate(candidate, score));
        }

        return scored;
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes)[..16]; // First 16 hex chars (64 bits)
    }
}
