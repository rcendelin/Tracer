using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;

namespace Tracer.Infrastructure.Providers.Ares;

/// <summary>
/// Enrichment provider for the Czech ARES (Administrativní registr ekonomických subjektů) registry.
/// Handles companies registered in CZ/SK with IČO lookup or name search.
/// </summary>
internal sealed partial class AresProvider : IEnrichmentProvider
{
    private readonly IAresClient _client;
    private readonly ILogger<AresProvider> _logger;

    public AresProvider(IAresClient client, ILogger<AresProvider> logger)
    {
        _client = client;
        _logger = logger;
    }

    public string ProviderId => "ares";
    public int Priority => 10;
    public double SourceQuality => 0.95;

    public bool CanHandle(TraceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Handle CZ/SK countries
        if (context.Country is "CZ" or "SK")
            return true;

        // Handle if RegistrationId looks like a Czech IČO (8 digits)
        if (!string.IsNullOrWhiteSpace(context.Request.RegistrationId) &&
            IcoRegex().IsMatch(context.Request.RegistrationId))
            return true;

        return false;
    }

    public async Task<ProviderResult> EnrichAsync(TraceContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var ico = context.Request.RegistrationId;

            // If no IČO, try searching by name
            if (string.IsNullOrWhiteSpace(ico) && !string.IsNullOrWhiteSpace(context.Request.CompanyName))
            {
                ico = await _client.SearchByNameAsync(context.Request.CompanyName, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(ico))
            {
                LogNoIdentifier();
                return ProviderResult.NotFound(stopwatch.Elapsed);
            }

            var subject = await _client.GetByIcoAsync(ico, cancellationToken).ConfigureAwait(false);

            if (subject is null)
                return ProviderResult.NotFound(stopwatch.Elapsed);

            var fields = MapToFields(subject);
            var rawJson = JsonSerializer.Serialize(subject);

            LogEnrichmentSuccess(ico, fields.Count);

            return ProviderResult.Success(fields, stopwatch.Elapsed, rawJson);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProviderResult.Timeout(stopwatch.Elapsed);
        }
        catch (HttpRequestException ex)
        {
            LogEnrichmentError(ex);
            return ProviderResult.Error("ARES API call failed", stopwatch.Elapsed);
        }
    }

    private static Dictionary<FieldName, object?> MapToFields(AresEkonomickySubjekt subject)
    {
        var fields = new Dictionary<FieldName, object?>();

        if (!string.IsNullOrWhiteSpace(subject.ObchodniJmeno))
            fields[FieldName.LegalName] = subject.ObchodniJmeno;

        if (!string.IsNullOrWhiteSpace(subject.Ico))
            fields[FieldName.RegistrationId] = subject.Ico;

        if (!string.IsNullOrWhiteSpace(subject.Dic))
            fields[FieldName.TaxId] = subject.Dic;

        if (!string.IsNullOrWhiteSpace(subject.PravniForma))
            fields[FieldName.LegalForm] = subject.PravniForma;

        if (subject.DatumZaniku is not null)
            fields[FieldName.EntityStatus] = "dissolved";
        else
            fields[FieldName.EntityStatus] = "active";

        if (subject.CzNace?.Count > 0)
            fields[FieldName.Industry] = subject.CzNace.First();

        if (subject.Sidlo is not null)
            fields[FieldName.RegisteredAddress] = MapAddress(subject.Sidlo);

        return fields;
    }

    private static Address MapAddress(AresSidlo sidlo)
    {
        var street = BuildStreetLine(sidlo.NazevUlice, sidlo.CisloDomovni, sidlo.CisloOrientacni);

        return new Address
        {
            Street = street ?? string.Empty,
            City = sidlo.NazevObce ?? string.Empty,
            PostalCode = sidlo.Psc?.ToString("D5", CultureInfo.InvariantCulture) ?? string.Empty,
            Region = sidlo.NazevKraje,
            Country = sidlo.KodStatu ?? "CZ",
            FormattedAddress = sidlo.TextovaAdresa,
        };
    }

    private static string? BuildStreetLine(string? street, int? houseNumber, int? orientationNumber)
    {
        if (string.IsNullOrWhiteSpace(street) && houseNumber is null)
            return null;

        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(street))
            parts.Add(street);

        if (houseNumber.HasValue)
        {
            var number = houseNumber.Value.ToString(CultureInfo.InvariantCulture);
            if (orientationNumber.HasValue)
                number += "/" + orientationNumber.Value.ToString(CultureInfo.InvariantCulture);
            parts.Add(number);
        }

        return string.Join(" ", parts);
    }

    [GeneratedRegex(@"^\d{8}$")]
    private static partial Regex IcoRegex();

    [LoggerMessage(Level = LogLevel.Debug, Message = "ARES provider: No IČO or company name available")]
    private partial void LogNoIdentifier();

    [LoggerMessage(Level = LogLevel.Information, Message = "ARES provider: Enriched IČO {Ico} with {FieldCount} fields")]
    private partial void LogEnrichmentSuccess(string ico, int fieldCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ARES provider: Enrichment failed")]
    private partial void LogEnrichmentError(Exception ex);
}
