using Tracer.Contracts.Enums;
using Tracer.Contracts.Models;

namespace Tracer.Contracts.Messages;

/// <summary>
/// Message sent by Tracer upon completion of an enrichment request.
/// </summary>
/// <remarks>
/// <para><strong>Transport:</strong> Azure Service Bus queue <c>tracer-response</c>.</para>
/// <para><strong>Serialisation:</strong> JSON (camelCase), UTF-8, Content-Type: <c>application/json</c>.</para>
/// <para><strong>Matching:</strong> Use <see cref="CorrelationId"/> to match this response
/// to the original <see cref="TraceRequestMessage"/> you sent.</para>
/// <para><strong>Idempotency:</strong> Use <see cref="TraceId"/> as a stable identifier when
/// storing the result — duplicate deliveries of the same message carry the same <see cref="TraceId"/>.</para>
/// <para><strong>Partial results:</strong> When <see cref="Status"/> is
/// <see cref="TraceStatus.PartiallyCompleted"/>, <see cref="Company"/> is populated but some
/// fields may be <see langword="null"/>. Check individual field confidence scores before
/// writing to the CRM.</para>
/// </remarks>
/// <example>
/// Successful response:
/// <code>
/// {
///   "traceId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
///   "correlationId": "ff-req-00042",
///   "status": 2,
///   "company": {
///     "legalName": { "value": "ACME s.r.o.", "confidence": 1.0, "source": "ares", "enrichedAt": "2026-04-06T08:00:00Z" },
///     "registeredAddress": { "value": { "street": "...", "city": "Praha", "postalCode": "110 00", "country": "CZ" }, "confidence": 1.0, "source": "ares", "enrichedAt": "2026-04-06T08:00:00Z" }
///   },
///   "overallConfidence": 0.93,
///   "completedAt": "2026-04-06T08:00:01.234Z",
///   "durationMs": 1234
/// }
/// </code>
/// </example>
public sealed record TraceResponseMessage
{
    /// <summary>Unique identifier of the trace request in Tracer.</summary>
    public required Guid TraceId { get; init; }

    /// <summary>Your correlation ID from the original <see cref="TraceRequestMessage"/>.</summary>
    public required string CorrelationId { get; init; }

    /// <summary>Final status of the enrichment.</summary>
    public required TraceStatus Status { get; init; }

    /// <summary>
    /// Enriched company data.
    /// Populated when <see cref="Status"/> is <see cref="TraceStatus.Completed"/>
    /// or <see cref="TraceStatus.PartiallyCompleted"/>.
    /// </summary>
    public EnrichedCompanyContract? Company { get; init; }

    /// <summary>
    /// Per-provider execution results for diagnostics and SLA monitoring.
    /// Always populated regardless of <see cref="Status"/>.
    /// </summary>
    public IReadOnlyCollection<SourceResultContract>? Sources { get; init; }

    /// <summary>
    /// Weighted average confidence score across all enriched fields, in [0.0, 1.0].
    /// <see langword="null"/> when <see cref="Status"/> is <see cref="TraceStatus.Failed"/>.
    /// </summary>
    public double? OverallConfidence { get; init; }

    /// <summary>UTC timestamp when the enrichment request was received.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>UTC timestamp when enrichment completed (or failed).</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Total wall-clock duration of the enrichment pipeline, in milliseconds.</summary>
    public long? DurationMs { get; init; }

    /// <summary>
    /// Human-readable failure reason when <see cref="Status"/> is <see cref="TraceStatus.Failed"/>.
    /// Never contains internal error details — only a sanitised description.
    /// </summary>
    public string? FailureReason { get; init; }
}
