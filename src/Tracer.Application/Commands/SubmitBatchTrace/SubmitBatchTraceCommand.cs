using MediatR;
using Tracer.Application.DTOs;

namespace Tracer.Application.Commands.SubmitBatchTrace;

/// <summary>
/// Command to submit multiple enrichment trace requests in a single batch.
/// Each item is enqueued to the Service Bus queue for async processing;
/// the response returns immediately with a <see cref="TraceStatus.Queued"/> status per item.
/// </summary>
/// <remarks>
/// Maximum batch size: 200 items.
/// Rate limit: 5 batches per minute per client IP.
/// Poll <c>GET /api/trace/{traceId}</c> for individual enrichment results.
/// </remarks>
public sealed record SubmitBatchTraceCommand : IRequest<BatchTraceResultDto>
{
    /// <summary>
    /// The enrichment requests to process. At least 1, at most 200.
    /// </summary>
    public required IReadOnlyCollection<TraceRequestDto> Items { get; init; }

    /// <summary>
    /// Source identifier for audit and rate-limit attribution.
    /// </summary>
    public string Source { get; init; } = "rest-api-batch";
}
