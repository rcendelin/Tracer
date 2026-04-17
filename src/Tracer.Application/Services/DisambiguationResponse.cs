namespace Tracer.Application.Services;

/// <summary>
/// Raw response from <see cref="ILlmDisambiguatorClient.DisambiguateAsync"/> — the LLM's pick
/// of which candidate matches the query, with its self-reported confidence.
/// <para>
/// The downstream <see cref="LlmDisambiguator"/> applies a confidence discount factor (× 0.7)
/// and a 0.5 threshold before accepting the match; this DTO carries the raw values.
/// </para>
/// </summary>
/// <param name="SelectedIndex">
/// Zero-based index into the candidate list, or <c>-1</c> if none are a match.
/// </param>
/// <param name="Confidence">
/// Raw confidence reported by the LLM in <c>[0.0, 1.0]</c>. May be slightly out of range
/// due to model drift — callers must clamp before use.
/// </param>
/// <param name="Reasoning">Optional free-form explanation for audit / debugging.</param>
internal sealed record DisambiguationResponse(
    int SelectedIndex,
    double Confidence,
    string? Reasoning);
