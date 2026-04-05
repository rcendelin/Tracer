using Tracer.Application.DTOs;
using Tracer.Domain.ValueObjects;

namespace Tracer.Application.Mapping;

/// <summary>
/// Extension methods for mapping <see cref="TracedField{T}"/> domain value objects to DTOs.
/// </summary>
public static class TracedFieldMappingExtensions
{
    public static TracedFieldDto<string>? ToDto(this TracedField<string>? field) =>
        field is null ? null : new TracedFieldDto<string>
        {
            Value = field.Value,
            Confidence = field.Confidence.Value,
            Source = field.Source,
            EnrichedAt = field.EnrichedAt,
        };

    public static TracedFieldDto<AddressDto>? ToDto(this TracedField<Address>? field) =>
        field is null ? null : new TracedFieldDto<AddressDto>
        {
            Value = field.Value.ToDto(),
            Confidence = field.Confidence.Value,
            Source = field.Source,
            EnrichedAt = field.EnrichedAt,
        };

    public static TracedFieldDto<GeoCoordinateDto>? ToDto(this TracedField<GeoCoordinate>? field) =>
        field is null ? null : new TracedFieldDto<GeoCoordinateDto>
        {
            Value = field.Value.ToDto(),
            Confidence = field.Confidence.Value,
            Source = field.Source,
            EnrichedAt = field.EnrichedAt,
        };
}
