using Tracer.Domain.Enums;

namespace Tracer.Application.DTOs;

/// <summary>
/// DTO for a field-level change event.
/// </summary>
public sealed record ChangeEventDto
{
    public required Guid Id { get; init; }
    public required Guid CompanyProfileId { get; init; }
    public required FieldName Field { get; init; }
    public required ChangeType ChangeType { get; init; }
    public required ChangeSeverity Severity { get; init; }
    public string? PreviousValueJson { get; init; }
    public string? NewValueJson { get; init; }
    public required string DetectedBy { get; init; }
    public required DateTimeOffset DetectedAt { get; init; }
    public bool IsNotified { get; init; }
}
