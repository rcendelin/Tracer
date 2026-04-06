using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Tracer.Application.Commands.SubmitTrace;
using Tracer.Application.DTOs;
using Tracer.Application.Services;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;

namespace Tracer.Application.Tests.Commands.SubmitTrace;

public sealed class SubmitTraceHandlerTests
{
    private readonly ITraceRequestRepository _traceRequestRepo = Substitute.For<ITraceRequestRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IWaterfallOrchestrator _orchestrator = Substitute.For<IWaterfallOrchestrator>();
    private readonly IWebhookCallbackService _webhookService = Substitute.For<IWebhookCallbackService>();
    private readonly ILogger<SubmitTraceHandler> _logger = Substitute.For<ILogger<SubmitTraceHandler>>();

    private SubmitTraceHandler CreateSut() => new(_traceRequestRepo, _unitOfWork, _orchestrator, _webhookService, _logger);

    private static SubmitTraceCommand CreateCommand(string companyName = "Acme s.r.o.", string source = "rest-api") =>
        new()
        {
            Input = new TraceRequestDto
            {
                CompanyName = companyName,
                Country = "CZ",
                RegistrationId = "12345678",
                Depth = TraceDepth.Standard,
            },
            Source = source,
        };

    private static CompanyProfile CreateProfile()
    {
        var profile = new CompanyProfile("CZ:12345678", "CZ", "12345678");
        profile.SetOverallConfidence(Confidence.Create(0.85));
        return profile;
    }

    [Fact]
    public async Task Handle_SuccessfulTrace_ReturnsCompletedResult()
    {
        var sut = CreateSut();
        var command = CreateCommand();
        var profile = CreateProfile();

        _orchestrator.ExecuteAsync(Arg.Any<TraceRequest>(), Arg.Any<CancellationToken>())
            .Returns(profile);

        var result = await sut.Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.Status.Should().Be(TraceStatus.Completed);
        result.OverallConfidence.Should().Be(0.85);
        result.Company.Should().NotBeNull();
        result.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task Handle_PersistsTraceRequest()
    {
        var sut = CreateSut();
        var command = CreateCommand();
        _orchestrator.ExecuteAsync(Arg.Any<TraceRequest>(), Arg.Any<CancellationToken>())
            .Returns(CreateProfile());

        await sut.Handle(command, CancellationToken.None);

        await _traceRequestRepo.Received(1).AddAsync(Arg.Any<TraceRequest>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>()); // once after add, once after complete
    }

    [Fact]
    public async Task Handle_OrchestratorThrows_ReturnsFailedResult()
    {
        var sut = CreateSut();
        var command = CreateCommand();
        _orchestrator.ExecuteAsync(Arg.Any<TraceRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("All providers failed"));

        var result = await sut.Handle(command, CancellationToken.None);

        result.Status.Should().Be(TraceStatus.Failed);
        result.FailureReason.Should().Contain("All providers failed");
        result.Company.Should().BeNull();
    }

    [Fact]
    public async Task Handle_OrchestratorThrows_StillSavesChanges()
    {
        var sut = CreateSut();
        var command = CreateCommand();
        _orchestrator.ExecuteAsync(Arg.Any<TraceRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Error"));

        await sut.Handle(command, CancellationToken.None);

        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OperationCancelled_Propagates()
    {
        var sut = CreateSut();
        var command = CreateCommand();
        _orchestrator.ExecuteAsync(Arg.Any<TraceRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var act = () => sut.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Handle_SetsCorrectSource()
    {
        var sut = CreateSut();
        var command = CreateCommand(source: "ui");
        _orchestrator.ExecuteAsync(Arg.Any<TraceRequest>(), Arg.Any<CancellationToken>())
            .Returns(CreateProfile());

        await sut.Handle(command, CancellationToken.None);

        await _traceRequestRepo.Received(1).AddAsync(
            Arg.Is<TraceRequest>(r => r.Source == "ui"),
            Arg.Any<CancellationToken>());
    }
}
