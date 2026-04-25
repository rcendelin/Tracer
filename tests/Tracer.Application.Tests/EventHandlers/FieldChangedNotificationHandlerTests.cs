using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Tracer.Application.DTOs;
using Tracer.Application.EventHandlers;
using Tracer.Application.Messaging;
using Tracer.Application.Services;
using Tracer.Contracts.Messages;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Events;
using Tracer.Domain.Interfaces;
using ContractsEnums = Tracer.Contracts.Enums;

namespace Tracer.Application.Tests.EventHandlers;

public sealed class FieldChangedNotificationHandlerTests
{
    private readonly IServiceBusPublisher _serviceBus = Substitute.For<IServiceBusPublisher>();
    private readonly ITraceNotificationService _signalR = Substitute.For<ITraceNotificationService>();
    private readonly ICompanyProfileRepository _profileRepo = Substitute.For<ICompanyProfileRepository>();
    private readonly ILogger<FieldChangedNotificationHandler> _logger = Substitute.For<ILogger<FieldChangedNotificationHandler>>();

    private FieldChangedNotificationHandler CreateSut() =>
        new(_serviceBus, _signalR, _profileRepo, _logger);

    private static CompanyProfile CreateProfile() => new("CZ:12345678", "CZ", "12345678");

    private static FieldChangedEvent CreateEvent(
        ChangeSeverity severity,
        Guid profileId,
        FieldName field = FieldName.LegalName,
        ChangeType changeType = ChangeType.Updated) =>
        new(profileId, Guid.NewGuid(), field, changeType, severity, "\"old\"", "\"new\"");

    // ── Critical: both channels skipped (handled elsewhere) ─────────────────

    [Fact]
    public async Task Handle_CriticalSeverity_SkipsBothChannels()
    {
        var sut = CreateSut();
        var profile = CreateProfile();
        _profileRepo.GetByIdAsync(profile.Id, Arg.Any<CancellationToken>()).Returns(profile);
        var notification = CreateEvent(ChangeSeverity.Critical, profile.Id, FieldName.EntityStatus);

        await sut.Handle(notification, CancellationToken.None);

        // Critical is handled by CriticalChangeNotificationHandler — this handler
        // must stay silent to avoid double publish.
        await _serviceBus.DidNotReceive().PublishChangeEventAsync(
            Arg.Any<ChangeEventMessage>(), Arg.Any<CancellationToken>());
        await _signalR.DidNotReceive().NotifyChangeDetectedAsync(
            Arg.Any<ChangeEventDto>(), Arg.Any<CancellationToken>());
        await _profileRepo.DidNotReceive().GetByIdAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ── Major: both Service Bus + SignalR ───────────────────────────────────

    [Fact]
    public async Task Handle_MajorSeverity_PublishesToServiceBusAndSignalR()
    {
        var sut = CreateSut();
        var profile = CreateProfile();
        _profileRepo.GetByIdAsync(profile.Id, Arg.Any<CancellationToken>()).Returns(profile);
        var notification = CreateEvent(ChangeSeverity.Major, profile.Id, FieldName.LegalName);

        await sut.Handle(notification, CancellationToken.None);

        await _serviceBus.Received(1).PublishChangeEventAsync(
            Arg.Is<ChangeEventMessage>(m =>
                m.CompanyProfileId == profile.Id &&
                m.NormalizedKey == profile.NormalizedKey &&
                m.ChangeEvent.Id == notification.ChangeEventId &&
                m.ChangeEvent.Severity == ContractsEnums.ChangeSeverity.Major &&
                m.ChangeEvent.Field == ContractsEnums.FieldName.LegalName),
            Arg.Any<CancellationToken>());

        await _signalR.Received(1).NotifyChangeDetectedAsync(
            Arg.Is<ChangeEventDto>(dto =>
                dto.Severity == ChangeSeverity.Major &&
                dto.Field == FieldName.LegalName),
            Arg.Any<CancellationToken>());
    }

    // ── Minor: Service Bus only (monitoring subscription), no SignalR ───────

    [Fact]
    public async Task Handle_MinorSeverity_PublishesToServiceBusOnly()
    {
        var sut = CreateSut();
        var profile = CreateProfile();
        _profileRepo.GetByIdAsync(profile.Id, Arg.Any<CancellationToken>()).Returns(profile);
        var notification = CreateEvent(ChangeSeverity.Minor, profile.Id, FieldName.Phone);

        await sut.Handle(notification, CancellationToken.None);

        await _serviceBus.Received(1).PublishChangeEventAsync(
            Arg.Is<ChangeEventMessage>(m =>
                m.ChangeEvent.Severity == ContractsEnums.ChangeSeverity.Minor &&
                m.ChangeEvent.Field == ContractsEnums.FieldName.Phone),
            Arg.Any<CancellationToken>());

        await _signalR.DidNotReceive().NotifyChangeDetectedAsync(
            Arg.Any<ChangeEventDto>(), Arg.Any<CancellationToken>());
    }

    // ── Cosmetic: log only ──────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CosmeticSeverity_SkipsBothChannels()
    {
        var sut = CreateSut();
        var profile = CreateProfile();
        _profileRepo.GetByIdAsync(profile.Id, Arg.Any<CancellationToken>()).Returns(profile);
        var notification = CreateEvent(
            ChangeSeverity.Cosmetic, profile.Id, FieldName.Industry, ChangeType.Created);

        await sut.Handle(notification, CancellationToken.None);

        await _serviceBus.DidNotReceive().PublishChangeEventAsync(
            Arg.Any<ChangeEventMessage>(), Arg.Any<CancellationToken>());
        await _signalR.DidNotReceive().NotifyChangeDetectedAsync(
            Arg.Any<ChangeEventDto>(), Arg.Any<CancellationToken>());
        await _profileRepo.DidNotReceive().GetByIdAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ── Profile lookup edge cases ───────────────────────────────────────────

    [Theory]
    [InlineData(ChangeSeverity.Major, FieldName.LegalName)]
    [InlineData(ChangeSeverity.Minor, FieldName.Phone)]
    public async Task Handle_ProfileNotFound_SkipsBothChannels(ChangeSeverity severity, FieldName field)
    {
        // Mirrors CriticalChangeNotificationHandler: without NormalizedKey we cannot
        // publish a correlatable Service Bus message, and pushing SignalR for a
        // profile that no longer exists would confuse the UI. The change is still
        // persisted in the ChangeEvent table and replayable via /api/changes.
        var sut = CreateSut();
        var profileId = Guid.NewGuid();
        _profileRepo.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns((CompanyProfile?)null);
        var notification = CreateEvent(severity, profileId, field);

        await sut.Handle(notification, CancellationToken.None);

        await _serviceBus.DidNotReceive().PublishChangeEventAsync(
            Arg.Any<ChangeEventMessage>(), Arg.Any<CancellationToken>());
        await _signalR.DidNotReceive().NotifyChangeDetectedAsync(
            Arg.Any<ChangeEventDto>(), Arg.Any<CancellationToken>());
    }

    // ── Guard ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NullNotification_Throws()
    {
        var sut = CreateSut();

        var act = () => sut.Handle(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
