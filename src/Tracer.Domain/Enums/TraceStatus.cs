namespace Tracer.Domain.Enums;

/// <summary>
/// Lifecycle state of a trace request from submission through enrichment completion.
/// </summary>
public enum TraceStatus
{
    /// <summary>Request received, not yet picked up by the orchestrator.</summary>
    Pending = 0,

    /// <summary>Orchestrator is actively running providers.</summary>
    InProgress = 1,

    /// <summary>All applicable providers completed and a profile was assembled.</summary>
    Completed = 2,

    /// <summary>At least one provider returned data but some fields could not be enriched.</summary>
    PartiallyCompleted = 3,

    /// <summary>Orchestrator failed to produce any result (all providers errored or no match found).</summary>
    Failed = 4,

    /// <summary>
    /// Request was explicitly cancelled before or during processing.
    /// Used for Service Bus dead-lettered messages, client-initiated cancellation,
    /// and duplicate detection scenarios in the batch pipeline.
    /// </summary>
    Cancelled = 5,
}
