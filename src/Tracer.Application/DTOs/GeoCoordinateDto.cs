namespace Tracer.Application.DTOs;

/// <summary>
/// DTO for a geographic coordinate.
/// </summary>
public sealed record GeoCoordinateDto
{
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
}
