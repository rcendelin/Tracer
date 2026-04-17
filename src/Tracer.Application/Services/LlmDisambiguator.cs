using Microsoft.Extensions.Logging;
using Tracer.Domain.Entities;

namespace Tracer.Application.Services;

/// <summary>
/// Orchestrates LLM disambiguation: caps candidates, calls the LLM client, applies the
/// confidence discount factor, and enforces the match threshold. Stateless; registered as Scoped.
/// </summary>
internal sealed partial class LlmDisambiguator : ILlmDisambiguator
{
    /// <summary>Maximum candidates passed to the LLM per call (prevents cost blowup).</summary>
    private const int MaxCandidates = 5;

    /// <summary>
    /// Discount factor applied to raw LLM confidence. LLMs are less reliable than registry
    /// evidence, so we cap their maximum contribution at 0.7. Per B-64 spec.
    /// </summary>
    private const double ConfidenceDiscount = 0.7;

    /// <summary>Minimum calibrated confidence for the LLM pick to be accepted as a match.</summary>
    private const double MatchThreshold = 0.5;

    private readonly ILlmDisambiguatorClient _client;
    private readonly ILogger<LlmDisambiguator> _logger;

    public LlmDisambiguator(ILlmDisambiguatorClient client, ILogger<LlmDisambiguator> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CompanyProfile?> PickBestMatchAsync(
        string queryName,
        string? country,
        IReadOnlyList<FuzzyMatchCandidate> candidates,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryName, nameof(queryName));
        ArgumentNullException.ThrowIfNull(candidates, nameof(candidates));

        if (candidates.Count == 0)
            return null;

        // Cap candidates defensively — the LLM cost scales linearly with candidate count.
        var bounded = candidates.Count <= MaxCandidates
            ? candidates
            : candidates.Take(MaxCandidates).ToArray();

        var request = new DisambiguationRequest(queryName, country, bounded);

        var response = await _client
            .DisambiguateAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (response is null)
        {
            // Null means the client is a Null impl (Azure not configured) or the real call failed.
            // Either way, there's no signal — fall back to "no match".
            return null;
        }

        // Per spec, -1 is the ONLY valid negative value — it signals "no candidate matches".
        // Any other negative (or an index ≥ candidate count) is a schema violation: reject defensively.
        if (response.SelectedIndex == -1)
        {
            LogNoMatch(queryName);
            return null;
        }

        if (response.SelectedIndex < 0 || response.SelectedIndex >= bounded.Count)
        {
            LogInvalidIndex(response.SelectedIndex, bounded.Count);
            return null;
        }

        // Clamp raw confidence to [0, 1]. A value materially outside that range signals model drift
        // or a schema violation; log it so operators can monitor prompt/deployment health.
        var rawConfidence = response.Confidence;
        if (rawConfidence < 0.0 || rawConfidence > 1.0)
        {
            LogConfidenceClamped(queryName, rawConfidence);
            rawConfidence = Math.Clamp(rawConfidence, 0.0, 1.0);
        }

        var calibrated = rawConfidence * ConfidenceDiscount;
        if (calibrated < MatchThreshold)
        {
            LogBelowThreshold(queryName, rawConfidence, calibrated);
            return null;
        }

        var selected = bounded[response.SelectedIndex];
        LogMatch(queryName, response.SelectedIndex, rawConfidence, calibrated);
        return selected.Profile;
    }

    // ── Logging ──────────────────────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Information,
        Message = "LLM disambiguation: no match for '{QueryName}' (LLM returned index -1)")]
    private partial void LogNoMatch(string queryName);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "LLM disambiguation: LLM returned out-of-range index {Index} (candidates={Count})")]
    private partial void LogInvalidIndex(int index, int count);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "LLM disambiguation: below threshold for '{QueryName}' (raw={Raw:F2}, calibrated={Calibrated:F2})")]
    private partial void LogBelowThreshold(string queryName, double raw, double calibrated);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "LLM disambiguation: matched '{QueryName}' → candidate {Index} (raw={Raw:F2}, calibrated={Calibrated:F2})")]
    private partial void LogMatch(string queryName, int index, double raw, double calibrated);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "LLM disambiguation: LLM returned out-of-range confidence {Raw:F4} for '{QueryName}'; clamped to [0, 1]")]
    private partial void LogConfidenceClamped(string queryName, double raw);
}
