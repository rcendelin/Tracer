using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Tracer.Application.DTOs;
using Tracer.Application.EventHandlers;
using Tracer.Application.Messaging;
using Tracer.Application.Services;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Events;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Tests.EventHandlers;

public sealed class CriticalChangeNotificationHandlerTests
{
    private readonly IServiceBusPublisher _serviceBus = Substitute.For<IServiceBusPublisher>();
    private readonly ITraceNotificationService _signalR = Substitute.For<ITraceNotificationService>();
    private readonly ICompanyProfileRepository _profileRepo = Substitute.For<ICompanyProfileRepository>();
    private readonly ILogger<CriticalChangeNotificationHandler> _logger = Substitute.For<ILogger<CriticalChangeNotificationHandler>>();

    private CriticalChangeNotificationHandler CreateSut() =>
        new(_serviceBus, _signalR, _profileRepo, _logger);

    private static CompanyProfile CreateProfile() => new("CZ:12345678", "CZ", "12345678");

    [Fact]
    public async Task Handle_PublishesToServiceBus()
    {
        var sut = CreateSut();
        var profile = CreateProfile();
        var changeEventId = Guid.NewGuid();
        var notification = new CriticalChangeDetectedEvent(
            profile.Id, changeEventId, FieldName.EntityStatus, "\"Dissolved\"");

        _profileRepo.GetByIdAsync(profile.Id, Arg.Any<CancellationToken>())
            .Returns(profile);

        await sut.Handle(notification, CancellationToken.None);

        await _serviceBus.Received(1).PublishChangeEventAsync(
            Arg.Is<ChangeEventMessage>(m =>
                m.CompanyProfileId == profile.Id &&
                m.NormalizedKey == "CZ:12345678" &&
                m.ChangeEvent.Severity == ChangeSeverity.Critical &&
                m.ChangeEvent.Id == changeEventId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PushesSignalR()
    {
        var sut = CreateSut();
        var profile = CreateProfile();
        var notification = new CriticalChangeDetectedEvent(
            profile.Id, Guid.NewGuid(), FieldName.EntityStatus, "\"In Liquidation\"");

        _profileRepo.GetByIdAsync(profile.Id, Arg.Any<CancellationToken>())
            .Returns(profile);

        await sut.Handle(notification, CancellationToken.None);

        await _signalR.Received(1).NotifyChangeDetectedAsync(
            Arg.Is<ChangeEventDto>(dto =>
                dto.Severity == ChangeSeverity.Critical &&
                dto.Field == FieldName.EntityStatus),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ProfileNotFound_DoesNotPublish()
    {
        var sut = CreateSut();
        var notification = new CriticalChangeDetectedEvent(
            Guid.NewGuid(), Guid.NewGuid(), FieldName.EntityStatus, "\"Dissolved\"");

        _profileRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((CompanyProfile?)null);

        await sut.Handle(notification, CancellationToken.None);

        await _serviceBus.DidNotReceive().PublishChangeEventAsync(
            Arg.Any<ChangeEventMessage>(), Arg.Any<CancellationToken>());
        await _signalR.DidNotReceive().NotifyChangeDetectedAsync(
            Arg.Any<ChangeEventDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NullNotification_Throws()
    {
        var sut = CreateSut();

        var act = () => sut.Handle(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Handle_UsesChangeEventIdFromNotification()
    {
        var sut = CreateSut();
        var profile = CreateProfile();
        var changeEventId = Guid.NewGuid();
        var notification = new CriticalChangeDetectedEvent(
            profile.Id, changeEventId, FieldName.EntityStatus, "\"Dissolved\"");

        _profileRepo.GetByIdAsync(profile.Id, Arg.Any<CancellationToken>())
            .Returns(profile);

        await sut.Handle(notification, CancellationToken.None);

        await _serviceBus.Received(1).PublishChangeEventAsync(
            Arg.Is<ChangeEventMessage>(m => m.ChangeEvent.Id == changeEventId),
            Arg.Any<CancellationToken>());
    }
}
