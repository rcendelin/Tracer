namespace Tracer.Contracts.Enums;

/// <summary>
/// Lifecycle state of a trace request from submission through completion.
/// Returned in <see cref="Messages.TraceResponseMessage"/> on the <c>tracer-response</c> queue.
/// </summary>
public enum TraceStatus
{
    /// <summary>Request received, not yet picked up by the orchestrator.</summary>
    Pending = 0,

    /// <summary>Orchestrator is actively calling enrichment providers.</summary>
    InProgress = 1,

    /// <summary>All applicable providers completed; a full company profile was assembled.</summary>
    Completed = 2,

    /// <summary>
    /// Some providers completed but others failed or found no data.
    /// A partial profile is still returned — use <see cref="Models.EnrichedCompanyContract"/> fields
    /// to determine which data was successfully enriched.
    /// </summary>
    PartiallyCompleted = 3,

    /// <summary>
    /// All providers failed or no matching company was found.
    /// Check <see cref="Messages.TraceResponseMessage.FailureReason"/> for details.
    /// </summary>
    Failed = 4,

    /// <summary>Request was explicitly cancelled before or during processing.</summary>
    Cancelled = 5,

    /// <summary>
    /// Request was submitted via the batch API endpoint and is waiting in the Service Bus queue.
    /// Will transition to <see cref="InProgress"/> when the Tracer consumer picks it up.
    /// Batch submissions return this status immediately — poll <c>GET /api/trace/{traceId}</c>
    /// for the final result.
    /// </summary>
    Queued = 6,
}
