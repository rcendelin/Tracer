using Tracer.Domain.Enums;

namespace Tracer.Application.DTOs;

/// <summary>
/// Response DTO for a single item within a batch trace submission.
/// </summary>
public sealed record BatchTraceItemDto
{
    /// <summary>
    /// Unique identifier for this trace request.
    /// Use with <c>GET /api/trace/{traceId}</c> to poll for the enrichment result.
    /// </summary>
    public required Guid TraceId { get; init; }

    /// <summary>
    /// Caller-supplied correlation ID, if provided in the original request.
    /// Echoed back for request-reply matching on the consumer side.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Always <see cref="TraceStatus.Queued"/> for batch items — the request has been
    /// accepted and published to the Service Bus queue for async processing.
    /// </summary>
    public required TraceStatus Status { get; init; }
}

/// <summary>
/// Response DTO for a batch trace submission (<c>POST /api/trace/batch</c>).
/// All items are accepted immediately; actual enrichment happens asynchronously.
/// </summary>
public sealed record BatchTraceResultDto
{
    /// <summary>Per-item results, in the same order as the submitted requests.</summary>
    public required IReadOnlyCollection<BatchTraceItemDto> Items { get; init; }

    /// <summary>Total number of items accepted in this batch.</summary>
    public int Count => Items.Count;
}
