using MediatR;
using Tracer.Application.Services;
using Tracer.Domain.Enums;

namespace Tracer.Application.EventHandlers;

/// <summary>
/// MediatR notification wrapper for trace completion.
/// Dispatched by the handler after trace completes.
/// </summary>
public sealed record TraceCompletedNotification(
    Guid TraceRequestId,
    TraceStatus Status,
    double? OverallConfidence) : INotification;

/// <summary>
/// Handles <see cref="TraceCompletedNotification"/> by pushing a SignalR notification.
/// </summary>
public sealed class TraceCompletedNotificationHandler : INotificationHandler<TraceCompletedNotification>
{
    private readonly ITraceNotificationService _notificationService;

    public TraceCompletedNotificationHandler(ITraceNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public async Task Handle(TraceCompletedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        await _notificationService.NotifyTraceCompletedAsync(
            notification.TraceRequestId,
            notification.Status,
            notification.OverallConfidence,
            cancellationToken).ConfigureAwait(false);
    }
}
