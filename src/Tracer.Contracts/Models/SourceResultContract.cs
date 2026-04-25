using Tracer.Contracts.Enums;

namespace Tracer.Contracts.Models;

/// <summary>
/// Execution result for a single enrichment provider within a trace request.
/// Included in <see cref="Messages.TraceResponseMessage"/> for diagnostics and SLA monitoring.
/// </summary>
public sealed record SourceResultContract
{
    /// <summary>
    /// Provider identifier (e.g. <c>"ares"</c>, <c>"companies-house"</c>,
    /// <c>"gleif-lei"</c>, <c>"google-maps"</c>, <c>"web-scraper"</c>, <c>"ai-extractor"</c>).
    /// </summary>
    public required string ProviderId { get; init; }

    /// <summary>Outcome of this provider call.</summary>
    public required SourceStatus Status { get; init; }

    /// <summary>Number of fields that were successfully enriched by this provider.</summary>
    public int FieldsEnriched { get; init; }

    /// <summary>Wall-clock time this provider took to execute, in milliseconds.</summary>
    public long DurationMs { get; init; }

    /// <summary>
    /// Generic error message if <see cref="Status"/> is <see cref="SourceStatus.Error"/>.
    /// Never contains raw exception details — only a sanitised description.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
