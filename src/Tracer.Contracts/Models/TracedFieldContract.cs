namespace Tracer.Contracts.Models;

/// <summary>
/// An enriched field value with provenance metadata.
/// </summary>
/// <typeparam name="T">The type of the enriched field value.</typeparam>
/// <remarks>
/// Every enriched field in Tracer carries its own confidence score and source identifier.
/// This allows FieldForce to decide whether a value is reliable enough to use directly,
/// or whether it should be presented to a human for review.
/// </remarks>
public sealed record TracedFieldContract<T>
{
    /// <summary>The enriched value.</summary>
    public required T Value { get; init; }

    /// <summary>
    /// Confidence score in the range [0.0, 1.0].
    /// <list type="bullet">
    ///   <item><description>≥ 0.9 — high confidence (official registry source)</description></item>
    ///   <item><description>0.7–0.89 — medium confidence (geo API, GLEIF)</description></item>
    ///   <item><description>0.5–0.69 — lower confidence (web scraping)</description></item>
    ///   <item><description>&lt; 0.5 — low confidence (AI extraction)</description></item>
    /// </list>
    /// </summary>
    public required double Confidence { get; init; }

    /// <summary>
    /// Identifier of the enrichment provider that produced this value
    /// (e.g. <c>"ares"</c>, <c>"companies-house"</c>, <c>"gleif-lei"</c>, <c>"google-maps"</c>).
    /// </summary>
    public required string Source { get; init; }

    /// <summary>UTC timestamp when this field was last enriched.</summary>
    public required DateTimeOffset EnrichedAt { get; init; }
}
