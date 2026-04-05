using Tracer.Domain.Enums;

namespace Tracer.Application.DTOs;

/// <summary>
/// Response DTO for a trace request result.
/// </summary>
public sealed record TraceResultDto
{
    public required Guid TraceId { get; init; }
    public required TraceStatus Status { get; init; }
    public EnrichedCompanyDto? Company { get; init; }
    public IReadOnlyCollection<SourceResultDto>? Sources { get; init; }
    public double? OverallConfidence { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public long? DurationMs { get; init; }
    public string? FailureReason { get; init; }
}
