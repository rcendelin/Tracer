namespace Tracer.Application.DTOs;

/// <summary>
/// DTO for a single enriched field with provenance information.
/// </summary>
/// <typeparam name="T">The type of the field value.</typeparam>
public sealed record TracedFieldDto<T>
{
    public required T Value { get; init; }
    public required double Confidence { get; init; }
    public required string Source { get; init; }
    public required DateTimeOffset EnrichedAt { get; init; }
}
