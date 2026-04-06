using Microsoft.AspNetCore.SignalR;
using Tracer.Application.DTOs;
using Tracer.Application.Services;
using Tracer.Domain.Enums;

namespace Tracer.Api.Hubs;

/// <summary>
/// SignalR-backed implementation of <see cref="ITraceNotificationService"/>.
/// Broadcasts to all connected clients.
/// </summary>
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
        await _hubContext.Clients.All.SendAsync("SourceCompleted", new
        {
            traceId,
            providerId,
            status = status.ToString(),
            fieldsEnriched,
            durationMs,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task NotifyTraceCompletedAsync(
        Guid traceId, TraceStatus status, double? overallConfidence,
        CancellationToken cancellationToken)
    {
        await _hubContext.Clients.All.SendAsync("TraceCompleted", new
        {
            traceId,
            status = status.ToString(),
            overallConfidence,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task NotifyChangeDetectedAsync(
        ChangeEventDto changeEvent, CancellationToken cancellationToken)
    {
        await _hubContext.Clients.All.SendAsync("ChangeDetected",
            changeEvent, cancellationToken).ConfigureAwait(false);
    }
}
