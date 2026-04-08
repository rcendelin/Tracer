using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace Tracer.Api.Telemetry;

/// <summary>
/// Serilog log event enricher that adds OpenTelemetry trace and span identifiers
/// from <see cref="Activity.Current"/> to every log event.
///
/// Properties added:
///   TraceId — W3C trace identifier (hex, 32 chars), correlates with App Insights operation_Id.
///   SpanId  — W3C span identifier (hex, 16 chars), correlates with App Insights operation_ParentId.
///
/// Usage: .Enrich.With&lt;ActivityTraceEnricher&gt;() in Serilog configuration.
/// </summary>
internal sealed class ActivityTraceEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        var activity = Activity.Current;
        if (activity is null)
            return;

        logEvent.AddOrUpdateProperty(
            propertyFactory.CreateProperty("TraceId", activity.TraceId.ToString()));

        logEvent.AddOrUpdateProperty(
            propertyFactory.CreateProperty("SpanId", activity.SpanId.ToString()));
    }
}
