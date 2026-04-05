using Tracer.Application.DTOs;
using Tracer.Domain.Entities;

namespace Tracer.Application.Mapping;

/// <summary>
/// Extension methods for mapping <see cref="ValidationRecord"/> to DTOs.
/// </summary>
public static class ValidationRecordMappingExtensions
{
    public static ValidationRecordDto ToDto(this ValidationRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new ValidationRecordDto
        {
            Id = record.Id,
            CompanyProfileId = record.CompanyProfileId,
            ValidationType = record.ValidationType,
            FieldsChecked = record.FieldsChecked,
            FieldsChanged = record.FieldsChanged,
            ProviderId = record.ProviderId,
            DurationMs = record.DurationMs,
            ValidatedAt = record.ValidatedAt,
        };
    }
}
