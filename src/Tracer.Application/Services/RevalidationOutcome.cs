namespace Tracer.Application.Services;

/// <summary>
/// Outcome of a single re-validation pass performed by
/// <see cref="IRevalidationRunner"/>. Used by the scheduler for
/// metrics and structured logging.
/// </summary>
public enum RevalidationOutcome
{
    /// <summary>
    /// Runner chose not to act (e.g. no expired fields, or the
    /// lightweight/deep pipeline has not yet been implemented in
    /// the current phase). The profile's <c>LastValidatedAt</c> is
    /// not touched.
    /// </summary>
    Deferred = 0,

    /// <summary>Lightweight re-check completed (B-66).</summary>
    Lightweight = 1,

    /// <summary>Full waterfall re-enrichment completed (B-67).</summary>
    Deep = 2,

    /// <summary>Runner raised an error; see logs for details.</summary>
    Failed = 3,
}
