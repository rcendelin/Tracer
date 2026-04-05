using Tracer.Domain.Enums;

namespace Tracer.Application.DTOs;

/// <summary>
/// DTO for a re-validation record.
/// </summary>
public sealed record ValidationRecordDto
{
    public required Guid Id { get; init; }
    public required Guid CompanyProfileId { get; init; }
    public required ValidationType ValidationType { get; init; }
    public required int FieldsChecked { get; init; }
    public required int FieldsChanged { get; init; }
    public required string ProviderId { get; init; }
    public required long DurationMs { get; init; }
    public required DateTimeOffset ValidatedAt { get; init; }
}
