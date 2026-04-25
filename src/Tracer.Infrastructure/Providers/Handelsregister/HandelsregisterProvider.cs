using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;

namespace Tracer.Infrastructure.Providers.Handelsregister;

/// <summary>
/// Enrichment provider that scrapes the German commercial register (Handelsregister.de)
/// to extract company registration data for German companies.
/// <para>
/// Priority 200 — Tier 2 (Registry scraping). Runs sequentially after all Tier 1 API providers
/// have completed. Requires at least <see cref="TraceDepth.Standard"/> depth.
/// </para>
/// <para>
/// CanHandle logic:
/// <list type="number">
///   <item>Country is "DE" (Germany) — primary match</item>
///   <item>RegistrationId matches German commercial register format (HRB/HRA + number) — fallback</item>
/// </list>
/// </para>
/// </summary>
internal sealed partial class HandelsregisterProvider(
    IHandelsregisterClient client,
    ILogger<HandelsregisterProvider> logger) : IEnrichmentProvider
{
    public string ProviderId => "handelsregister";
    public int Priority => 200;
    public double SourceQuality => 0.85;

    /// <inheritdoc />
    /// <remarks>
    /// Returns <see langword="true"/> when:
    /// <list type="bullet">
    ///   <item>Depth is at least Standard (Quick traces skip registry scraping).</item>
    ///   <item>Country is "DE", OR the RegistrationId matches a German register pattern (HRB/HRA).</item>
    ///   <item>At least a company name or registration ID is available to search.</item>
    /// </list>
    /// </remarks>
    public bool CanHandle(TraceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Depth < TraceDepth.Standard)
            return false;

        var hasSearchableInput = !string.IsNullOrWhiteSpace(context.Request.CompanyName)
            || !string.IsNullOrWhiteSpace(context.Request.RegistrationId);

        if (!hasSearchableInput)
            return false;

        // German company by country code
        if (string.Equals(context.Country, "DE", StringComparison.OrdinalIgnoreCase))
            return true;

        // Registration ID matches German format (HRB/HRA + number)
        if (!string.IsNullOrWhiteSpace(context.Request.RegistrationId) &&
            GermanRegisterIdRegex().IsMatch(context.Request.RegistrationId))
            return true;

        return false;
    }

    /// <inheritdoc />
    public async Task<ProviderResult> EnrichAsync(TraceContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Strategy 1: Direct lookup by register number (most precise)
            if (TryParseRegistrationId(context.Request.RegistrationId, out var regType, out var regNumber))
            {
                var detail = await client
                    .GetByRegisterNumberAsync(regType, regNumber, null, cancellationToken)
                    .ConfigureAwait(false);

                if (detail is not null)
                {
                    var fields = MapDetailToFields(detail);
                    if (fields.Count > 0)
                    {
                        LogSuccess(detail.RegistrationId, fields.Count);
                        return ProviderResult.Success(fields, stopwatch.Elapsed);
                    }
                }
            }

            // Strategy 2: Search by company name
            if (!string.IsNullOrWhiteSpace(context.Request.CompanyName))
            {
                var searchResults = await client
                    .SearchByNameAsync(context.Request.CompanyName, cancellationToken)
                    .ConfigureAwait(false);

                if (searchResults is not null && searchResults.Count > 0)
                {
                    // Take the first result (highest relevance from Handelsregister search)
                    var best = searchResults[0];
                    var fields = MapSearchResultToFields(best);
                    if (fields.Count > 0)
                    {
                        var regId = $"{best.RegisterType} {best.RegisterNumber}";
                        var fieldCount = fields.Count;
                        LogSuccess(regId, fieldCount);
                        return ProviderResult.Success(fields, stopwatch.Elapsed);
                    }
                }
            }

            var query = context.Request.CompanyName ?? context.Request.RegistrationId ?? "(unknown)";
            LogNotFound(query);
            return ProviderResult.NotFound(stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Polly AttemptTimeout — not caller cancellation
            return ProviderResult.Timeout(stopwatch.Elapsed);
        }
        catch (HttpRequestException ex)
        {
            LogHttpError(ex);
            return ProviderResult.Error("Handelsregister search failed", stopwatch.Elapsed);
        }
    }

    // ── Field mapping ───────────────────────────────────���────────────────────

    private static Dictionary<FieldName, object?> MapDetailToFields(HandelsregisterCompanyDetail detail)
    {
        var fields = new Dictionary<FieldName, object?>();

        if (!string.IsNullOrWhiteSpace(detail.CompanyName))
            fields[FieldName.LegalName] = detail.CompanyName;

        if (!string.IsNullOrWhiteSpace(detail.RegistrationId))
            fields[FieldName.RegistrationId] = detail.RegistrationId;

        if (!string.IsNullOrWhiteSpace(detail.LegalForm))
            fields[FieldName.LegalForm] = detail.LegalForm;

        if (!string.IsNullOrWhiteSpace(detail.Status))
            fields[FieldName.EntityStatus] = NormalizeStatus(detail.Status);

        if (HasMeaningfulAddress(detail))
        {
            fields[FieldName.RegisteredAddress] = new Address
            {
                Street = detail.Street ?? string.Empty,
                City = detail.City ?? string.Empty,
                PostalCode = detail.PostalCode ?? string.Empty,
                Country = "DE",
            };
        }

        return fields;
    }

    private static Dictionary<FieldName, object?> MapSearchResultToFields(HandelsregisterSearchResult result)
    {
        var fields = new Dictionary<FieldName, object?>();

        if (!string.IsNullOrWhiteSpace(result.CompanyName))
            fields[FieldName.LegalName] = result.CompanyName;

        fields[FieldName.RegistrationId] = $"{result.RegisterType} {result.RegisterNumber}";

        if (!string.IsNullOrWhiteSpace(result.Status))
            fields[FieldName.EntityStatus] = NormalizeStatus(result.Status);

        return fields;
    }

    private static bool HasMeaningfulAddress(HandelsregisterCompanyDetail detail) =>
        !string.IsNullOrWhiteSpace(detail.Street) || !string.IsNullOrWhiteSpace(detail.City);

    /// <summary>
    /// Normalizes German entity status to a canonical English form for consistent storage.
    /// </summary>
    private static string NormalizeStatus(string germanStatus) =>
        germanStatus.Trim() switch
        {
            var s when s.Equals("aktiv", StringComparison.OrdinalIgnoreCase)
                || s.Equals("eingetragen", StringComparison.OrdinalIgnoreCase) => "active",
            var s when s.Equals("gelöscht", StringComparison.OrdinalIgnoreCase)
                || s.Equals("erloschen", StringComparison.OrdinalIgnoreCase) => "dissolved",
            var s when s.Equals("aufgelöst", StringComparison.OrdinalIgnoreCase) => "in_liquidation",
            var s when s.Equals("insolvent", StringComparison.OrdinalIgnoreCase)
                || s.Equals("insolvenzverfahren", StringComparison.OrdinalIgnoreCase) => "insolvent",
            _ => germanStatus.Trim(),
        };

    // ── Registration ID parsing ──────────────────────────────────────────────

    private static bool TryParseRegistrationId(
        string? registrationId,
        out string registerType,
        out string registerNumber)
    {
        registerType = string.Empty;
        registerNumber = string.Empty;

        if (string.IsNullOrWhiteSpace(registrationId))
            return false;

        var match = GermanRegisterIdRegex().Match(registrationId.Trim());
        if (!match.Success)
            return false;

        registerType = match.Groups[1].Value.ToUpperInvariant();
        registerNumber = match.Groups[2].Value;
        return true;
    }

    /// <summary>
    /// Matches German commercial register identifiers: "HRB 6324", "HRA 1234", "GnR 567", "VR 12345".
    /// </summary>
    [GeneratedRegex(@"^(HR[AB]|GnR|PR|VR)\s*(\d{1,7})$", RegexOptions.IgnoreCase)]
    private static partial Regex GermanRegisterIdRegex();

    // ── Logging ──────────────────────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Handelsregister provider: enriched '{RegistrationId}' with {FieldCount} fields")]
    private partial void LogSuccess(string registrationId, int fieldCount);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Handelsregister provider: no matching company found for '{Query}'")]
    private partial void LogNotFound(string query);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Handelsregister provider: HTTP error during enrichment")]
    private partial void LogHttpError(Exception ex);
}
