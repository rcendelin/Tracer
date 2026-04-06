using Tracer.Application.DTOs;
using Tracer.Domain.Enums;

namespace Tracer.Application.Services;

/// <summary>
/// Pushes real-time notifications to connected clients (via SignalR).
/// </summary>
public interface ITraceNotificationService
{
    /// <summary>
    /// Notifies that a provider completed enrichment for a trace.
    /// </summary>
    Task NotifySourceCompletedAsync(
        Guid traceId,
        string providerId,
        SourceStatus status,
        int fieldsEnriched,
        long durationMs,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies that a trace request completed (all providers done).
    /// </summary>
    Task NotifyTraceCompletedAsync(
        Guid traceId,
        TraceStatus status,
        double? overallConfidence,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies that a field change was detected on a company profile.
    /// </summary>
    Task NotifyChangeDetectedAsync(
        ChangeEventDto changeEvent,
        CancellationToken cancellationToken = default);
}
