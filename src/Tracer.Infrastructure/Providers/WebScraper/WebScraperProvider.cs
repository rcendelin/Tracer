using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;

namespace Tracer.Infrastructure.Providers.WebScraper;

/// <summary>
/// Enrichment provider that fetches a company's own website and extracts structured contact
/// and identity data using <see cref="IWebScraperClient"/> (AngleSharp HTML parsing).
/// <para>
/// Priority 150 — runs in Tier 2 after registry APIs (Tier 1) have had a chance to populate
/// the Website field. Requires at least <see cref="TraceDepth.Standard"/> depth.
/// </para>
/// <para>
/// URL resolution order:
/// <list type="number">
///   <item><see cref="TraceContext.Request"/>.Website — caller-supplied URL (highest priority)</item>
///   <item><see cref="TraceContext.ExistingProfile"/>.Website — URL from a previous trace stored in CKB</item>
/// </list>
/// </para>
/// </summary>
internal sealed partial class WebScraperProvider(
    IWebScraperClient scraper,
    ILogger<WebScraperProvider> logger) : IEnrichmentProvider
{
    public string ProviderId => "web-scraper";
    public int Priority => 150;
    public double SourceQuality => 0.50;

    /// <inheritdoc />
    /// <remarks>
    /// Returns <see langword="true"/> when a website URL is available (from the request or the
    /// CKB existing profile) and the trace depth is at least <see cref="TraceDepth.Standard"/>.
    /// Quick traces skip web scraping to stay within the ≤5 s latency target.
    /// </remarks>
    public bool CanHandle(TraceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Depth < TraceDepth.Standard)
            return false;

        // Accept the request's website, or whatever URL a previous trace stored in the CKB.
        return !string.IsNullOrWhiteSpace(context.Request.Website)
            || context.ExistingProfile?.Website?.Value is not null;
    }

    /// <inheritdoc />
    public async Task<ProviderResult> EnrichAsync(TraceContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var stopwatch = Stopwatch.StartNew();

        // Prefer the caller-supplied URL; fall back to the CKB value from a prior enrichment.
        var websiteUrl = !string.IsNullOrWhiteSpace(context.Request.Website)
            ? context.Request.Website.Trim()
            : context.ExistingProfile?.Website?.Value;

        if (string.IsNullOrWhiteSpace(websiteUrl))
            return ProviderResult.NotFound(stopwatch.Elapsed);

        try
        {
            var scraped = await scraper.ScrapeAsync(websiteUrl, cancellationToken).ConfigureAwait(false);

            if (scraped is null)
            {
                LogNotFound(websiteUrl);
                return ProviderResult.NotFound(stopwatch.Elapsed);
            }

            var fields = MapToFields(scraped);
            if (fields.Count == 0)
            {
                LogNoUsableFields(websiteUrl);
                return ProviderResult.NotFound(stopwatch.Elapsed);
            }

            LogSuccess(websiteUrl, fields.Count);
            return ProviderResult.Success(fields, stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Polly AttemptTimeout — not caller cancellation
            return ProviderResult.Timeout(stopwatch.Elapsed);
        }
        catch (HttpRequestException ex)
        {
            LogHttpError(ex, websiteUrl);
            return ProviderResult.Error("Web scraping request failed", stopwatch.Elapsed);
        }
    }

    // ── Field mapping ────────────────────────────────────────────────────────

    private static Dictionary<FieldName, object?> MapToFields(WebScrapingResult scraped)
    {
        var fields = new Dictionary<FieldName, object?>();

        // Company name — included but lower-weighted; GoldenRecordMerger prefers registry sources.
        if (!string.IsNullOrWhiteSpace(scraped.CompanyName))
            fields[FieldName.LegalName] = scraped.CompanyName;

        if (!string.IsNullOrWhiteSpace(scraped.Phone))
            fields[FieldName.Phone] = scraped.Phone;

        if (!string.IsNullOrWhiteSpace(scraped.Email))
            fields[FieldName.Email] = scraped.Email;

        // Only record Website when JSON-LD/OG provided a canonical URL that differs from what
        // we were given. SourceUrl is already known to the caller — storing it back adds nothing.
        if (!string.IsNullOrWhiteSpace(scraped.Website))
            fields[FieldName.Website] = scraped.Website;

        if (!string.IsNullOrWhiteSpace(scraped.Industry))
            fields[FieldName.Industry] = scraped.Industry;

        // Only emit an address if we have at least one meaningful component.
        // A web page address is the operational location, not necessarily the registered office.
        if (scraped.Address is { } addr && HasMeaningfulAddress(addr))
        {
            fields[FieldName.OperatingAddress] = new Address
            {
                Street = addr.Street ?? string.Empty,
                City = addr.City ?? string.Empty,
                PostalCode = addr.PostalCode ?? string.Empty,
                Region = addr.Region,
                Country = addr.Country ?? string.Empty,
            };
        }

        return fields;
    }

    private static bool HasMeaningfulAddress(ScrapedAddress addr) =>
        !string.IsNullOrWhiteSpace(addr.Street)
        || !string.IsNullOrWhiteSpace(addr.City);

    // ── Logging ──────────────────────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "WebScraper provider: no structured data found at '{WebsiteUrl}'")]
    private partial void LogNotFound(string websiteUrl);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "WebScraper provider: page at '{WebsiteUrl}' yielded no mappable fields")]
    private partial void LogNoUsableFields(string websiteUrl);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "WebScraper provider: enriched '{WebsiteUrl}' with {FieldCount} fields")]
    private partial void LogSuccess(string websiteUrl, int fieldCount);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "WebScraper provider: HTTP error scraping '{WebsiteUrl}'")]
    private partial void LogHttpError(Exception ex, string websiteUrl);
}
