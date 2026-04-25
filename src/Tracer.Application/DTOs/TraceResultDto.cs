using Tracer.Domain.Enums;

namespace Tracer.Application.DTOs;

/// <summary>
/// Response DTO for a trace request result.
/// </summary>
public sealed record TraceResultDto
{
    /// <summary>Server-assigned trace identifier; use to poll <c>GET /api/trace/{traceId}</c>.</summary>
    public required Guid TraceId { get; init; }

    /// <summary>Current lifecycle status (Pending, Queued, InProgress, Completed, Failed, Cancelled).</summary>
    public required TraceStatus Status { get; init; }

    /// <summary>Enriched company profile. Populated once the waterfall completes (or partially on timeout).</summary>
    public EnrichedCompanyDto? Company { get; init; }

    /// <summary>Per-provider contribution (success/error, duration, returned fields).</summary>
    public IReadOnlyCollection<SourceResultDto>? Sources { get; init; }

    /// <summary>Aggregate confidence across all merged fields (0.0–1.0). Null for still-running traces.</summary>
    public double? OverallConfidence { get; init; }

    /// <summary>UTC timestamp when the trace request was accepted.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>UTC timestamp when the trace reached a terminal state.</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Wall-clock duration of the waterfall in milliseconds.</summary>
    public long? DurationMs { get; init; }

    /// <summary>Sanitized failure description when <see cref="Status"/> is <c>Failed</c>.</summary>
    public string? FailureReason { get; init; }
}
