using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;

namespace Tracer.Infrastructure.Providers.CompaniesHouse;

/// <summary>
/// Enrichment provider for UK Companies House registry.
/// Handles GB/IE companies or CRN-format registration IDs.
/// </summary>
internal sealed partial class CompaniesHouseProvider : IEnrichmentProvider
{
    private readonly ICompaniesHouseClient _client;
    private readonly ILogger<CompaniesHouseProvider> _logger;

    public CompaniesHouseProvider(ICompaniesHouseClient client, ILogger<CompaniesHouseProvider> logger)
    {
        _client = client;
        _logger = logger;
    }

    public string ProviderId => "companies-house";
    public int Priority => 10;
    public double SourceQuality => 0.95;

    public bool CanHandle(TraceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Country is "GB" or "IE")
            return true;

        // UK CRN format: 8 chars (digits or 2-letter prefix + 6 digits)
        if (!string.IsNullOrWhiteSpace(context.Request.RegistrationId) &&
            CrnRegex().IsMatch(context.Request.RegistrationId))
            return true;

        return false;
    }

    public async Task<ProviderResult> EnrichAsync(TraceContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var companyNumber = context.Request.RegistrationId;

            // Search by name if no CRN
            if (string.IsNullOrWhiteSpace(companyNumber) && !string.IsNullOrWhiteSpace(context.Request.CompanyName))
            {
                var searchResults = await _client.SearchByNameAsync(
                    context.Request.CompanyName, cancellationToken).ConfigureAwait(false);

                companyNumber = searchResults.FirstOrDefault()?.CompanyNumber;
            }

            if (string.IsNullOrWhiteSpace(companyNumber))
            {
                LogNotFound();
                return ProviderResult.NotFound(stopwatch.Elapsed);
            }

            var company = await _client.GetCompanyAsync(companyNumber, cancellationToken).ConfigureAwait(false);

            if (company is null)
                return ProviderResult.NotFound(stopwatch.Elapsed);

            var fields = MapToFields(company);
            var rawJson = JsonSerializer.Serialize(company);

            LogSuccess(companyNumber, fields.Count);
            return ProviderResult.Success(fields, stopwatch.Elapsed, rawJson);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProviderResult.Timeout(stopwatch.Elapsed);
        }
        catch (HttpRequestException ex)
        {
            LogError(ex);
            return ProviderResult.Error("Companies House API call failed", stopwatch.Elapsed);
        }
    }

    private static Dictionary<FieldName, object?> MapToFields(CompaniesHouseCompanyProfile company)
    {
        var fields = new Dictionary<FieldName, object?>();

        if (!string.IsNullOrWhiteSpace(company.CompanyName))
            fields[FieldName.LegalName] = company.CompanyName;

        if (!string.IsNullOrWhiteSpace(company.CompanyNumber))
            fields[FieldName.RegistrationId] = company.CompanyNumber;

        if (!string.IsNullOrWhiteSpace(company.Type))
            fields[FieldName.LegalForm] = company.Type;

        if (!string.IsNullOrWhiteSpace(company.CompanyStatus))
            fields[FieldName.EntityStatus] = MapEntityStatus(company.CompanyStatus);

        if (company.SicCodes?.Count > 0)
            fields[FieldName.Industry] = string.Join(", ", company.SicCodes);

        if (company.RegisteredOfficeAddress is not null)
            fields[FieldName.RegisteredAddress] = MapAddress(company.RegisteredOfficeAddress);

        return fields;
    }

    private static string MapEntityStatus(string status) => status.ToUpperInvariant() switch
    {
        "ACTIVE" => "active",
        "DISSOLVED" => "dissolved",
        "LIQUIDATION" => "liquidation",
        "RECEIVERSHIP" => "receivership",
        "ADMINISTRATION" => "administration",
        _ => status,
    };

    private static Address MapAddress(CompanyAddress addr)
    {
        var street = string.Join(", ",
            new[] { addr.AddressLine1, addr.AddressLine2 }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

        return new Address
        {
            Street = street,
            City = addr.Locality ?? string.Empty,
            PostalCode = addr.PostalCode ?? string.Empty,
            Region = addr.Region,
            Country = addr.Country ?? "GB",
        };
    }

    // UK Company Registration Number: 8 chars — all digits or 2-letter prefix + 6 digits
    [GeneratedRegex(@"^([A-Z]{2}\d{6}|\d{8})$", RegexOptions.IgnoreCase)]
    private static partial Regex CrnRegex();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Companies House: No match found")]
    private partial void LogNotFound();

    [LoggerMessage(Level = LogLevel.Information, Message = "Companies House: Enriched CRN {CompanyNumber} with {FieldCount} fields")]
    private partial void LogSuccess(string companyNumber, int fieldCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Companies House: Enrichment failed")]
    private partial void LogError(Exception ex);
}
