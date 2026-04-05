namespace Tracer.Infrastructure.Providers.AzureMaps;

/// <summary>
/// HTTP client abstraction for Azure Maps Geocoding API.
/// </summary>
internal interface IAzureMapsClient
{
    /// <summary>
    /// Geocodes an address string to a geographic coordinate.
    /// </summary>
    /// <param name="address">The address to geocode.</param>
    /// <param name="countryCode">Optional ISO 3166-1 alpha-2 country code to bias results.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The first geocoding result, or <see langword="null"/> if no match.</returns>
    Task<GeocodeFeature?> GeocodeAsync(string address, string? countryCode, CancellationToken cancellationToken);
}
