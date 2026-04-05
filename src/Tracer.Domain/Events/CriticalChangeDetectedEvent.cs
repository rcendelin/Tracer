using Tracer.Domain.Common;
using Tracer.Domain.Enums;

namespace Tracer.Domain.Events;

/// <summary>
/// Raised when a <see cref="ChangeSeverity.Critical"/> change is detected on a company profile.
/// Triggers immediate notification via Service Bus topic <c>tracer-changes</c>.
/// </summary>
/// <param name="CompanyProfileId">The ID of the affected company profile.</param>
/// <param name="Field">The field that triggered the critical change.</param>
/// <param name="NewValueJson">JSON-serialised new value of the field.</param>
public sealed record CriticalChangeDetectedEvent(
    Guid CompanyProfileId,
    FieldName Field,
    string? NewValueJson) : IDomainEvent;
