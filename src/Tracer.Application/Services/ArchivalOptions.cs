namespace Tracer.Application.Services;

/// <summary>
/// Configuration for the CKB archival background service.
/// Bound from the <c>Archival</c> section in <c>appsettings.json</c>.
/// </summary>
/// <remarks>
/// The archival policy is intentionally simple: archive profiles that
/// were enriched only once (<c>TraceCount &lt;= MaxTraceCount</c>) and
/// haven't been touched in at least <c>MinAgeDays</c> days. Archived
/// profiles remain queryable via <c>?includeArchived=true</c> but are
/// excluded from the re-validation sweep and the fuzzy match candidate
/// pool. A new trace that resolves to an archived profile automatically
/// un-archives it in <c>CkbPersistenceService</c>.
/// </remarks>
public sealed class ArchivalOptions
{
    public const string SectionName = "Archival";

    /// <summary>
    /// Master switch. When <see langword="false"/>, the archival background
    /// service is not registered and no automatic archival runs take place.
    /// Un-archive on new trace continues to work regardless of this flag.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Interval between archival ticks in hours. Default 24 (daily).
    /// Archival is monotonic (a profile stays eligible once it reaches the
    /// age + trace-count threshold) so a daily cadence is safely idempotent.
    /// </summary>
    public int IntervalHours { get; init; } = 24;

    /// <summary>
    /// Minimum age (in days since <c>LastEnrichedAt</c>) before a profile
    /// is considered for archival. Default 365 (one year).
    /// </summary>
    public int MinAgeDays { get; init; } = 365;

    /// <summary>
    /// Maximum <c>TraceCount</c> for a profile to be eligible for archival.
    /// Default 1 — profiles that were only requested once and never returned
    /// to. Profiles with <c>TraceCount &gt; MaxTraceCount</c> are considered
    /// "in use" and kept active regardless of age.
    /// </summary>
    public int MaxTraceCount { get; init; } = 1;

    /// <summary>
    /// Maximum number of rows archived per database round-trip. The service
    /// repeatedly invokes the repository until no more rows match, so this
    /// is a per-batch cap, not a per-tick cap. Keeps the SQL Server
    /// transaction log bounded on the first run after deployment.
    /// Default 500.
    /// </summary>
    public int BatchSize { get; init; } = 500;
}
