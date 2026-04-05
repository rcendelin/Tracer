using Tracer.Domain.Enums;

namespace Tracer.Application.DTOs;

/// <summary>
/// DTO for a single provider execution result.
/// </summary>
public sealed record SourceResultDto
{
    public required string ProviderId { get; init; }
    public required SourceStatus Status { get; init; }
    public int FieldsEnriched { get; init; }
    public long DurationMs { get; init; }
    public string? ErrorMessage { get; init; }
}
