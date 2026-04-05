using Tracer.Domain.Common;
using Tracer.Domain.Enums;

namespace Tracer.Domain.Events;

/// <summary>
/// Raised when a field value on a <see cref="Entities.CompanyProfile"/> changes
/// during enrichment or re-validation. One event per changed field.
/// </summary>
/// <param name="CompanyProfileId">The ID of the affected company profile.</param>
/// <param name="Field">The field that changed.</param>
/// <param name="ChangeType">Whether the field was created, updated, or deleted.</param>
/// <param name="Severity">Business impact classification of the change.</param>
/// <param name="PreviousValueJson">JSON-serialised previous value, or <see langword="null"/> for new fields.</param>
/// <param name="NewValueJson">JSON-serialised new value, or <see langword="null"/> for deleted fields.</param>
public sealed record FieldChangedEvent(
    Guid CompanyProfileId,
    FieldName Field,
    ChangeType ChangeType,
    ChangeSeverity Severity,
    string? PreviousValueJson,
    string? NewValueJson) : IDomainEvent;
