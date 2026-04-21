using Tracer.Domain.Enums;

namespace Tracer.Application.DTOs;

/// <summary>
/// DTO projecting a CKB <c>CompanyProfile</c> into a row of the re-validation
/// queue view. Includes the fields whose TTL has expired so operators can see
/// why the profile is queued.
/// </summary>
public sealed record ValidationQueueItemDto
{
    public required Guid ProfileId { get; init; }
    public required string NormalizedKey { get; init; }
    public required string Country { get; init; }
    public required string? RegistrationId { get; init; }
    public required string? LegalName { get; init; }
    public required int TraceCount { get; init; }
    public required double? OverallConfidence { get; init; }
    public required DateTimeOffset? LastValidatedAt { get; init; }
    public required DateTimeOffset? NextFieldExpiryDate { get; init; }
    public required IReadOnlyCollection<FieldName> ExpiredFields { get; init; }
}
