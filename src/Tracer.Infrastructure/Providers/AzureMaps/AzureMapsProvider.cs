using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;

namespace Tracer.Infrastructure.Providers.AzureMaps;

/// <summary>
/// Enrichment provider for Azure Maps Geocoding API.
/// Provides standardised address and GPS coordinates.
/// Free tier: 5,000 requests/day.
/// </summary>
internal sealed partial class AzureMapsProvider : IEnrichmentProvider
{
    private readonly IAzureMapsClient _client;
    private readonly ILogger<AzureMapsProvider> _logger;

    public AzureMapsProvider(IAzureMapsClient client, ILogger<AzureMapsProvider> logger)
    {
        _client = client;
        _logger = logger;
    }

    public string ProviderId => "azure-maps";
    public int Priority => 50;
    public double SourceQuality => 0.75;

    public bool CanHandle(TraceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Can geocode if we have an address or city+country, but don't already have Location
        var hasAddress = !string.IsNullOrWhiteSpace(context.Request.Address) ||
                         !string.IsNullOrWhiteSpace(context.Request.City);

        var alreadyHasLocation = context.AccumulatedFields.Contains(FieldName.Location);

        return hasAddress && !alreadyHasLocation;
    }

    public async Task<ProviderResult> EnrichAsync(TraceContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var query = BuildGeocodingQuery(context);
            if (string.IsNullOrWhiteSpace(query))
                return ProviderResult.NotFound(stopwatch.Elapsed);

            var feature = await _client.GeocodeAsync(query, context.Country, cancellationToken)
                .ConfigureAwait(false);

            if (feature is null)
            {
                LogNotFound(query);
                return ProviderResult.NotFound(stopwatch.Elapsed);
            }

            var fields = MapToFields(feature);

            if (fields.Count == 0)
                return ProviderResult.NotFound(stopwatch.Elapsed);

            var rawJson = JsonSerializer.Serialize(feature);
            LogSuccess(fields.Count);

            return ProviderResult.Success(fields, stopwatch.Elapsed, rawJson);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProviderResult.Timeout(stopwatch.Elapsed);
        }
        catch (HttpRequestException ex)
        {
            LogError(ex);
            return ProviderResult.Error("Azure Maps API call failed", stopwatch.Elapsed);
        }
    }

    private static string BuildGeocodingQuery(TraceContext context)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(context.Request.Address))
            parts.Add(context.Request.Address);

        if (!string.IsNullOrWhiteSpace(context.Request.City))
            parts.Add(context.Request.City);

        if (!string.IsNullOrWhiteSpace(context.Country))
            parts.Add(context.Country);

        var query = string.Join(", ", parts);
        return query.Length > MaxQueryLength ? query[..MaxQueryLength] : query;
    }

    private const int MaxQueryLength = 500;

    private static Dictionary<FieldName, object?> MapToFields(GeocodeFeature feature)
    {
        var fields = new Dictionary<FieldName, object?>();

        // GPS coordinates
        var coords = feature.Geometry?.Coordinates;
        if (coords is { Count: >= 2 })
        {
            var coordsList = coords.ToList();
            // GeoJSON: [longitude, latitude]
            fields[FieldName.Location] = GeoCoordinate.Create(
                latitude: coordsList[1],
                longitude: coordsList[0]);
        }

        // Standardised address
        var addr = feature.Properties?.Address;
        if (addr is not null)
        {
            fields[FieldName.OperatingAddress] = new Address
            {
                Street = addr.AddressLine ?? string.Empty,
                City = addr.Locality ?? string.Empty,
                PostalCode = addr.PostalCode ?? string.Empty,
                Region = addr.AdminDistricts?.FirstOrDefault()?.Name,
                Country = addr.CountryRegion?.Iso ?? string.Empty,
                FormattedAddress = addr.FormattedAddress,
            };
        }

        return fields;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Azure Maps: No geocoding result for '{Query}'")]
    private partial void LogNotFound(string query);

    [LoggerMessage(Level = LogLevel.Information, Message = "Azure Maps: Geocoded with {FieldCount} fields")]
    private partial void LogSuccess(int fieldCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Azure Maps: Geocoding failed")]
    private partial void LogError(Exception ex);
}
