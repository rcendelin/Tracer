using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Tracer.Application.Commands.SubmitBatchTrace;
using Tracer.Application.DTOs;
using Tracer.Application.Messaging;
using Tracer.Contracts.Messages;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Tests.Commands.SubmitBatchTrace;

public sealed class SubmitBatchTraceHandlerTests
{
    private readonly ITraceRequestRepository _traceRequestRepo = Substitute.For<ITraceRequestRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IServiceBusPublisher _serviceBus = Substitute.For<IServiceBusPublisher>();
    private readonly ILogger<SubmitBatchTraceHandler> _logger = Substitute.For<ILogger<SubmitBatchTraceHandler>>();

    private SubmitBatchTraceHandler CreateSut() =>
        new(_traceRequestRepo, _unitOfWork, _serviceBus, _logger);

    private static TraceRequestDto CreateItem(
        string companyName = "Acme s.r.o.",
        string country = "CZ",
        string? registrationId = "12345678") =>
        new()
        {
            CompanyName = companyName,
            Country = country,
            RegistrationId = registrationId,
            Depth = TraceDepth.Standard,
        };

    private static SubmitBatchTraceCommand CreateCommand(
        IReadOnlyCollection<TraceRequestDto>? items = null,
        string source = "rest-api-batch") =>
        new()
        {
            Items = items ?? [CreateItem()],
            Source = source,
        };

    [Fact]
    public async Task Handle_SingleItem_PersistsAndPublishes()
    {
        // Arrange
        var command = CreateCommand();
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(command, CancellationToken.None);

        // Assert
        result.Items.Should().HaveCount(1);
        result.Items.First().Status.Should().Be(TraceStatus.Queued);

        await _traceRequestRepo.Received(1).AddAsync(Arg.Any<Tracer.Domain.Entities.TraceRequest>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _serviceBus.Received(1).EnqueueTraceRequestAsync(Arg.Any<TraceRequestMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MultipleItems_PublishesAll()
    {
        // Arrange
        var items = new[]
        {
            CreateItem("Company A", "CZ", "11111111"),
            CreateItem("Company B", "CZ", "22222222"),
            CreateItem("Company C", "DE", "33333333"),
        };
        var command = CreateCommand(items);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(command, CancellationToken.None);

        // Assert
        result.Items.Should().HaveCount(3);
        result.Items.Should().AllSatisfy(item => item.Status.Should().Be(TraceStatus.Queued));
        result.Count.Should().Be(3);

        await _traceRequestRepo.Received(3).AddAsync(Arg.Any<Tracer.Domain.Entities.TraceRequest>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _serviceBus.Received(3).EnqueueTraceRequestAsync(Arg.Any<TraceRequestMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SavesToDatabaseBeforePublishing()
    {
        // Arrange — verify transaction order: SaveChangesAsync happens before any Service Bus call
        var callOrder = new List<string>();

        _unitOfWork.When(uow => uow.SaveChangesAsync(Arg.Any<CancellationToken>()))
            .Do(_ => callOrder.Add("save"));

        _serviceBus.When(sb => sb.EnqueueTraceRequestAsync(Arg.Any<TraceRequestMessage>(), Arg.Any<CancellationToken>()))
            .Do(_ => callOrder.Add("publish"));

        var command = CreateCommand();
        var sut = CreateSut();

        // Act
        await sut.Handle(command, CancellationToken.None);

        // Assert
        callOrder.Should().Equal("save", "publish");
    }

    [Fact]
    public async Task Handle_PublishFails_ReturnsFailedStatusForAffectedItem()
    {
        // Arrange — Service Bus publish throws but handler should not rethrow (best-effort)
        _serviceBus.EnqueueTraceRequestAsync(Arg.Any<TraceRequestMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Service Bus unavailable"));

        var command = CreateCommand();
        var sut = CreateSut();

        // Act — must not throw
        var result = await sut.Handle(command, CancellationToken.None);

        // Assert — item persisted in DB (Queued) but response returns Failed so caller knows
        result.Items.Should().HaveCount(1);
        result.Items.First().Status.Should().Be(TraceStatus.Failed);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OperationCancelledDuringPublish_Propagates()
    {
        // Arrange — cancellation must propagate (not swallowed by the catch filter)
        using var cts = new CancellationTokenSource();
        _serviceBus.EnqueueTraceRequestAsync(Arg.Any<TraceRequestMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var command = CreateCommand();
        var sut = CreateSut();

        // Act & Assert
        await sut.Invoking(s => s.Handle(command, cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Handle_TraceIdIsCorrelationId_MatchesPersistedId()
    {
        // Arrange — CorrelationId in the Service Bus message must equal TraceRequest.Id
        TraceRequestMessage? capturedMessage = null;
        _serviceBus.When(sb => sb.EnqueueTraceRequestAsync(Arg.Any<TraceRequestMessage>(), Arg.Any<CancellationToken>()))
            .Do(call => capturedMessage = call.ArgAt<TraceRequestMessage>(0));

        var command = CreateCommand();
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(command, CancellationToken.None);

        // Assert
        capturedMessage.Should().NotBeNull();
        var traceId = result.Items.Single().TraceId;
        capturedMessage!.CorrelationId.Should().Be(traceId.ToString());
    }

    [Fact]
    public async Task Handle_SetsSource_OnPublishedMessage()
    {
        // Arrange
        const string customSource = "fieldforce-import";
        TraceRequestMessage? capturedMessage = null;
        _serviceBus.When(sb => sb.EnqueueTraceRequestAsync(Arg.Any<TraceRequestMessage>(), Arg.Any<CancellationToken>()))
            .Do(call => capturedMessage = call.ArgAt<TraceRequestMessage>(0));

        var command = CreateCommand(source: customSource);
        var sut = CreateSut();

        // Act
        await sut.Handle(command, CancellationToken.None);

        // Assert
        capturedMessage!.Source.Should().Be(customSource);
    }

    [Fact]
    public async Task Handle_CallerCorrelationId_EchoedInResponse()
    {
        // Arrange
        var item = CreateItem() with { };
        var itemWithCorrelation = new TraceRequestDto
        {
            CompanyName = "Acme s.r.o.",
            Country = "CZ",
            RegistrationId = "12345678",
            Depth = TraceDepth.Standard,
            CorrelationId = "caller-ref-abc123",
        };

        var command = new SubmitBatchTraceCommand
        {
            Items = [itemWithCorrelation],
        };

        var sut = CreateSut();

        // Act
        var result = await sut.Handle(command, CancellationToken.None);

        // Assert — caller's CorrelationId echoed back
        result.Items.Single().CorrelationId.Should().Be("caller-ref-abc123");
    }

    [Fact]
    public async Task Handle_NullCommand_Throws()
    {
        var sut = CreateSut();

        await sut.Invoking(s => s.Handle(null!, CancellationToken.None))
            .Should().ThrowAsync<ArgumentNullException>();
    }
}
