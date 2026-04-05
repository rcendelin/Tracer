using Tracer.Application.DTOs;
using Tracer.Domain.Entities;

namespace Tracer.Application.Mapping;

/// <summary>
/// Extension methods for mapping <see cref="ChangeEvent"/> to DTOs.
/// </summary>
public static class ChangeEventMappingExtensions
{
    public static ChangeEventDto ToDto(this ChangeEvent changeEvent)
    {
        ArgumentNullException.ThrowIfNull(changeEvent);
        return new ChangeEventDto
        {
            Id = changeEvent.Id,
        CompanyProfileId = changeEvent.CompanyProfileId,
        Field = changeEvent.Field,
        ChangeType = changeEvent.ChangeType,
        Severity = changeEvent.Severity,
        PreviousValueJson = changeEvent.PreviousValueJson,
        NewValueJson = changeEvent.NewValueJson,
        DetectedBy = changeEvent.DetectedBy,
        DetectedAt = changeEvent.DetectedAt,
            IsNotified = changeEvent.IsNotified,
        };
    }
}
