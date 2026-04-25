using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;

namespace Tracer.Infrastructure.Providers.AbnLookup;

/// <summary>
/// Enrichment provider for Australian Business Register (ABN Lookup).
/// Handles AU companies or 11-digit ABN registration IDs.
/// </summary>
internal sealed partial class AbnLookupProvider : IEnrichmentProvider
{
    private readonly IAbnLookupClient _client;
    private readonly ILogger<AbnLookupProvider> _logger;

    public AbnLookupProvider(IAbnLookupClient client, ILogger<AbnLookupProvider> logger)
    {
        _client = client;
        _logger = logger;
    }

    public string ProviderId => "abn-lookup";
    public int Priority => 10;
    public double SourceQuality => 0.90;

    public bool CanHandle(TraceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Country == "AU")
            return true;

        // ABN format: 11 digits
        if (!string.IsNullOrWhiteSpace(context.Request.RegistrationId) &&
            AbnRegex().IsMatch(context.Request.RegistrationId.Replace(" ", "", StringComparison.Ordinal)))
            return true;

        return false;
    }

    public async Task<ProviderResult> EnrichAsync(TraceContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var abn = context.Request.RegistrationId?.Replace(" ", "", StringComparison.Ordinal);

            // Search by name if no ABN
            if (string.IsNullOrWhiteSpace(abn) && !string.IsNullOrWhiteSpace(context.Request.CompanyName))
            {
                var searchResults = await _client.SearchByNameAsync(
                    context.Request.CompanyName, cancellationToken).ConfigureAwait(false);

                abn = searchResults.FirstOrDefault()?.Abn;
            }

            if (string.IsNullOrWhiteSpace(abn))
            {
                LogNotFound();
                return ProviderResult.NotFound(stopwatch.Elapsed);
            }

            var details = await _client.GetByAbnAsync(abn, cancellationToken).ConfigureAwait(false);

            if (details is null)
                return ProviderResult.NotFound(stopwatch.Elapsed);

            var fields = MapToFields(details);
            var rawJson = JsonSerializer.Serialize(details);

            LogSuccess(abn, fields.Count);
            return ProviderResult.Success(fields, stopwatch.Elapsed, rawJson);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProviderResult.Timeout(stopwatch.Elapsed);
        }
        catch (HttpRequestException ex)
        {
            LogError(ex);
            return ProviderResult.Error("ABN Lookup API call failed", stopwatch.Elapsed);
        }
    }

    private static Dictionary<FieldName, object?> MapToFields(AbnDetailsResponse details)
    {
        var fields = new Dictionary<FieldName, object?>();

        if (!string.IsNullOrWhiteSpace(details.EntityName))
            fields[FieldName.LegalName] = details.EntityName;

        if (!string.IsNullOrWhiteSpace(details.Abn))
            fields[FieldName.RegistrationId] = details.Abn;

        if (!string.IsNullOrWhiteSpace(details.EntityTypeName))
            fields[FieldName.LegalForm] = details.EntityTypeName;

        if (!string.IsNullOrWhiteSpace(details.AbnStatus))
            fields[FieldName.EntityStatus] = details.AbnStatus.Equals("Active", StringComparison.OrdinalIgnoreCase)
                ? "active" : "cancelled";

        if (!string.IsNullOrWhiteSpace(details.Gst))
            fields[FieldName.TaxId] = $"GST:{details.Gst}";

        if (!string.IsNullOrWhiteSpace(details.AddressState) || !string.IsNullOrWhiteSpace(details.AddressPostcode))
        {
            fields[FieldName.RegisteredAddress] = new Address
            {
                Street = string.Empty,
                City = string.Empty,
                PostalCode = details.AddressPostcode ?? string.Empty,
                Region = details.AddressState,
                Country = "AU",
            };
        }

        return fields;
    }

    [GeneratedRegex(@"^\d{11}$")]
    private static partial Regex AbnRegex();

    [LoggerMessage(Level = LogLevel.Debug, Message = "ABN Lookup: No match found")]
    private partial void LogNotFound();

    [LoggerMessage(Level = LogLevel.Information, Message = "ABN Lookup: Enriched ABN {Abn} with {FieldCount} fields")]
    private partial void LogSuccess(string abn, int fieldCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ABN Lookup: Enrichment failed")]
    private partial void LogError(Exception ex);
}
