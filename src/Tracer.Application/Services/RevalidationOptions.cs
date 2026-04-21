namespace Tracer.Application.Services;

/// <summary>
/// Configuration for the CKB re-validation scheduler.
/// Bound from the <c>Revalidation</c> section in <c>appsettings.json</c>.
/// </summary>
public sealed class RevalidationOptions
{
    public const string SectionName = "Revalidation";

    /// <summary>
    /// Master switch. When <c>false</c>, the scheduler is not registered
    /// and no automatic re-validation runs take place. Manual
    /// <c>POST /api/profiles/{id}/revalidate</c> requests are also rejected
    /// because the queue consumer is not running.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Interval between scheduler ticks in minutes. Default 60 (hourly).
    /// </summary>
    public int IntervalMinutes { get; init; } = 60;

    /// <summary>
    /// Maximum number of profiles processed by a single scheduled run.
    /// Default 100. Manual queue items are processed in addition and do
    /// not count against this budget.
    /// </summary>
    public int MaxProfilesPerRun { get; init; } = 100;

    /// <summary>
    /// Off-peak window gating the automatic run. Manual queue items are
    /// always processed regardless of the off-peak gate.
    /// </summary>
    public OffPeakWindow OffPeak { get; init; } = new();
}
