using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Tracer.Application;
using Tracer.Application.Commands.SubmitBatchTrace;
using Tracer.Application.DTOs;
using Tracer.Application.Mapping;
using Tracer.Application.Messaging;
using Tracer.Application.Services;
using Tracer.Contracts.Messages;
using Tracer.Domain.Entities;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;
using ContractsEnums = Tracer.Contracts.Enums;
using DomainEnums = Tracer.Domain.Enums;

namespace Tracer.Infrastructure.Tests.Messaging;

/// <summary>
/// Tests the MediatR pipeline invoked by <c>ServiceBusConsumer</c>:
/// <c>TraceRequestMessage → SubmitBatchTraceCommand → IMediator → result → TraceResponseMessage</c>.
/// Uses a real <see cref="IMediator"/> wired with mocked repositories to verify the
/// full handler logic without requiring a real database or Service Bus.
/// </summary>
public sealed class ServiceBusConsumerPipelineTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IMediator _mediator;
    private readonly ITraceRequestRepository _traceRequestRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IServiceBusPublisher _serviceBusPublisher;

    public ServiceBusConsumerPipelineTests()
    {
        _traceRequestRepository = Substitute.For<ITraceRequestRepository>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _serviceBusPublisher = Substitute.For<IServiceBusPublisher>();

        var services = new ServiceCollection();

        services.AddApplication();

        // Replace Application-registered scoped services with mocks
        services.AddScoped(_ => _traceRequestRepository);
        services.AddScoped(_ => _unitOfWork);
        services.AddSingleton(_serviceBusPublisher);

        // Infrastructure services required by Application layer
        services.AddScoped(_ => Substitute.For<ICompanyProfileRepository>());
        services.AddScoped(_ => Substitute.For<IChangeEventRepository>());
        services.AddScoped(_ => Substitute.For<IValidationRecordRepository>());
        services.AddScoped(_ => Substitute.For<ISourceResultRepository>());
        services.AddScoped(_ => Substitute.For<IWebhookCallbackService>());

        // WaterfallOrchestrator is scoped in Application but implemented in Infrastructure.
        // Replace with a mock that returns a minimal CompanyProfile.
        var orchestratorMock = Substitute.For<IWaterfallOrchestrator>();
        orchestratorMock.ExecuteAsync(Arg.Any<TraceRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var profile = new CompanyProfile("CZ:00177041", "CZ", "00177041");
                profile.SetOverallConfidence(Confidence.Create(0.9));
                return Task.FromResult(profile);
            });
        services.AddScoped(_ => orchestratorMock);

        // NullLogger for all types — avoids Castle.DynamicProxy failures on internal type loggers
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>),
            typeof(NullLogger<>));

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<IMediator>();
    }

    public void Dispose() => _serviceProvider.Dispose();

    // ── SubmitBatchTraceCommand ────────────────────────────────────────────────

    [Fact]
    public async Task SubmitBatchTrace_SingleItem_EnqueuesOneMessage()
    {
        var command = new SubmitBatchTraceCommand
        {
            Items = new[]
            {
                new TraceRequestDto
                {
                    CompanyName = "ACME s.r.o.",
                    Country = "CZ",
                    RegistrationId = "00177041",
                },
            },
            Source = "service-bus",
        };

        var result = await _mediator.Send(command).ConfigureAwait(true);

        result.Should().NotBeNull();
        result.Items.Should().HaveCount(1);
        result.Items.Single().Status.Should().Be(DomainEnums.TraceStatus.Queued);

        await _serviceBusPublisher.Received(1)
            .EnqueueTraceRequestAsync(Arg.Any<TraceRequestMessage>(), Arg.Any<CancellationToken>())
            .ConfigureAwait(true);
    }

    [Fact]
    public async Task SubmitBatchTrace_MultipleItems_EnqueuesAllMessages()
    {
        var command = new SubmitBatchTraceCommand
        {
            Items = new[]
            {
                new TraceRequestDto { CompanyName = "ACME s.r.o.", Country = "CZ" },
                new TraceRequestDto { CompanyName = "Škoda Auto a.s.", Country = "CZ" },
                new TraceRequestDto { CompanyName = "BHP Group", Country = "AU" },
            },
            Source = "batch-api",
        };

        var result = await _mediator.Send(command).ConfigureAwait(true);

        result.Items.Should().HaveCount(3);
        result.Items.Should().AllSatisfy(item =>
            item.Status.Should().Be(DomainEnums.TraceStatus.Queued));

        await _serviceBusPublisher.Received(3)
            .EnqueueTraceRequestAsync(Arg.Any<TraceRequestMessage>(), Arg.Any<CancellationToken>())
            .ConfigureAwait(true);
    }

    [Fact]
    public async Task SubmitBatchTrace_ItemWithCorrelationId_EchoesCorrelationIdInResponse()
    {
        const string callerCorrelationId = "ff-req-00042";
        var command = new SubmitBatchTraceCommand
        {
            Items = new[]
            {
                new TraceRequestDto
                {
                    CorrelationId = callerCorrelationId,
                    CompanyName = "ACME s.r.o.",
                },
            },
            Source = "test",
        };

        var result = await _mediator.Send(command).ConfigureAwait(true);

        result.Items.Single().CorrelationId.Should().Be(callerCorrelationId);
    }

    [Fact]
    public async Task SubmitBatchTrace_TraceIdUsedAsMessageCorrelationId()
    {
        var command = new SubmitBatchTraceCommand
        {
            Items = new[]
            {
                new TraceRequestDto { CompanyName = "Test Co.", Country = "GB" },
            },
            Source = "test",
        };

        TraceRequestMessage? capturedMessage = null;
        await _serviceBusPublisher
            .EnqueueTraceRequestAsync(
                Arg.Do<TraceRequestMessage>(m => capturedMessage = m),
                Arg.Any<CancellationToken>())
            .ConfigureAwait(true);

        var result = await _mediator.Send(command).ConfigureAwait(true);

        var traceId = result.Items.Single().TraceId;
        capturedMessage.Should().NotBeNull();
        capturedMessage!.CorrelationId.Should().Be(traceId.ToString(),
            "batch handler uses TraceId as CorrelationId for SB request-reply matching");
    }

    [Fact]
    public async Task SubmitBatchTrace_PublishFails_ReturnsFailed()
    {
        _serviceBusPublisher
            .EnqueueTraceRequestAsync(Arg.Any<TraceRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("SB unavailable")));

        var command = new SubmitBatchTraceCommand
        {
            Items = new[]
            {
                new TraceRequestDto { CompanyName = "Fail Co." },
            },
            Source = "test",
        };

        var result = await _mediator.Send(command).ConfigureAwait(true);

        result.Items.Single().Status.Should().Be(DomainEnums.TraceStatus.Failed,
            "items that fail to publish are returned as Failed so the caller can retry");
    }

    // ── ContractMappingExtensions integration ─────────────────────────────────

    [Fact]
    public void TraceRequestMessage_ToTraceRequestDto_SourceFieldNotMapped()
    {
        // Source is top-level on SubmitTraceCommand, not inside TraceRequestDto.
        // Verify that DTO mapping does NOT include Source (it comes from the command).
        var message = new TraceRequestMessage
        {
            CorrelationId = "corr-src-test",
            CompanyName = "Test Co.",
            Source = "fieldforce",
        };

        var dto = message.ToTraceRequestDto();

        // TraceRequestDto has no Source property — the consumer sets it on the command.
        dto.CompanyName.Should().Be("Test Co.");
    }

    [Fact]
    public void TraceResultDto_ToResponseMessage_CorrelationIdRoundTrips()
    {
        // Verify the exact correlationId from the SB message header is echoed back.
        const string originalCorrelationId = "ff-req-00099";
        var resultDto = new TraceResultDto
        {
            TraceId = Guid.NewGuid(),
            Status = DomainEnums.TraceStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            OverallConfidence = 0.95,
            DurationMs = 1500,
        };

        var response = resultDto.ToResponseMessage(originalCorrelationId);

        response.CorrelationId.Should().Be(originalCorrelationId);
        response.Status.Should().Be(ContractsEnums.TraceStatus.Completed);
        response.OverallConfidence.Should().Be(0.95);
    }
}
