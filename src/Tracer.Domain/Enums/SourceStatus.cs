namespace Tracer.Domain.Enums;

/// <summary>
/// Outcome of a single enrichment provider execution for a given trace request.
/// Stored in a <c>SourceResult</c> entity for per-provider audit and analytics.
/// </summary>
/// <remarks>
/// <see cref="Unknown"/> is the default (<c>0</c>) to ensure that an uninitialised
/// or incorrectly deserialised status is distinguishable from a genuine success or
/// failure outcome (fail-safe default, CWE-1188).
/// </remarks>
public enum SourceStatus
{
    /// <summary>Status not yet determined or could not be mapped from provider response.</summary>
    Unknown = 0,

    /// <summary>Provider returned data and at least one field was enriched.</summary>
    Success = 1,

    /// <summary>Provider was called but found no matching company.</summary>
    NotFound = 2,

    /// <summary>Provider returned an error response (HTTP 4xx/5xx or parsing failure).</summary>
    Error = 3,

    /// <summary>Provider exceeded its configured timeout budget.</summary>
    Timeout = 4,

    /// <summary>Provider was not invoked because <c>CanHandle</c> returned false for this request.</summary>
    Skipped = 5,
}
