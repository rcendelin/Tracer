using MediatR;
using Microsoft.Extensions.Logging;
using Tracer.Application.DTOs;
using Tracer.Application.Mapping;
using Tracer.Application.Messaging;
using Tracer.Contracts.Messages;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Commands.SubmitBatchTrace;

/// <summary>
/// Handles <see cref="SubmitBatchTraceCommand"/> by persisting all trace requests
/// in a single transaction and publishing each to the Service Bus request queue.
/// Returns immediately with <see cref="TraceStatus.Queued"/> status per item.
/// </summary>
public sealed partial class SubmitBatchTraceHandler
    : IRequestHandler<SubmitBatchTraceCommand, BatchTraceResultDto>
{
    private readonly ITraceRequestRepository _traceRequestRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IServiceBusPublisher _serviceBus;
    private readonly ILogger<SubmitBatchTraceHandler> _logger;

    public SubmitBatchTraceHandler(
        ITraceRequestRepository traceRequestRepository,
        IUnitOfWork unitOfWork,
        IServiceBusPublisher serviceBus,
        ILogger<SubmitBatchTraceHandler> logger)
    {
        _traceRequestRepository = traceRequestRepository;
        _unitOfWork = unitOfWork;
        _serviceBus = serviceBus;
        _logger = logger;
    }

    public async Task<BatchTraceResultDto> Handle(
        SubmitBatchTraceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        LogBatchReceived(command.Items.Count, command.Source);

        // Phase 1: Build and persist all TraceRequest entities in one transaction.
        // MarkQueued() transitions from Pending → Queued immediately after construction.
        // Pair each entity with the caller-supplied CorrelationId for the response.
        var traceRequests = new List<(TraceRequest Entity, string? CallerCorrelationId)>(command.Items.Count);

        foreach (var item in command.Items)
        {
            var traceRequest = new TraceRequest(
                companyName: item.CompanyName,
                phone: item.Phone,
                email: item.Email,
                website: item.Website,
                address: item.Address,
                city: item.City,
                country: item.Country,
                registrationId: item.RegistrationId,
                taxId: item.TaxId,
                industryHint: item.IndustryHint,
                depth: item.Depth,
                callbackUrl: item.CallbackUrl,
                source: command.Source);

            traceRequest.MarkQueued();
            await _traceRequestRepository.AddAsync(traceRequest, cancellationToken).ConfigureAwait(false);
            traceRequests.Add((traceRequest, item.CorrelationId));
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        LogBatchPersisted(traceRequests.Count);

        // Phase 2: Publish each request to the Service Bus queue.
        // The ServiceBusConsumer will pick up each message and run the enrichment pipeline.
        // Best-effort: if publishing fails for some items, their TraceRequests remain in
        // Queued status and can be retried by a future reconciliation mechanism.
        var publishedCount = 0;
        var failedIds = new HashSet<Guid>();

        foreach (var (entity, _) in traceRequests)
        {
            try
            {
                var message = BuildTraceRequestMessage(entity);
                await _serviceBus.EnqueueTraceRequestAsync(message, cancellationToken).ConfigureAwait(false);
                publishedCount++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Log and continue — the TraceRequest remains Queued in DB.
                // A future reconciliation sweep can detect stuck Queued items and retry.
                LogPublishFailed(ex, entity.Id);
                failedIds.Add(entity.Id);
            }
        }

        if (publishedCount < traceRequests.Count)
            LogPublishPartialFailure(publishedCount, traceRequests.Count);
        else
            LogBatchPublished(publishedCount, traceRequests.Count, command.Source);

        // Phase 3: Build response — same order as input
        // Items that failed to publish are returned with Failed status so the caller
        // can identify which requests will NOT be processed automatically.
        var items = traceRequests.Select(pair => new BatchTraceItemDto
        {
            TraceId = pair.Entity.Id,
            CorrelationId = pair.CallerCorrelationId,
            Status = failedIds.Contains(pair.Entity.Id) ? TraceStatus.Failed : TraceStatus.Queued,
        }).ToList();

        return new BatchTraceResultDto { Items = items };
    }

    private static TraceRequestMessage BuildTraceRequestMessage(TraceRequest request) =>
        new()
        {
            CorrelationId = request.Id.ToString(), // TraceId as correlation for matching response
            CompanyName = request.CompanyName,
            Phone = request.Phone,
            Email = request.Email,
            Website = request.Website,
            Address = request.Address,
            City = request.City,
            Country = request.Country,
            RegistrationId = request.RegistrationId,
            TaxId = request.TaxId,
            IndustryHint = request.IndustryHint,
            Depth = MapTraceDepth(request.Depth),
            Source = request.Source,
        };

    private static Tracer.Contracts.Enums.TraceDepth MapTraceDepth(TraceDepth depth) =>
        depth switch
        {
            TraceDepth.Quick => Tracer.Contracts.Enums.TraceDepth.Quick,
            TraceDepth.Standard => Tracer.Contracts.Enums.TraceDepth.Standard,
            TraceDepth.Deep => Tracer.Contracts.Enums.TraceDepth.Deep,
            _ => throw new InvalidOperationException($"Unmapped TraceDepth value: {depth}. Update MapTraceDepth when adding new enum members."),
        };

    [LoggerMessage(Level = LogLevel.Information, Message = "Batch trace received: {Count} items from {Source}")]
    private partial void LogBatchReceived(int count, string source);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Batch persisted: {Count} TraceRequests in Queued status")]
    private partial void LogBatchPersisted(int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Batch published: {Published} of {Total} messages to Service Bus from {Source}")]
    private partial void LogBatchPublished(int published, int total, string source);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Batch publish: only {Published}/{Total} messages published; remaining items stuck in Queued status")]
    private partial void LogPublishPartialFailure(int published, int total);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to publish TraceRequest {TraceId} to Service Bus; item remains in Queued status")]
    private partial void LogPublishFailed(Exception ex, Guid traceId);
}
