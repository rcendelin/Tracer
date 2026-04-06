namespace Tracer.Contracts.Models;

/// <summary>
/// WGS-84 geographic coordinate pair.
/// </summary>
public sealed record GeoCoordinateContract
{
    /// <summary>Latitude in decimal degrees (-90 to +90).</summary>
    public required double Latitude { get; init; }

    /// <summary>Longitude in decimal degrees (-180 to +180).</summary>
    public required double Longitude { get; init; }
}
