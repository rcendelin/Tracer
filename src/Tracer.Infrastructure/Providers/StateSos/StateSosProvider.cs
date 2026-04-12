using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;

namespace Tracer.Infrastructure.Providers.StateSos;

/// <summary>
/// Enrichment provider for US Secretary of State business registries.
/// Supports California, Delaware, and New York via per-state adapters.
/// <para>
/// Priority 200 — Tier 2 (Registry scraping). Runs after SEC EDGAR (Priority 20, Tier 1).
/// Requires at least <see cref="TraceDepth.Standard"/> depth.
/// </para>
/// <para>
/// CanHandle logic:
/// <list type="number">
///   <item>Country is "US"</item>
///   <item>Depth is at least Standard</item>
///   <item>A company name is available to search</item>
///   <item>SEC EDGAR has NOT already enriched RegistrationId (avoids redundant work)</item>
/// </list>
/// </para>
/// </summary>
internal sealed partial class StateSosProvider : IEnrichmentProvider
{
    private readonly IStateSosClient _client;
    private readonly ILogger<StateSosProvider> _logger;

    public StateSosProvider(IStateSosClient client, ILogger<StateSosProvider> logger)
    {
        _client = client;
        _logger = logger;
    }

    public string ProviderId => "state-sos";
    public int Priority => 200;
    public double SourceQuality => 0.85;

    /// <inheritdoc />
    public bool CanHandle(TraceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Depth < TraceDepth.Standard)
            return false;

        if (!string.Equals(context.Country, "US", StringComparison.OrdinalIgnoreCase))
            return false;

        // Must have a company name (state registries are name-search based)
        if (string.IsNullOrWhiteSpace(context.Request.CompanyName))
            return false;

        // Skip if a higher-priority provider (SEC EDGAR) already enriched RegistrationId
        if (context.AccumulatedFields.Contains(FieldName.RegistrationId))
            return false;

        return true;
    }

    /// <inheritdoc />
    public async Task<ProviderResult> EnrichAsync(TraceContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Search across supported states (adapter dispatch)
            var results = await _client
                .SearchAsync(context.Request.CompanyName!, null, cancellationToken)
                .ConfigureAwait(false);

            if (results is null || results.Count == 0)
            {
                var query = context.Request.CompanyName ?? "(unknown)";
                LogNotFound(query);
                return ProviderResult.NotFound(stopwatch.Elapsed);
            }

            // Take the first (best) match
            var best = results[0];
            var fields = MapToFields(best);

            if (fields.Count == 0)
                return ProviderResult.NotFound(stopwatch.Elapsed);

            var regId = $"{best.StateCode}:{best.FilingNumber}";
            var fieldCount = fields.Count;
            LogSuccess(regId, fieldCount);

            return ProviderResult.Success(fields, stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProviderResult.Timeout(stopwatch.Elapsed);
        }
        catch (HttpRequestException ex)
        {
            LogError(ex);
            return ProviderResult.Error("State SoS search failed", stopwatch.Elapsed);
        }
    }

    // ── Field mapping ────────────────────────────────────────────────────────

    private static Dictionary<FieldName, object?> MapToFields(StateSosSearchResult result)
    {
        var fields = new Dictionary<FieldName, object?>();

        if (!string.IsNullOrWhiteSpace(result.EntityName))
            fields[FieldName.LegalName] = result.EntityName;

        // Prefix with state code for disambiguation (e.g. "CA:C0806592")
        fields[FieldName.RegistrationId] = $"{result.StateCode}:{result.FilingNumber}";

        if (!string.IsNullOrWhiteSpace(result.Status))
            fields[FieldName.EntityStatus] = NormalizeStatus(result.Status);

        if (!string.IsNullOrWhiteSpace(result.EntityType))
            fields[FieldName.LegalForm] = result.EntityType;

        return fields;
    }

    /// <summary>
    /// Normalizes US entity status strings to canonical English.
    /// Different states use different terminology for the same statuses.
    /// </summary>
    internal static string NormalizeStatus(string status) =>
        status.Trim() switch
        {
            var s when s.Equals("Active", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Good Standing", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Current", StringComparison.OrdinalIgnoreCase) => "active",
            var s when s.Equals("Dissolved", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Revoked", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Withdrawn", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Inactive", StringComparison.OrdinalIgnoreCase) => "dissolved",
            var s when s.Equals("Suspended", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Forfeited", StringComparison.OrdinalIgnoreCase) => "suspended",
            var s when s.Equals("Merged", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Converted", StringComparison.OrdinalIgnoreCase) => "merged",
            _ => status.Trim(),
        };

    // ── Logging ──────────────────────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "StateSoS provider: No match for '{Query}'")]
    private partial void LogNotFound(string query);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "StateSoS provider: Enriched '{RegistrationId}' with {FieldCount} fields")]
    private partial void LogSuccess(string registrationId, int fieldCount);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "StateSoS provider: Enrichment failed")]
    private partial void LogError(Exception ex);
}
