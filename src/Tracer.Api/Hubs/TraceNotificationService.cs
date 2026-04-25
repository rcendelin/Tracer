using Microsoft.AspNetCore.SignalR;
using Tracer.Application.DTOs;
using Tracer.Application.Services;
using Tracer.Domain.Enums;

namespace Tracer.Api.Hubs;

/// <summary>
/// SignalR-backed implementation of <see cref="ITraceNotificationService"/>.
/// </summary>
/// <remarks>
/// <para>
/// Trace-specific events (<c>SourceCompleted</c>, <c>TraceCompleted</c>) are sent only to
/// clients that joined the per-trace group via <c>TraceHub.SubscribeToTrace(traceId)</c>.
/// This prevents cross-client leakage of enrichment data in multi-consumer deployments.
/// </para>
/// <para>
/// Profile change events (<c>ChangeDetected</c>) are broadcast to all authenticated clients
/// because change monitoring is a shared concern — all consumers are expected to be
/// authorized and interested in profile changes.
/// </para>
/// </remarks>
internal sealed class TraceNotificationService : ITraceNotificationService
{
    private readonly IHubContext<TraceHub> _hubContext;

    public TraceNotificationService(IHubContext<TraceHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task NotifySourceCompletedAsync(
        Guid traceId, string providerId, SourceStatus status,
        int fieldsEnriched, long durationMs, CancellationToken cancellationToken)
    {
        // Send only to clients subscribed to this specific trace.
        await _hubContext.Clients
            .Group(traceId.ToString())
            .SendAsync("SourceCompleted", new
            {
                traceId,
                // Nested as { source } to match the SourceCompletedEvent frontend type.
                source = new
                {
                    providerId,
                    status = status.ToString(),
                    fieldsEnriched,
                    durationMs,
                    errorMessage = (string?)null,
                },
            }, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task NotifyTraceCompletedAsync(
        Guid traceId, TraceStatus status, double? overallConfidence,
        CancellationToken cancellationToken)
    {
        // Send only to clients subscribed to this specific trace.
        await _hubContext.Clients
            .Group(traceId.ToString())
            .SendAsync("TraceCompleted", new
            {
                traceId,
                status = status.ToString(),
                overallConfidence,
            }, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task NotifyChangeDetectedAsync(
        ChangeEventDto changeEvent, CancellationToken cancellationToken)
    {
        // Broadcast change events to all authenticated clients — change monitoring
        // is a shared concern across all API consumers.
        await _hubContext.Clients
            .All
            .SendAsync("ChangeDetected", changeEvent, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task NotifyValidationProgressAsync(
        int profilesProcessed, int profilesRemaining, CancellationToken cancellationToken)
    {
        // Broadcast to all clients — the validation dashboard is a shared monitoring
        // surface. Caller (RevalidationScheduler) rate-limits to ~1 update / 2 s.
        await _hubContext.Clients
            .All
            .SendAsync("ValidationProgress", new
            {
                profilesProcessed,
                profilesRemaining,
            }, cancellationToken)
            .ConfigureAwait(false);
    }
}
