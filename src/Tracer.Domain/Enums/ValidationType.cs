namespace Tracer.Domain.Enums;

/// <summary>
/// Distinguishes the two modes of re-validation performed by the background scheduler.
/// </summary>
public enum ValidationType
{
    /// <summary>
    /// Re-checks only expired fields against the primary registry for this company's country.
    /// Fast and low-cost; used when only 1–2 fields have expired TTLs.
    /// </summary>
    Lightweight = 0,

    /// <summary>
    /// Full waterfall re-enrichment across all applicable providers.
    /// Used when ≥3 fields have expired TTLs or when a prior lightweight check detected a discrepancy.
    /// </summary>
    Deep = 1,
}
