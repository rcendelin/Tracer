namespace Tracer.Contracts.Enums;

/// <summary>
/// Controls how deeply Tracer enriches a company profile.
/// </summary>
/// <remarks>
/// Higher depth means more providers are called and more data is collected,
/// but at the cost of increased latency.
/// </remarks>
public enum TraceDepth
{
    /// <summary>
    /// CKB cache + fastest registry APIs only.
    /// Target latency: &lt;5 seconds.
    /// Use for real-time UI lookups where freshness is acceptable.
    /// </summary>
    Quick = 0,

    /// <summary>
    /// Full waterfall through all Tier 1 API sources (registries, geo, GLEIF).
    /// Target latency: &lt;10 seconds.
    /// Recommended default for most FieldForce enrichment scenarios.
    /// </summary>
    Standard = 1,

    /// <summary>
    /// Standard + web scraping + AI extraction from unstructured content.
    /// Target latency: &lt;30 seconds.
    /// Use when maximum data completeness is required (e.g. lead qualification).
    /// </summary>
    Deep = 2,
}
