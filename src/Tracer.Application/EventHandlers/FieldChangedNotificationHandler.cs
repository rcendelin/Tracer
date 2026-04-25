using MediatR;
using Microsoft.Extensions.Logging;
using Tracer.Application.DTOs;
using Tracer.Application.Mapping;
using Tracer.Application.Messaging;
using Tracer.Application.Services;
using Tracer.Contracts.Messages;
using Tracer.Domain.Enums;
using Tracer.Domain.Events;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.EventHandlers;

/// <summary>
/// Handles <see cref="FieldChangedEvent"/> for non-critical changes.
/// Routes by severity:
/// <list type="bullet">
///   <item><description><b>Critical</b> — skipped (handled by <see cref="CriticalChangeNotificationHandler"/> to avoid double publish).</description></item>
///   <item><description><b>Major</b> — Service Bus topic <c>tracer-changes</c> + SignalR.</description></item>
///   <item><description><b>Minor</b> — Service Bus topic <c>tracer-changes</c> (for monitoring subscription); no SignalR (UI polls change feed).</description></item>
///   <item><description><b>Cosmetic</b> — logged only; never published.</description></item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// This handler runs inside the domain event dispatch loop triggered by
/// <c>TracerDbContext.SaveChangesAsync</c>. It must NOT call <c>SaveChangesAsync</c>
/// to avoid recursive dispatch. The <c>MarkNotified</c> flag is set by
/// <see cref="CkbPersistenceService"/> after the dispatch completes.
/// </para>
/// <para>
/// Profile lookup uses the repository — inside the current scope the DbContext
/// returns the tracked entity without a round-trip. A missing profile is logged
/// and the notification is skipped (the change event is still persisted, so the
/// history is preserved for later replay).
/// </para>
/// </remarks>
public sealed partial class FieldChangedNotificationHandler
    : INotificationHandler<FieldChangedEvent>
{
    private readonly IServiceBusPublisher _serviceBus;
    private readonly ITraceNotificationService _signalR;
    private readonly ICompanyProfileRepository _profileRepository;
    private readonly ILogger<FieldChangedNotificationHandler> _logger;

    public FieldChangedNotificationHandler(
        IServiceBusPublisher serviceBus,
        ITraceNotificationService signalR,
        ICompanyProfileRepository profileRepository,
        ILogger<FieldChangedNotificationHandler> logger)
    {
        _serviceBus = serviceBus;
        _signalR = signalR;
        _profileRepository = profileRepository;
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

        // Critical is handled by CriticalChangeNotificationHandler (also raised
        // by CompanyProfile.UpdateField). Skipping here avoids double publish.
        if (notification.Severity == ChangeSeverity.Critical)
            return;

        // Cosmetic never leaves the log — confidence/formatting changes are high
        // volume with no downstream business value. Publishing them would swamp
        // the monitoring subscription.
        if (notification.Severity == ChangeSeverity.Cosmetic)
            return;

        // NormalizedKey is required for downstream correlation (both on Service Bus
        // and in SignalR UI feeds). If the profile vanished between SaveChangesAsync
        // persisting the ChangeEvent row and this handler running (edge case: concurrent
        // archival), skip both channels to avoid notifying about a non-existent profile.
        // The ChangeEvent itself is already persisted and can be replayed from /api/changes.
        var profile = await _profileRepository
            .GetByIdAsync(notification.CompanyProfileId, cancellationToken)
            .ConfigureAwait(false);

        if (profile is null)
        {
            LogProfileNotFound(notification.CompanyProfileId);
            return;
        }

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

        // Publish Major + Minor to Service Bus topic. The subscription filter on
        // fieldforce-changes (Severity IN {Critical, Major}) keeps Minor out of the
        // FieldForce CRM pipeline; monitoring-changes (no filter) receives everything.
        var message = new ChangeEventMessage
        {
            ChangeEvent = dto.ToContract(),
            CompanyProfileId = notification.CompanyProfileId,
            NormalizedKey = profile.NormalizedKey,
        };

        await _serviceBus.PublishChangeEventAsync(message, cancellationToken).ConfigureAwait(false);
        LogChangePublished(profile.NormalizedKey, notification.Field, notification.Severity);

        // SignalR push is reserved for Major — UI shows a real-time badge/toast.
        // Minor changes are polled from the change feed (see /api/changes) to
        // avoid drowning the hub in low-severity traffic.
        if (notification.Severity == ChangeSeverity.Major)
        {
            await _signalR.NotifyChangeDetectedAsync(dto, cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Field changed: profile={ProfileId}, field={Field}, severity={Severity}, type={ChangeType}")]
    private partial void LogFieldChanged(Guid profileId, FieldName field, ChangeSeverity severity, ChangeType changeType);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Profile {ProfileId} not found for field-change notification; skipping Service Bus publish")]
    private partial void LogProfileNotFound(Guid profileId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Change event published to Service Bus: {NormalizedKey}, field {Field}, severity {Severity}")]
    private partial void LogChangePublished(string normalizedKey, FieldName field, ChangeSeverity severity);
}
