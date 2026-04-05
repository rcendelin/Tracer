using Tracer.Domain.Common;
using Tracer.Domain.Enums;

namespace Tracer.Domain.Entities;

/// <summary>
/// Records a single field-level change detected on a <see cref="CompanyProfile"/>
/// during enrichment or re-validation. Used for change history and notification tracking.
/// </summary>
public sealed class ChangeEvent : BaseEntity
{
    // EF Core parameterless constructor
    private ChangeEvent() { }

    public ChangeEvent(
        Guid companyProfileId,
        FieldName field,
        ChangeType changeType,
        ChangeSeverity severity,
        string? previousValueJson,
        string? newValueJson,
        string detectedBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detectedBy, nameof(detectedBy));

        CompanyProfileId = companyProfileId;
        Field = field;
        ChangeType = changeType;
        Severity = severity;
        PreviousValueJson = previousValueJson is { Length: > MaxValueJsonLength }
            ? previousValueJson[..MaxValueJsonLength]
            : previousValueJson;
        NewValueJson = newValueJson is { Length: > MaxValueJsonLength }
            ? newValueJson[..MaxValueJsonLength]
            : newValueJson;
        DetectedBy = detectedBy;
        DetectedAt = DateTimeOffset.UtcNow;
    }

    private const int MaxValueJsonLength = 4000;

    public Guid CompanyProfileId { get; private set; }
    public FieldName Field { get; private set; }
    public ChangeType ChangeType { get; private set; }
    public ChangeSeverity Severity { get; private set; }
    // TODO: GDPR — When Officers or contact person fields are added,
    // redact PII from these JSON values before storing/emitting.
    public string? PreviousValueJson { get; private set; }
    public string? NewValueJson { get; private set; }

    /// <summary>
    /// Gets the identifier of the provider or process that detected the change,
    /// e.g. <c>"ares"</c>, <c>"revalidation-scheduler"</c>.
    /// </summary>
    public string DetectedBy { get; private set; } = null!;

    public DateTimeOffset DetectedAt { get; private set; }

    /// <summary>
    /// Gets or sets whether a notification has been sent for this change event.
    /// Set by the notification dispatcher after successful delivery.
    /// </summary>
    public bool IsNotified { get; private set; }

    /// <summary>
    /// Marks this change event as notified after successful delivery.
    /// </summary>
    public void MarkNotified()
    {
        IsNotified = true;
    }
}
