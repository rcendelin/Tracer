using MediatR;
using Microsoft.Extensions.Logging;
using Tracer.Application.DTOs;
using Tracer.Application.Mapping;
using Tracer.Application.Messaging;
using Tracer.Application.Services;
using Tracer.Domain.Events;
using Tracer.Domain.Interfaces;
using DomainEnums = Tracer.Domain.Enums;

namespace Tracer.Application.EventHandlers;

/// <summary>
/// Handles <see cref="CriticalChangeDetectedEvent"/> by publishing a notification
/// to Service Bus topic <c>tracer-changes</c> and pushing a SignalR event.
/// </summary>
public sealed partial class CriticalChangeNotificationHandler
    : INotificationHandler<CriticalChangeDetectedEvent>
{
    private readonly IServiceBusPublisher _serviceBus;
    private readonly ITraceNotificationService _signalR;
    private readonly ICompanyProfileRepository _profileRepository;
    private readonly ILogger<CriticalChangeNotificationHandler> _logger;

    public CriticalChangeNotificationHandler(
        IServiceBusPublisher serviceBus,
        ITraceNotificationService signalR,
        ICompanyProfileRepository profileRepository,
        ILogger<CriticalChangeNotificationHandler> logger)
    {
        _serviceBus = serviceBus;
        _signalR = signalR;
        _profileRepository = profileRepository;
        _logger = logger;
    }

    public async Task Handle(CriticalChangeDetectedEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        LogCriticalChangeReceived(notification.CompanyProfileId, notification.Field);

        // Load profile for NormalizedKey (required by ChangeEventMessage)
        var profile = await _profileRepository
            .GetByIdAsync(notification.CompanyProfileId, cancellationToken)
            .ConfigureAwait(false);

        if (profile is null)
        {
            LogProfileNotFound(notification.CompanyProfileId);
            return;
        }

        // Build internal DTO for SignalR notification (carries IsNotified state)
        var changeEventDto = new ChangeEventDto
        {
            Id = notification.ChangeEventId,
            CompanyProfileId = notification.CompanyProfileId,
            Field = notification.Field,
            ChangeType = DomainEnums.ChangeType.Updated,
            Severity = DomainEnums.ChangeSeverity.Critical,
            NewValueJson = notification.NewValueJson,
            DetectedBy = "change-detector",
            DetectedAt = DateTimeOffset.UtcNow,
        };

        // Publish to Service Bus topic using Contracts message type
        var message = new ChangeEventMessage
        {
            ChangeEvent = changeEventDto.ToContract(),
            CompanyProfileId = notification.CompanyProfileId,
            NormalizedKey = profile.NormalizedKey,
        };

        await _serviceBus.PublishChangeEventAsync(message, cancellationToken).ConfigureAwait(false);

        // Push SignalR notification using internal DTO
        await _signalR.NotifyChangeDetectedAsync(changeEventDto, cancellationToken).ConfigureAwait(false);

        LogCriticalChangePublished(profile.NormalizedKey, notification.Field);
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Critical change detected on profile {ProfileId}, field {Field}")]
    private partial void LogCriticalChangeReceived(Guid profileId, DomainEnums.FieldName field);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Profile {ProfileId} not found for critical change notification")]
    private partial void LogProfileNotFound(Guid profileId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Critical change published to Service Bus + SignalR: {NormalizedKey}, field {Field}")]
    private partial void LogCriticalChangePublished(string normalizedKey, DomainEnums.FieldName field);
}
