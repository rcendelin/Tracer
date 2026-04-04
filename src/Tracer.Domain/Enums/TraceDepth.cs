namespace Tracer.Domain.Enums;

/// <summary>
/// Controls how deeply Tracer enriches a company profile.
/// Maps directly to the waterfall pipeline tiers.
/// </summary>
public enum TraceDepth
{
    /// <summary>CKB cache + fastest APIs only. Target latency ≤5s.</summary>
    Quick = 0,

    /// <summary>Full waterfall through all Tier 1 API sources. Target latency ≤10s.</summary>
    Standard = 1,

    /// <summary>Standard + web scraping + AI extraction. Target latency ≤30s.</summary>
    Deep = 2,
}
