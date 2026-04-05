using Tracer.Application.DTOs;
using Tracer.Domain.ValueObjects;

namespace Tracer.Application.Mapping;

/// <summary>
/// Extension methods for mapping domain value objects to DTOs.
/// </summary>
public static class ValueObjectMappingExtensions
{
    public static AddressDto ToDto(this Address address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return new AddressDto
        {
        Street = address.Street,
        City = address.City,
        PostalCode = address.PostalCode,
        Region = address.Region,
        Country = address.Country,
            FormattedAddress = address.FormattedAddress,
        };
    }

    public static GeoCoordinateDto ToDto(this GeoCoordinate coordinate)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        return new GeoCoordinateDto
        {
            Latitude = coordinate.Latitude,
            Longitude = coordinate.Longitude,
        };
    }
}
