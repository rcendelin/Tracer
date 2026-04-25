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

    /// <summary>
    /// Notifies live progress of the re-validation engine (B-94).
    /// </summary>
    /// <remarks>
    /// Broadcast to <c>Clients.All</c> like <see cref="NotifyChangeDetectedAsync"/> —
    /// re-validation progress is a global concern. The frontend dashboard subscribes
    /// via <c>useSignalR().onValidationProgress</c>. The scheduler is expected to
    /// rate-limit calls (~1 update/2 s) so a 100-profile tick does not flood the hub.
    /// </remarks>
    /// <param name="profilesProcessed">Cumulative profiles processed in the current tick.</param>
    /// <param name="profilesRemaining">Profiles still pending in the current tick.</param>
    Task NotifyValidationProgressAsync(
        int profilesProcessed,
        int profilesRemaining,
        CancellationToken cancellationToken = default);
}
