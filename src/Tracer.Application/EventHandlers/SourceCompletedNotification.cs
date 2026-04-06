using MediatR;
using Tracer.Application.Services;
using Tracer.Domain.Enums;

namespace Tracer.Application.EventHandlers;

/// <summary>
/// MediatR notification for when a single provider completes.
/// Dispatched by the orchestrator after each provider finishes.
/// </summary>
public sealed record SourceCompletedNotification(
    Guid TraceId,
    string ProviderId,
    SourceStatus Status,
    int FieldsEnriched,
    long DurationMs) : INotification;

/// <summary>
/// Pushes SourceCompleted event to SignalR clients.
/// </summary>
public sealed class SourceCompletedNotificationHandler : INotificationHandler<SourceCompletedNotification>
{
    private readonly ITraceNotificationService _notificationService;

    public SourceCompletedNotificationHandler(ITraceNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public async Task Handle(SourceCompletedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        await _notificationService.NotifySourceCompletedAsync(
            notification.TraceId,
            notification.ProviderId,
            notification.Status,
            notification.FieldsEnriched,
            notification.DurationMs,
            cancellationToken).ConfigureAwait(false);
    }
}
