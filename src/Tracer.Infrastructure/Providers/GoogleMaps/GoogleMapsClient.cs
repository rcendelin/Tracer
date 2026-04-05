using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Tracer.Infrastructure.Providers.GoogleMaps;

/// <summary>
/// HTTP client for the Google Maps Places API (New).
/// Uses <c>POST /v1/places:searchText</c> with field masks.
/// API key is passed via <c>X-Goog-Api-Key</c> header (configured in DI).
/// </summary>
internal sealed partial class GoogleMapsClient : IGoogleMapsClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GoogleMapsClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // Fields we request from the API — controls both response content and billing
    private const string FieldMask =
        "places.id,places.displayName,places.formattedAddress,places.addressComponents," +
        "places.location,places.nationalPhoneNumber,places.internationalPhoneNumber," +
        "places.websiteUri,places.types,places.businessStatus,places.primaryType";

    public GoogleMapsClient(HttpClient httpClient, ILogger<GoogleMapsClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<PlaceResult>> SearchTextAsync(
        string query, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query, nameof(query));

        LogSearching(query);

        var requestBody = new { textQuery = query.Trim(), maxResultCount = 3 };

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("v1/places:searchText", UriKind.Relative))
        {
            Content = JsonContent.Create(requestBody, options: JsonOptions),
        };
        request.Headers.Add("X-Goog-FieldMask", FieldMask);

        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<PlacesSearchResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return result?.Places ?? [];
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Google Maps: Searching '{Query}'")]
    private partial void LogSearching(string query);
}
