using MediatR;
using Microsoft.Extensions.Logging;
using Tracer.Application.DTOs;
using Tracer.Application.Services;
using Tracer.Domain.Enums;
using Tracer.Domain.Events;

namespace Tracer.Application.EventHandlers;

/// <summary>
/// Handles <see cref="FieldChangedEvent"/> for non-critical changes.
/// Major changes trigger a SignalR notification; all changes are logged.
/// Critical changes are handled separately by <see cref="CriticalChangeNotificationHandler"/>.
/// </summary>
/// <remarks>
/// This handler runs inside the domain event dispatch loop triggered by
/// <c>TracerDbContext.SaveChangesAsync</c>. It must NOT call <c>SaveChangesAsync</c>
/// to avoid recursive dispatch. The <c>MarkNotified</c> flag is set by
/// <see cref="CkbPersistenceService"/> after the dispatch completes.
/// </remarks>
public sealed partial class FieldChangedNotificationHandler
    : INotificationHandler<FieldChangedEvent>
{
    private readonly ITraceNotificationService _signalR;
    private readonly ILogger<FieldChangedNotificationHandler> _logger;

    public FieldChangedNotificationHandler(
        ITraceNotificationService signalR,
        ILogger<FieldChangedNotificationHandler> logger)
    {
        _signalR = signalR;
        _logger = logger;
    }

    public async Task Handle(FieldChangedEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        LogFieldChanged(
            notification.CompanyProfileId,
            notification.Field,
            notification.Severity,
            notification.ChangeType);

        // Critical changes are handled by CriticalChangeNotificationHandler — skip here.
        if (notification.Severity == ChangeSeverity.Critical)
            return;

        // Major changes get a SignalR push for near-real-time UI updates.
        if (notification.Severity >= ChangeSeverity.Major)
        {
            var dto = new ChangeEventDto
            {
                Id = notification.ChangeEventId,
                CompanyProfileId = notification.CompanyProfileId,
                Field = notification.Field,
                ChangeType = notification.ChangeType,
                Severity = notification.Severity,
                PreviousValueJson = notification.PreviousValueJson,
                NewValueJson = notification.NewValueJson,
                DetectedBy = "change-detector",
                DetectedAt = DateTimeOffset.UtcNow,
            };

            await _signalR.NotifyChangeDetectedAsync(dto, cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Field changed: profile={ProfileId}, field={Field}, severity={Severity}, type={ChangeType}")]
    private partial void LogFieldChanged(Guid profileId, FieldName field, ChangeSeverity severity, ChangeType changeType);
}
