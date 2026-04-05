using MediatR;
using Microsoft.Extensions.Logging;
using Tracer.Application.DTOs;
using Tracer.Application.Mapping;
using Tracer.Application.Services;
using Tracer.Domain.Entities;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;

namespace Tracer.Application.Commands.SubmitTrace;

/// <summary>
/// Handles the <see cref="SubmitTraceCommand"/> by persisting the request,
/// executing the waterfall enrichment pipeline, and returning the result.
/// </summary>
public sealed partial class SubmitTraceHandler : IRequestHandler<SubmitTraceCommand, TraceResultDto>
{
    private readonly ITraceRequestRepository _traceRequestRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWaterfallOrchestrator _orchestrator;
    private readonly ILogger<SubmitTraceHandler> _logger;

    public SubmitTraceHandler(
        ITraceRequestRepository traceRequestRepository,
        IUnitOfWork unitOfWork,
        IWaterfallOrchestrator orchestrator,
        ILogger<SubmitTraceHandler> logger)
    {
        _traceRequestRepository = traceRequestRepository;
        _unitOfWork = unitOfWork;
        _orchestrator = orchestrator;
        _logger = logger;
    }

    public async Task<TraceResultDto> Handle(SubmitTraceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var input = command.Input;

        var traceRequest = new TraceRequest(
            companyName: input.CompanyName,
            phone: input.Phone,
            email: input.Email,
            website: input.Website,
            address: input.Address,
            city: input.City,
            country: input.Country,
            registrationId: input.RegistrationId,
            taxId: input.TaxId,
            industryHint: input.IndustryHint,
            depth: input.Depth,
            callbackUrl: input.CallbackUrl,
            source: command.Source);

        traceRequest.MarkInProgress();
        await _traceRequestRepository.AddAsync(traceRequest, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        CompanyProfile? profile = null;
        try
        {
            profile = await _orchestrator.ExecuteAsync(traceRequest, cancellationToken).ConfigureAwait(false);

            var confidence = profile.OverallConfidence ?? Confidence.Zero;
            traceRequest.Complete(profile.Id, confidence);

            LogTraceCompleted(traceRequest.Id, profile.NormalizedKey, confidence.Value);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            traceRequest.Fail(ex.Message);

            LogTraceFailed(ex, traceRequest.Id, ex.Message);
        }

        // Use CancellationToken.None to ensure terminal state is always persisted,
        // even if the original token has been cancelled.
        await _unitOfWork.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

        return traceRequest.ToResultDto(profile);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Trace {TraceId} completed for {NormalizedKey} with confidence {Confidence}")]
    private partial void LogTraceCompleted(Guid traceId, string normalizedKey, double confidence);

    [LoggerMessage(Level = LogLevel.Error, Message = "Trace {TraceId} failed: {ErrorMessage}")]
    private partial void LogTraceFailed(Exception ex, Guid traceId, string errorMessage);
}
