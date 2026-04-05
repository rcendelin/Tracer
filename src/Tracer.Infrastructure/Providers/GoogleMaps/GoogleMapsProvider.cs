using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;

namespace Tracer.Infrastructure.Providers.GoogleMaps;

/// <summary>
/// Enrichment provider for Google Maps Places API (New).
/// Provides operating address, phone, website, GPS location, and industry hint from place types.
/// </summary>
internal sealed partial class GoogleMapsProvider : IEnrichmentProvider
{
    private readonly IGoogleMapsClient _client;
    private readonly ILogger<GoogleMapsProvider> _logger;

    public GoogleMapsProvider(IGoogleMapsClient client, ILogger<GoogleMapsProvider> logger)
    {
        _client = client;
        _logger = logger;
    }

    public string ProviderId => "google-maps";
    public int Priority => 50;
    public double SourceQuality => 0.70;

    public bool CanHandle(TraceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return !string.IsNullOrWhiteSpace(context.Request.CompanyName) ||
               !string.IsNullOrWhiteSpace(context.Request.Address);
    }

    public async Task<ProviderResult> EnrichAsync(TraceContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var query = BuildSearchQuery(context);
            var results = await _client.SearchTextAsync(query, cancellationToken).ConfigureAwait(false);

            var place = results.FirstOrDefault();
            if (place is null)
            {
                LogNotFound(query);
                return ProviderResult.NotFound(stopwatch.Elapsed);
            }

            var fields = MapToFields(place);
            var rawJson = JsonSerializer.Serialize(place);

            LogSuccess(place.Id, fields.Count);
            return ProviderResult.Success(fields, stopwatch.Elapsed, rawJson);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProviderResult.Timeout(stopwatch.Elapsed);
        }
        catch (HttpRequestException ex)
        {
            LogError(ex);
            return ProviderResult.Error("Google Maps API call failed", stopwatch.Elapsed);
        }
    }

    private static string BuildSearchQuery(TraceContext context)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(context.Request.CompanyName))
            parts.Add(context.Request.CompanyName);

        if (!string.IsNullOrWhiteSpace(context.Request.Address))
            parts.Add(context.Request.Address);

        if (!string.IsNullOrWhiteSpace(context.Request.City))
            parts.Add(context.Request.City);

        if (!string.IsNullOrWhiteSpace(context.Country))
            parts.Add(context.Country);

        var query = string.Join(" ", parts);
        return query.Length > MaxQueryLength ? query[..MaxQueryLength] : query;
    }

    private const int MaxQueryLength = 500;

    private static Dictionary<FieldName, object?> MapToFields(PlaceResult place)
    {
        var fields = new Dictionary<FieldName, object?>();

        // Operating address from address components
        var address = MapAddress(place);
        if (address is not null)
            fields[FieldName.OperatingAddress] = address;

        // Phone
        var phone = place.InternationalPhoneNumber ?? place.NationalPhoneNumber;
        if (!string.IsNullOrWhiteSpace(phone))
            fields[FieldName.Phone] = phone;

        // Website
        if (!string.IsNullOrWhiteSpace(place.WebsiteUri))
            fields[FieldName.Website] = place.WebsiteUri;

        // GPS location
        if (place.Location is not null)
            fields[FieldName.Location] = GeoCoordinate.Create(place.Location.Latitude, place.Location.Longitude);

        // Industry hint from primary type
        if (!string.IsNullOrWhiteSpace(place.PrimaryType))
            fields[FieldName.Industry] = NormalizePlaceType(place.PrimaryType);

        return fields;
    }

    private static Address? MapAddress(PlaceResult place)
    {
        if (place.AddressComponents is null || place.AddressComponents.Count == 0)
        {
            // Fallback to formatted address
            if (!string.IsNullOrWhiteSpace(place.FormattedAddress))
                return new Address
                {
                    Street = string.Empty,
                    City = string.Empty,
                    PostalCode = string.Empty,
                    Country = string.Empty,
                    FormattedAddress = place.FormattedAddress,
                };
            return null;
        }

        var street = FindComponent(place.AddressComponents, "route");
        var streetNumber = FindComponent(place.AddressComponents, "street_number");
        var city = FindComponent(place.AddressComponents, "locality")
                ?? FindComponent(place.AddressComponents, "administrative_area_level_2");
        var postalCode = FindComponent(place.AddressComponents, "postal_code");
        var country = FindComponent(place.AddressComponents, "country");
        var region = FindComponent(place.AddressComponents, "administrative_area_level_1");

        var streetLine = string.IsNullOrWhiteSpace(street) && string.IsNullOrWhiteSpace(streetNumber)
            ? string.Empty
            : $"{street} {streetNumber}".Trim();

        return new Address
        {
            Street = streetLine,
            City = city ?? string.Empty,
            PostalCode = postalCode ?? string.Empty,
            Country = FindShortComponent(place.AddressComponents, "country") ?? string.Empty,
            Region = region,
            FormattedAddress = place.FormattedAddress,
        };
    }

    private static string? FindComponent(IReadOnlyCollection<AddressComponent> components, string type)
    {
        return components.FirstOrDefault(c => c.Types?.Contains(type) == true)?.LongText;
    }

    private static string? FindShortComponent(IReadOnlyCollection<AddressComponent> components, string type)
    {
        return components.FirstOrDefault(c => c.Types?.Contains(type) == true)?.ShortText;
    }

    private static string NormalizePlaceType(string placeType)
    {
        // Convert Google's snake_case types to readable form
        return placeType.Replace('_', ' ');
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Google Maps: No match for '{Query}'")]
    private partial void LogNotFound(string query);

    [LoggerMessage(Level = LogLevel.Information, Message = "Google Maps: Enriched place {PlaceId} with {FieldCount} fields")]
    private partial void LogSuccess(string? placeId, int fieldCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Google Maps: Enrichment failed")]
    private partial void LogError(Exception ex);
}
