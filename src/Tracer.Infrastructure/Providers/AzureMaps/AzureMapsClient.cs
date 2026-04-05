using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Tracer.Infrastructure.Providers.AzureMaps;

/// <summary>
/// HTTP client for Azure Maps Geocoding API.
/// Uses <c>GET /geocode?api-version=2023-06-01&amp;query={address}</c>.
/// Subscription key is read from <c>X-AzureMaps-Key</c> default header (configured in DI).
/// </summary>
internal sealed partial class AzureMapsClient : IAzureMapsClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AzureMapsClient> _logger;
    private readonly string _subscriptionKey;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public AzureMapsClient(HttpClient httpClient, ILogger<AzureMapsClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        // Read subscription key from the custom header set during DI configuration
        _subscriptionKey = httpClient.DefaultRequestHeaders.TryGetValues("X-AzureMaps-Key", out var values)
            ? values.First()
            : string.Empty;
    }

    public async Task<GeocodeFeature?> GeocodeAsync(
        string address, string? countryCode, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address, nameof(address));

        var encodedAddress = Uri.EscapeDataString(address.Trim());
        var url = $"geocode?api-version=2023-06-01&query={encodedAddress}&subscription-key={_subscriptionKey}";

        if (!string.IsNullOrWhiteSpace(countryCode))
            url += $"&countryRegion={Uri.EscapeDataString(countryCode)}";

        LogGeocoding(address);

        var response = await _httpClient.GetAsync(new Uri(url, UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<GeocodeResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return result?.Features?.FirstOrDefault();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Azure Maps: Geocoding '{Address}'")]
    private partial void LogGeocoding(string address);
}
