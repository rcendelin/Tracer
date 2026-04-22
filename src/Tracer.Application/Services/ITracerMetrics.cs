namespace Tracer.Application.Services;

/// <summary>
/// Abstraction for recording Tracer observability metrics.
/// Implementation records to System.Diagnostics.Metrics and exports via OpenTelemetry.
/// </summary>
public interface ITracerMetrics
{
    /// <summary>
    /// Name of the OpenTelemetry Meter. Register with <c>metrics.AddMeter(ITracerMetrics.MeterName)</c>
    /// in Program.cs so the metrics are exported to Azure Monitor / Application Insights.
    /// </summary>
    const string MeterName = "Tracer";

    /// <summary>
    /// Records the duration of a provider enrichment call and whether it succeeded.
    /// Increments the success or error counter accordingly.
    /// </summary>
    /// <param name="providerId">Provider identifier (e.g. "ares", "gleif").</param>
    /// <param name="milliseconds">Duration of the provider call in milliseconds.</param>
    /// <param name="success">True if the provider returned data; false on error or timeout.</param>
    void RecordProviderDuration(string providerId, double milliseconds, bool success);

    /// <summary>
    /// Records the number of fields returned by a provider enrichment call.
    /// Called only when the provider succeeded and returned at least one field.
    /// </summary>
    /// <param name="providerId">Provider identifier.</param>
    /// <param name="fieldCount">Number of fields returned.</param>
    void RecordProviderFieldsEnriched(string providerId, int fieldCount);

    /// <summary>
    /// Records the total duration of a complete trace enrichment pass.
    /// </summary>
    /// <param name="depth">TraceDepth name ("Quick", "Standard", "Deep").</param>
    /// <param name="milliseconds">Total trace duration in milliseconds.</param>
    void RecordTraceDuration(string depth, double milliseconds);

    /// <summary>
    /// Records the creation of a new CKB company profile.
    /// Called when a profile is first persisted (TraceCount transitions from 0 to 1).
    /// </summary>
    void RecordCkbProfileCreated();

    /// <summary>Records a profile cache hit (profile found in distributed cache).</summary>
    void RecordCacheHit();

    /// <summary>Records a profile cache miss (profile not in distributed cache).</summary>
    void RecordCacheMiss();

    /// <summary>
    /// Records the outcome of a single <see cref="Tracer.Infrastructure.BackgroundJobs.RevalidationScheduler"/>
    /// tick. <paramref name="trigger"/> is one of <c>"auto"</c> (scheduled tick)
    /// or <c>"manual"</c> (queue drain). <paramref name="processed"/> +
    /// <paramref name="skipped"/> + <paramref name="failed"/> equals the number
    /// of candidates returned by the repository.
    /// </summary>
    void RecordRevalidationRun(string trigger, int processed, int skipped, int failed, double durationMs);
}
