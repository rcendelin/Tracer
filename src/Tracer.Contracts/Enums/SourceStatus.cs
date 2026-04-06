namespace Tracer.Contracts.Enums;

/// <summary>
/// Outcome of a single enrichment provider execution.
/// Reported per-provider in <see cref="Models.SourceResultContract"/> within <see cref="Messages.TraceResponseMessage"/>.
/// </summary>
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

    /// <summary>Provider was not invoked because it cannot handle this request (e.g. wrong country).</summary>
    Skipped = 5,
}
