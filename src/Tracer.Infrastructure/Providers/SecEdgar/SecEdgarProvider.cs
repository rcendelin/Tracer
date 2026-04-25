using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;

namespace Tracer.Infrastructure.Providers.SecEdgar;

/// <summary>
/// Enrichment provider for SEC EDGAR (US publicly traded companies).
/// Provides registration data, SIC industry code, and business address.
/// </summary>
internal sealed partial class SecEdgarProvider : IEnrichmentProvider
{
    private readonly ISecEdgarClient _client;
    private readonly ILogger<SecEdgarProvider> _logger;

    public SecEdgarProvider(ISecEdgarClient client, ILogger<SecEdgarProvider> logger)
    {
        _client = client;
        _logger = logger;
    }

    public string ProviderId => "sec-edgar";
    public int Priority => 20;
    public double SourceQuality => 0.90;

    public bool CanHandle(TraceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Country == "US" && !string.IsNullOrWhiteSpace(context.Request.CompanyName);
    }

    public async Task<ProviderResult> EnrichAsync(TraceContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Search by name to find CIK
            var searchResults = await _client.SearchByNameAsync(
                context.Request.CompanyName!, cancellationToken).ConfigureAwait(false);

            var firstMatch = searchResults.FirstOrDefault();
            if (firstMatch?.EntityId is null)
            {
                LogNotFound(context.Request.CompanyName);
                return ProviderResult.NotFound(stopwatch.Elapsed);
            }

            // Get full submissions data
            var submissions = await _client.GetSubmissionsAsync(
                firstMatch.EntityId, cancellationToken).ConfigureAwait(false);

            if (submissions is null)
                return ProviderResult.NotFound(stopwatch.Elapsed);

            var fields = MapToFields(submissions);
            var rawJson = JsonSerializer.Serialize(submissions);

            LogSuccess(submissions.Cik, fields.Count);
            return ProviderResult.Success(fields, stopwatch.Elapsed, rawJson);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProviderResult.Timeout(stopwatch.Elapsed);
        }
        catch (HttpRequestException ex)
        {
            LogError(ex);
            return ProviderResult.Error("SEC EDGAR API call failed", stopwatch.Elapsed);
        }
    }

    private static Dictionary<FieldName, object?> MapToFields(EdgarSubmissions submissions)
    {
        var fields = new Dictionary<FieldName, object?>();

        if (!string.IsNullOrWhiteSpace(submissions.Name))
            fields[FieldName.LegalName] = submissions.Name;

        if (!string.IsNullOrWhiteSpace(submissions.Cik))
            fields[FieldName.RegistrationId] = $"CIK:{submissions.Cik}";

        if (!string.IsNullOrWhiteSpace(submissions.SicDescription))
            fields[FieldName.Industry] = $"{submissions.Sic} - {submissions.SicDescription}";
        else if (!string.IsNullOrWhiteSpace(submissions.Sic))
            fields[FieldName.Industry] = submissions.Sic;

        if (!string.IsNullOrWhiteSpace(submissions.EntityType))
            fields[FieldName.LegalForm] = submissions.EntityType;

        fields[FieldName.EntityStatus] = "active"; // If filed with SEC, assumed active

        if (!string.IsNullOrWhiteSpace(submissions.Ein))
            fields[FieldName.TaxId] = $"EIN:{submissions.Ein}";

        var addr = submissions.Addresses?.Business;
        if (addr is not null)
        {
            var street = string.Join(", ",
                new[] { addr.Street1, addr.Street2 }.Where(s => !string.IsNullOrWhiteSpace(s)));

            fields[FieldName.RegisteredAddress] = new Address
            {
                Street = street,
                City = addr.City ?? string.Empty,
                PostalCode = addr.ZipCode ?? string.Empty,
                Region = addr.StateOrCountry,
                Country = "US",
            };
        }

        return fields;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "SEC EDGAR: No match for '{Name}'")]
    private partial void LogNotFound(string? name);

    [LoggerMessage(Level = LogLevel.Information, Message = "SEC EDGAR: Enriched CIK {Cik} with {FieldCount} fields")]
    private partial void LogSuccess(string? cik, int fieldCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SEC EDGAR: Enrichment failed")]
    private partial void LogError(Exception ex);
}
