using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;

namespace Tracer.Infrastructure.Providers.GleifLei;

/// <summary>
/// Enrichment provider for the GLEIF LEI registry — global coverage.
/// Provides legal name, addresses, entity status, legal form, and parent company chain.
/// </summary>
internal sealed partial class GleifProvider : IEnrichmentProvider
{
    private readonly IGleifClient _client;
    private readonly ILogger<GleifProvider> _logger;

    public GleifProvider(IGleifClient client, ILogger<GleifProvider> logger)
    {
        _client = client;
        _logger = logger;
    }

    public string ProviderId => "gleif-lei";
    public int Priority => 30;
    public double SourceQuality => 0.85;

    public bool CanHandle(TraceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // GLEIF is a global source — requires company name for search
        return !string.IsNullOrWhiteSpace(context.Request.CompanyName);
    }

    public async Task<ProviderResult> EnrichAsync(TraceContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            GleifLeiRecord? record = null;

            // Search by company name
            if (!string.IsNullOrWhiteSpace(context.Request.CompanyName))
            {
                var results = await _client.SearchByNameAsync(
                    context.Request.CompanyName, context.Country, cancellationToken)
                    .ConfigureAwait(false);

                record = results.FirstOrDefault();
            }

            if (record is null)
            {
                LogNotFound(context.Request.CompanyName);
                return ProviderResult.NotFound(stopwatch.Elapsed);
            }

            var fields = MapToFields(record);

            // Try to get parent company
            var lei = record.Attributes?.Lei;
            if (!string.IsNullOrWhiteSpace(lei))
            {
                var parent = await _client.GetDirectParentAsync(lei, cancellationToken)
                    .ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(parent?.Name))
                    fields[FieldName.ParentCompany] = parent.Name;
            }

            var rawJson = JsonSerializer.Serialize(record);
            LogSuccess(lei, fields.Count);

            return ProviderResult.Success(fields, stopwatch.Elapsed, rawJson);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProviderResult.Timeout(stopwatch.Elapsed);
        }
        catch (HttpRequestException ex)
        {
            LogError(ex);
            return ProviderResult.Error("GLEIF API call failed", stopwatch.Elapsed);
        }
    }

    private static Dictionary<FieldName, object?> MapToFields(GleifLeiRecord record)
    {
        var fields = new Dictionary<FieldName, object?>();
        var entity = record.Attributes?.Entity;

        if (entity is null)
            return fields;

        if (!string.IsNullOrWhiteSpace(entity.LegalName?.Name))
            fields[FieldName.LegalName] = entity.LegalName.Name;

        if (!string.IsNullOrWhiteSpace(entity.RegisteredAs))
            fields[FieldName.RegistrationId] = entity.RegisteredAs;

        if (!string.IsNullOrWhiteSpace(entity.Status))
            fields[FieldName.EntityStatus] = MapEntityStatus(entity.Status);

        var legalFormValue = entity.LegalForm?.Other ?? entity.LegalForm?.Id;
        if (!string.IsNullOrWhiteSpace(legalFormValue))
            fields[FieldName.LegalForm] = legalFormValue;

        if (entity.LegalAddress is not null)
            fields[FieldName.RegisteredAddress] = MapAddress(entity.LegalAddress);

        if (entity.HeadquartersAddress is not null)
            fields[FieldName.OperatingAddress] = MapAddress(entity.HeadquartersAddress);

        return fields;
    }

    private static string MapEntityStatus(string gleifStatus) => gleifStatus.ToUpperInvariant() switch
    {
        "ACTIVE" => "active",
        "INACTIVE" => "dissolved",
        _ => gleifStatus,
    };

    private static Address MapAddress(GleifAddress addr)
    {
        var streetLine = addr.AddressLines?.Count > 0
            ? string.Join(", ", addr.AddressLines)
            : string.Empty;

        if (!string.IsNullOrWhiteSpace(addr.AddressNumber))
            streetLine = string.IsNullOrWhiteSpace(streetLine)
                ? addr.AddressNumber
                : $"{streetLine} {addr.AddressNumber}";

        return new Address
        {
            Street = streetLine,
            City = addr.City ?? string.Empty,
            PostalCode = addr.PostalCode ?? string.Empty,
            Region = addr.Region,
            Country = addr.Country ?? string.Empty,
        };
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "GLEIF: No match found for '{Name}'")]
    private partial void LogNotFound(string? name);

    [LoggerMessage(Level = LogLevel.Information, Message = "GLEIF: Enriched LEI {Lei} with {FieldCount} fields")]
    private partial void LogSuccess(string? lei, int fieldCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "GLEIF: Enrichment failed")]
    private partial void LogError(Exception ex);
}
