using Tracer.Domain.Common;

namespace Tracer.Domain.Events;

/// <summary>
/// Raised when a new <see cref="Entities.CompanyProfile"/> is created in the CKB.
/// </summary>
/// <param name="CompanyProfileId">The ID of the newly created profile.</param>
/// <param name="NormalizedKey">The normalised lookup key for the profile.</param>
public sealed record ProfileCreatedEvent(
    Guid CompanyProfileId,
    string NormalizedKey) : IDomainEvent;
