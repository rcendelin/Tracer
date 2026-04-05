using Tracer.Domain.Common;
using Tracer.Domain.Enums;
using Tracer.Domain.ValueObjects;

namespace Tracer.Domain.Events;

/// <summary>
/// Raised when a <see cref="Entities.TraceRequest"/> transitions to a terminal state
/// (<see cref="TraceStatus.Completed"/>, <see cref="TraceStatus.PartiallyCompleted"/>,
/// or <see cref="TraceStatus.Failed"/>).
/// </summary>
/// <param name="TraceRequestId">The ID of the completed trace request.</param>
/// <param name="CompanyProfileId">
/// The ID of the enriched company profile, or <see langword="null"/> if the trace failed.
/// </param>
/// <param name="Status">The terminal status of the trace request.</param>
/// <param name="OverallConfidence">
/// The overall confidence of the enrichment result, or <see langword="null"/> if the trace failed.
/// </param>
public sealed record TraceCompletedEvent(
    Guid TraceRequestId,
    Guid? CompanyProfileId,
    TraceStatus Status,
    Confidence? OverallConfidence) : IDomainEvent;
