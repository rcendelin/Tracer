namespace Tracer.Infrastructure.Providers.GoogleMaps;

/// <summary>
/// HTTP client abstraction for the Google Maps Places API (New).
/// Base URL: <c>https://places.googleapis.com</c>
/// </summary>
internal interface IGoogleMapsClient
{
    /// <summary>
    /// Searches for places by text query.
    /// </summary>
    /// <param name="query">The search text, e.g. <c>"Škoda Auto Mladá Boleslav"</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of matching place results.</returns>
    Task<IReadOnlyCollection<PlaceResult>> SearchTextAsync(string query, CancellationToken cancellationToken);
}
