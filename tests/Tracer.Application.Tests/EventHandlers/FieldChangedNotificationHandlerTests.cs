using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Tracer.Application.DTOs;
using Tracer.Application.EventHandlers;
using Tracer.Application.Services;
using Tracer.Domain.Enums;
using Tracer.Domain.Events;

namespace Tracer.Application.Tests.EventHandlers;

public sealed class FieldChangedNotificationHandlerTests
{
    private readonly ITraceNotificationService _signalR = Substitute.For<ITraceNotificationService>();
    private readonly ILogger<FieldChangedNotificationHandler> _logger = Substitute.For<ILogger<FieldChangedNotificationHandler>>();

    private FieldChangedNotificationHandler CreateSut() =>
        new(_signalR, _logger);

    private static FieldChangedEvent CreateEvent(
        ChangeSeverity severity,
        FieldName field = FieldName.LegalName,
        ChangeType changeType = ChangeType.Updated) =>
        new(Guid.NewGuid(), Guid.NewGuid(), field, changeType, severity, "\"old\"", "\"new\"");

    [Fact]
    public async Task Handle_CriticalSeverity_SkipsProcessing()
    {
        var sut = CreateSut();
        var notification = CreateEvent(ChangeSeverity.Critical, FieldName.EntityStatus);

        await sut.Handle(notification, CancellationToken.None);

        // Critical is handled by CriticalChangeNotificationHandler
        await _signalR.DidNotReceive().NotifyChangeDetectedAsync(
            Arg.Any<ChangeEventDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MajorSeverity_PushesSignalR()
    {
        var sut = CreateSut();
        var notification = CreateEvent(ChangeSeverity.Major, FieldName.LegalName);

        await sut.Handle(notification, CancellationToken.None);

        await _signalR.Received(1).NotifyChangeDetectedAsync(
            Arg.Is<ChangeEventDto>(dto =>
                dto.Severity == ChangeSeverity.Major &&
                dto.Field == FieldName.LegalName),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MinorSeverity_NoSignalR()
    {
        var sut = CreateSut();
        var notification = CreateEvent(ChangeSeverity.Minor, FieldName.Phone);

        await sut.Handle(notification, CancellationToken.None);

        await _signalR.DidNotReceive().NotifyChangeDetectedAsync(
            Arg.Any<ChangeEventDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CosmeticSeverity_NoSignalR()
    {
        var sut = CreateSut();
        var notification = CreateEvent(ChangeSeverity.Cosmetic, FieldName.Industry, ChangeType.Created);

        await sut.Handle(notification, CancellationToken.None);

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
}
