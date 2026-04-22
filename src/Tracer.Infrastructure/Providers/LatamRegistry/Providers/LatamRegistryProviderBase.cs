using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;

namespace Tracer.Infrastructure.Providers.LatamRegistry.Providers;

/// <summary>
/// Shared implementation for per-country LATAM registry providers.
/// Concrete providers (<c>ArgentinaAfipProvider</c>, <c>ChileSiiProvider</c>,
/// <c>ColombiaRuesProvider</c>, <c>MexicoSatProvider</c>) own the country code,
/// identifier regex, status normalization and provider metadata; the base handles
/// the CanHandle guard, stopwatch + error discrimination in EnrichAsync, and the
/// mapping of <see cref="LatamRegistrySearchResult"/> to provider fields.
/// </summary>
internal abstract class LatamRegistryProviderBase : IEnrichmentProvider
{
    private readonly ILatamRegistryClient _client;
    private readonly ILogger _logger;

    protected LatamRegistryProviderBase(ILatamRegistryClient client, ILogger logger)
    {
        _client = client;
        _logger = logger;
    }

    public abstract string ProviderId { get; }
    public int Priority => 200;
    public double SourceQuality => 0.80;

    /// <summary>ISO-3166-1 alpha-2 country code (e.g. "AR").</summary>
    protected abstract string CountryCode { get; }

    /// <summary>Human-readable error message surfaced to <see cref="ProviderResult.Error(string, TimeSpan)"/>.</summary>
    protected abstract string GenericErrorMessage { get; }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="identifier"/> looks like
    /// this country's tax identifier format (CUIT / RUT / NIT / RFC). Used as a
    /// fallback in <see cref="CanHandle"/> when the request country is missing.
    /// </summary>
    protected abstract bool IsPossibleCountryIdentifier(string identifier);

    /// <summary>
    /// Maps a registry-specific status string to a canonical English value
    /// ("active", "dissolved", "suspended", etc.).
    /// </summary>
    protected abstract string NormalizeStatus(string status);

    /// <inheritdoc />
    public bool CanHandle(TraceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Depth < TraceDepth.Standard)
            return false;

        // Need a RegistrationId / TaxId to look up — LATAM endpoints are
        // identifier-based, not name-search.
        var identifier = ChooseIdentifier(context);
        if (string.IsNullOrWhiteSpace(identifier))
            return false;

        // Primary match: request country matches this provider.
        if (string.Equals(context.Country, CountryCode, StringComparison.OrdinalIgnoreCase))
            return true;

        // Fallback: identifier format matches this country.
        return IsPossibleCountryIdentifier(identifier);
    }

    /// <inheritdoc />
    public async Task<ProviderResult> EnrichAsync(TraceContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var identifier = ChooseIdentifier(context);
            if (string.IsNullOrWhiteSpace(identifier))
                return ProviderResult.NotFound(stopwatch.Elapsed);

            var result = await _client.LookupAsync(CountryCode, identifier, cancellationToken)
                .ConfigureAwait(false);

            if (result is null)
            {
                LogNotFound(_logger, CountryCode);
                return ProviderResult.NotFound(stopwatch.Elapsed);
            }

            var fields = MapToFields(result);

            if (fields.Count == 0)
                return ProviderResult.NotFound(stopwatch.Elapsed);

            LogSuccess(_logger, ProviderId, fields.Count);
            return ProviderResult.Success(fields, stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProviderResult.Timeout(stopwatch.Elapsed);
        }
        catch (HttpRequestException ex)
        {
            LogError(_logger, ProviderId, ex.GetType().Name);
            return ProviderResult.Error(GenericErrorMessage, stopwatch.Elapsed);
        }
    }

    // ── Field mapping ────────────────────────────────────────────────────────

    private Dictionary<FieldName, object?> MapToFields(LatamRegistrySearchResult result)
    {
        var fields = new Dictionary<FieldName, object?>();

        if (!string.IsNullOrWhiteSpace(result.EntityName))
            fields[FieldName.LegalName] = result.EntityName;

        // Prefix with country code for CKB consistency (e.g. "AR:30500010912").
        fields[FieldName.RegistrationId] = $"{result.CountryCode}:{result.RegistrationId}";

        if (!string.IsNullOrWhiteSpace(result.Status))
            fields[FieldName.EntityStatus] = NormalizeStatus(result.Status);

        if (!string.IsNullOrWhiteSpace(result.EntityType))
            fields[FieldName.LegalForm] = result.EntityType;

        return fields;
    }

    private static string? ChooseIdentifier(TraceContext context)
    {
        var id = context.Request.RegistrationId;
        return !string.IsNullOrWhiteSpace(id) ? id : context.Request.TaxId;
    }

    // ── Logging ──────────────────────────────────────────────────────────────
    //
    // Shared LoggerMessage delegates — base class cannot use LoggerMessage source
    // generator directly for non-partial classes, so we fall back to Define().

    private static readonly Action<ILogger, string, Exception?> LogNotFoundDelegate =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(9200, nameof(LogNotFound)),
            "LatamRegistry [{CountryCode}]: No match for identifier");

    private static readonly Action<ILogger, string, int, Exception?> LogSuccessDelegate =
        LoggerMessage.Define<string, int>(
            LogLevel.Information,
            new EventId(9201, nameof(LogSuccess)),
            "LatamRegistry [{ProviderId}]: Enriched with {FieldCount} fields");

    private static readonly Action<ILogger, string, string, Exception?> LogErrorDelegate =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(9202, nameof(LogError)),
            "LatamRegistry [{ProviderId}]: Enrichment failed ({ExceptionType})");

    private static void LogNotFound(ILogger logger, string countryCode) =>
        LogNotFoundDelegate(logger, countryCode, null);

    private static void LogSuccess(ILogger logger, string providerId, int fieldCount) =>
        LogSuccessDelegate(logger, providerId, fieldCount, null);

    private static void LogError(ILogger logger, string providerId, string exceptionType) =>
        LogErrorDelegate(logger, providerId, exceptionType, null);
}
