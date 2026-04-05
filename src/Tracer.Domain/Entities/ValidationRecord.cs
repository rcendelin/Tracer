using Tracer.Domain.Common;
using Tracer.Domain.Enums;

namespace Tracer.Domain.Entities;

/// <summary>
/// Records the outcome of a re-validation pass on a <see cref="CompanyProfile"/>.
/// Created by the re-validation scheduler for audit and analytics.
/// </summary>
public sealed class ValidationRecord : BaseEntity
{
    // EF Core parameterless constructor
    private ValidationRecord() { }

    public ValidationRecord(
        Guid companyProfileId,
        ValidationType validationType,
        int fieldsChecked,
        int fieldsChanged,
        string providerId,
        long durationMs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId, nameof(providerId));

        if (fieldsChecked < 0)
            throw new ArgumentOutOfRangeException(nameof(fieldsChecked), fieldsChecked,
                "Fields checked count cannot be negative.");

        if (fieldsChanged < 0)
            throw new ArgumentOutOfRangeException(nameof(fieldsChanged), fieldsChanged,
                "Fields changed count cannot be negative.");

        if (durationMs < 0)
            throw new ArgumentOutOfRangeException(nameof(durationMs), durationMs,
                "Duration cannot be negative.");

        CompanyProfileId = companyProfileId;
        ValidationType = validationType;
        FieldsChecked = fieldsChecked;
        FieldsChanged = fieldsChanged;
        ProviderId = providerId;
        DurationMs = durationMs;
        ValidatedAt = DateTimeOffset.UtcNow;
    }

    public Guid CompanyProfileId { get; private set; }
    public ValidationType ValidationType { get; private set; }
    public int FieldsChecked { get; private set; }
    public int FieldsChanged { get; private set; }

    /// <summary>
    /// Gets the provider used for this validation pass.
    /// </summary>
    public string ProviderId { get; private set; } = null!;

    public long DurationMs { get; private set; }
    public DateTimeOffset ValidatedAt { get; private set; }
}
