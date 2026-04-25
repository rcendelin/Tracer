namespace Tracer.Domain.ValueObjects;

/// <summary>
/// Represents a geographic coordinate (latitude/longitude pair) for a company location.
/// Use <see cref="Create"/> to obtain a validated instance.
/// The parameterless constructor exists solely for EF Core owned-type materialisation
/// and must not be used in application code.
/// </summary>
public sealed record GeoCoordinate
{
    /// <summary>
    /// Gets the latitude in decimal degrees.
    /// Valid range: −90.0 (South Pole) to +90.0 (North Pole).
    /// </summary>
    public required double Latitude { get; init; }

    /// <summary>
    /// Gets the longitude in decimal degrees.
    /// Valid range: −180.0 (West) to +180.0 (East).
    /// </summary>
    public required double Longitude { get; init; }

    /// <summary>
    /// Parameterless constructor required by EF Core for owned-type materialisation.
    /// Marked <see langword="internal"/> so that only <c>Tracer.Infrastructure</c>
    /// (via <c>InternalsVisibleTo</c>) and test projects can invoke it.
    /// Application and API layers must use <see cref="Create"/> instead.
    /// </summary>
    internal GeoCoordinate() { }

    /// <summary>
    /// Creates a validated <see cref="GeoCoordinate"/> instance.
    /// </summary>
    /// <param name="latitude">Decimal degrees latitude, must be in [−90, +90].</param>
    /// <param name="longitude">Decimal degrees longitude, must be in [−180, +180].</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="latitude"/> or <paramref name="longitude"/> is outside valid range.
    /// </exception>
    public static GeoCoordinate Create(double latitude, double longitude)
    {
        if (!double.IsFinite(latitude) || latitude is < -90.0 or > 90.0)
            throw new ArgumentOutOfRangeException(nameof(latitude), latitude,
                "Latitude must be a finite number between -90 and 90.");

        if (!double.IsFinite(longitude) || longitude is < -180.0 or > 180.0)
            throw new ArgumentOutOfRangeException(nameof(longitude), longitude,
                "Longitude must be a finite number between -180 and 180.");

        return new GeoCoordinate { Latitude = latitude, Longitude = longitude };
    }
}
