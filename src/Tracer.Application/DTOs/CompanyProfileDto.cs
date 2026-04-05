namespace Tracer.Application.DTOs;

/// <summary>
/// DTO for a CKB company profile (for directory/detail views).
/// </summary>
public sealed record CompanyProfileDto
{
    public required Guid Id { get; init; }
    public required string NormalizedKey { get; init; }
    public required string Country { get; init; }
    public string? RegistrationId { get; init; }
    public EnrichedCompanyDto? Enriched { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastEnrichedAt { get; init; }
    public DateTimeOffset? LastValidatedAt { get; init; }
    public int TraceCount { get; init; }
    public double? OverallConfidence { get; init; }
    public bool IsArchived { get; init; }
}
