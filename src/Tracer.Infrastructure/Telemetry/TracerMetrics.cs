using System.Diagnostics;
using System.Diagnostics.Metrics;
using Tracer.Application.Services;

namespace Tracer.Infrastructure.Telemetry;

/// <summary>
/// Records Tracer observability metrics using System.Diagnostics.Metrics.
/// The "Tracer" meter is exported to Azure Monitor (Application Insights) via OpenTelemetry.
///
/// Metric names follow OpenTelemetry semantic conventions (dot-separated, lowercase):
///   tracer.provider.duration      — histogram, unit: ms, tags: provider.id
///   tracer.provider.successes     — counter, tags: provider.id
///   tracer.provider.errors        — counter, tags: provider.id (error + timeout)
///   tracer.provider.fields_enriched — counter, tags: provider.id
///   tracer.trace.duration         — histogram, unit: ms, tags: trace.depth
///   tracer.ckb.profiles_created   — counter
///   tracer.cache.hits             — counter
///   tracer.cache.misses           — counter
/// </summary>
internal sealed class TracerMetrics : ITracerMetrics, IDisposable
{
    // MeterName is defined on ITracerMetrics (Application layer) so Program.cs can reference it
    // without accessing this internal implementation class.
    private const string MeterNameValue = ITracerMetrics.MeterName;

    private readonly Meter _meter;

    private readonly Histogram<double> _providerDuration;
    private readonly Counter<long> _providerSuccesses;
    private readonly Counter<long> _providerErrors;
    private readonly Counter<long> _providerFieldsEnriched;
    private readonly Histogram<double> _traceDuration;
    private readonly Counter<long> _ckbProfilesCreated;
    private readonly Counter<long> _cacheHits;
    private readonly Counter<long> _cacheMisses;
    private readonly Histogram<double> _revalidationDuration;
    private readonly Counter<long> _revalidationProcessed;
    private readonly Counter<long> _revalidationSkipped;
    private readonly Counter<long> _revalidationFailed;

    public TracerMetrics()
    {
        _meter = new Meter(MeterNameValue, "1.0.0");

        _providerDuration = _meter.CreateHistogram<double>(
            "tracer.provider.duration",
            unit: "ms",
            description: "Duration of a provider enrichment call in milliseconds.");

        _providerSuccesses = _meter.CreateCounter<long>(
            "tracer.provider.successes",
            description: "Number of successful provider enrichment calls (status = Found).");

        _providerErrors = _meter.CreateCounter<long>(
            "tracer.provider.errors",
            description: "Number of failed or timed-out provider enrichment calls.");

        _providerFieldsEnriched = _meter.CreateCounter<long>(
            "tracer.provider.fields_enriched",
            description: "Number of fields returned by successful provider enrichments.");

        _traceDuration = _meter.CreateHistogram<double>(
            "tracer.trace.duration",
            unit: "ms",
            description: "Total duration of a complete trace enrichment pass in milliseconds.");

        _ckbProfilesCreated = _meter.CreateCounter<long>(
            "tracer.ckb.profiles_created",
            description: "Number of new company profiles created in the CKB.");

        _cacheHits = _meter.CreateCounter<long>(
            "tracer.cache.hits",
            description: "Number of profile cache hits (profile found in distributed cache).");

        _cacheMisses = _meter.CreateCounter<long>(
            "tracer.cache.misses",
            description: "Number of profile cache misses (profile not in distributed cache).");

        _revalidationDuration = _meter.CreateHistogram<double>(
            "tracer.revalidation.duration",
            unit: "ms",
            description: "Duration of a single re-validation scheduler tick in milliseconds.");

        _revalidationProcessed = _meter.CreateCounter<long>(
            "tracer.revalidation.processed",
            description: "Number of CKB profiles actually re-validated by the scheduler.");

        _revalidationSkipped = _meter.CreateCounter<long>(
            "tracer.revalidation.skipped",
            description: "Number of CKB profiles that were skipped (no expired fields or runner deferred).");

        _revalidationFailed = _meter.CreateCounter<long>(
            "tracer.revalidation.failed",
            description: "Number of re-validation passes that raised an unhandled error.");
    }

    /// <inheritdoc/>
    public void RecordProviderDuration(string providerId, double milliseconds, bool success)
    {
        var tags = new TagList { { "provider.id", providerId } };
        _providerDuration.Record(milliseconds, tags);

        if (success)
            _providerSuccesses.Add(1, tags);
        else
            _providerErrors.Add(1, tags);
    }

    /// <inheritdoc/>
    public void RecordProviderFieldsEnriched(string providerId, int fieldCount)
    {
        if (fieldCount > 0)
            _providerFieldsEnriched.Add(fieldCount, new TagList { { "provider.id", providerId } });
    }

    /// <inheritdoc/>
    public void RecordTraceDuration(string depth, double milliseconds)
    {
        _traceDuration.Record(milliseconds, new TagList { { "trace.depth", depth } });
    }

    /// <inheritdoc/>
    public void RecordCkbProfileCreated() => _ckbProfilesCreated.Add(1);

    /// <inheritdoc/>
    public void RecordCacheHit() => _cacheHits.Add(1);

    /// <inheritdoc/>
    public void RecordCacheMiss() => _cacheMisses.Add(1);

    /// <inheritdoc/>
    public void RecordRevalidationRun(string trigger, int processed, int skipped, int failed, double durationMs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trigger);

        var tags = new TagList { { "trigger", trigger } };
        _revalidationDuration.Record(durationMs, tags);

        if (processed > 0)
            _revalidationProcessed.Add(processed, tags);
        if (skipped > 0)
            _revalidationSkipped.Add(skipped, tags);
        if (failed > 0)
            _revalidationFailed.Add(failed, tags);
    }

    public void Dispose() => _meter.Dispose();
}
