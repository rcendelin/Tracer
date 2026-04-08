using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;
using Tracer.Infrastructure.Providers.WebScraper;

namespace Tracer.Infrastructure.Providers.AiExtractor;

/// <summary>
/// Enrichment provider that extracts structured company data from unstructured text using
/// Azure OpenAI GPT-4o-mini with structured outputs (JSON schema enforcement).
/// <para>
/// Priority 250 — runs last in Tier 3, after all registry APIs (Tier 1) and web scraping
/// (Tier 2) have completed. Requires <see cref="TraceDepth.Deep"/> and a website URL.
/// </para>
/// <para>
/// Execution flow:
/// <list type="number">
///   <item>Fetches the company website via <see cref="IWebScraperClient"/>.</item>
///   <item>Builds an unstructured text block from the scraped content.</item>
///   <item>Calls <see cref="IAiExtractorClient.ExtractCompanyInfoAsync"/> to extract structured fields.</item>
///   <item>Maps <see cref="AiExtractedData"/> to <see cref="ProviderResult.Fields"/>.
///         Fields already covered by higher-priority providers are still returned — the
///         <c>GoldenRecordMerger</c> handles confidence-based conflict resolution and
///         provides a multi-source confidence boost when the AI confirms an existing value.</item>
/// </list>
/// </para>
/// </summary>
/// <remarks>
/// Description truncation at 16 KB preserves structured fields (name, phone, address) within
/// the 32 KB total prompt budget even when an adversarial website publishes a large text block.
/// </remarks>
internal sealed partial class AiExtractorProvider(
    IAiExtractorClient aiExtractor,
    IWebScraperClient scraper,
    ILogger<AiExtractorProvider> logger) : IEnrichmentProvider
{
    // 16 KB character limit on Description; ensures structured fields are not pushed out
    // of the 32 KB total prompt budget AiExtractorClient enforces via TruncateUtf8.
    private const int DescriptionMaxChars = 16 * 1024;

    public string ProviderId => "ai-extractor";
    public int Priority => 250;
    public double SourceQuality => 0.40;

    /// <inheritdoc />
    /// <remarks>
    /// Only runs on <see cref="TraceDepth.Deep"/> traces where a website URL is available.
    /// Quick and Standard traces skip AI extraction to stay within latency targets.
    /// </remarks>
    public bool CanHandle(TraceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Depth != TraceDepth.Deep)
            return false;

        return !string.IsNullOrWhiteSpace(context.Request.Website)
            || context.ExistingProfile?.Website?.Value is not null;
    }

    /// <inheritdoc />
    public async Task<ProviderResult> EnrichAsync(TraceContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var stopwatch = Stopwatch.StartNew();

        var websiteUrl = !string.IsNullOrWhiteSpace(context.Request.Website)
            ? context.Request.Website.Trim()
            : context.ExistingProfile?.Website?.Value;

        if (string.IsNullOrWhiteSpace(websiteUrl))
            return ProviderResult.NotFound(stopwatch.Elapsed);

        // Step 1: Scrape the website to get content for AI analysis
        WebScrapingResult? scraped;
        try
        {
            scraped = await scraper.ScrapeAsync(websiteUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProviderResult.Timeout(stopwatch.Elapsed);
        }
        catch (HttpRequestException ex)
        {
            LogScrapeError(ex, websiteUrl);
            return ProviderResult.Error("AI extractor: website fetch failed", stopwatch.Elapsed);
        }

        if (scraped is null)
        {
            LogScrapeNotFound(websiteUrl);
            return ProviderResult.NotFound(stopwatch.Elapsed);
        }

        // Step 2: Build text content from scraped data for AI prompt
        var textContent = BuildTextContent(scraped, context);
        if (string.IsNullOrWhiteSpace(textContent))
        {
            LogNoTextContent(websiteUrl);
            return ProviderResult.NotFound(stopwatch.Elapsed);
        }

        // Step 3: Run AI extraction
        AiExtractedData? extracted;
        try
        {
            extracted = await aiExtractor
                .ExtractCompanyInfoAsync(textContent, context, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProviderResult.Timeout(stopwatch.Elapsed);
        }
        catch (HttpRequestException ex)
        {
            // Sanitized: no raw ex.Message — Azure endpoint errors may contain internal URLs
            LogAiError(ex);
            return ProviderResult.Error("AI extractor: extraction failed", stopwatch.Elapsed);
        }

        if (extracted is null)
        {
            LogNoExtraction(websiteUrl);
            return ProviderResult.NotFound(stopwatch.Elapsed);
        }

        // Step 4: Map to provider fields
        var fields = MapToFields(extracted);
        if (fields.Count == 0)
        {
            LogNoFields(websiteUrl);
            return ProviderResult.NotFound(stopwatch.Elapsed);
        }

        LogSuccess(websiteUrl, fields.Count);
        return ProviderResult.Success(fields, stopwatch.Elapsed);
    }

    // ── Field mapping ────────────────────────────────────────────────────────

    private static Dictionary<FieldName, object?> MapToFields(AiExtractedData extracted)
    {
        var fields = new Dictionary<FieldName, object?>();

        if (!string.IsNullOrWhiteSpace(extracted.LegalName))
            fields[FieldName.LegalName] = extracted.LegalName;

        if (!string.IsNullOrWhiteSpace(extracted.Phone))
            fields[FieldName.Phone] = extracted.Phone;

        if (!string.IsNullOrWhiteSpace(extracted.Email))
            fields[FieldName.Email] = extracted.Email;

        if (!string.IsNullOrWhiteSpace(extracted.Industry))
            fields[FieldName.Industry] = extracted.Industry;

        // EmployeeRange is a string field (e.g. "51-200"); the AI extractor is the primary
        // source for this field — registry APIs and scrapers rarely provide it.
        if (!string.IsNullOrWhiteSpace(extracted.EmployeeRange))
            fields[FieldName.EmployeeRange] = extracted.EmployeeRange;

        if (extracted.Address is { } addr && HasMeaningfulAddress(addr))
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

    private static bool HasMeaningfulAddress(AiExtractedAddress addr) =>
        !string.IsNullOrWhiteSpace(addr.Street) || !string.IsNullOrWhiteSpace(addr.City);

    // ── Text building ─────────────────────────────────────────────────────────

    /// <summary>
    /// Assembles an unstructured text block from the scraped website content and request hints.
    /// The AI extractor uses this text to infer fields that structured parsing cannot (e.g.
    /// EmployeeRange from a description, normalized industry classification).
    /// </summary>
    private static string BuildTextContent(WebScrapingResult scraped, TraceContext context)
    {
        var sb = new StringBuilder(512);

        // Request hints give the model anchoring context
        if (!string.IsNullOrWhiteSpace(context.Request.CompanyName))
            sb.Append("Company name: ").AppendLine(context.Request.CompanyName);
        if (!string.IsNullOrWhiteSpace(context.Country))
            sb.Append("Country: ").AppendLine(context.Country);

        // Scraped structured data
        if (!string.IsNullOrWhiteSpace(scraped.CompanyName))
            sb.Append("Name on website: ").AppendLine(scraped.CompanyName);
        if (!string.IsNullOrWhiteSpace(scraped.Phone))
            sb.Append("Phone: ").AppendLine(scraped.Phone);
        if (!string.IsNullOrWhiteSpace(scraped.Email))
            sb.Append("Email: ").AppendLine(scraped.Email);
        if (!string.IsNullOrWhiteSpace(scraped.Industry))
            sb.Append("Industry (raw): ").AppendLine(scraped.Industry);

        if (scraped.Address is { } addr)
        {
            var parts = new[] { addr.Street, addr.City, addr.PostalCode, addr.Region, addr.Country }
                .Where(x => !string.IsNullOrWhiteSpace(x));
            var joined = string.Join(", ", parts);
            if (!string.IsNullOrWhiteSpace(joined))
                sb.Append("Address: ").AppendLine(joined);
        }

        // Description is the richest source for AI inference (EmployeeRange, normalized Industry).
        // Truncated to 16 KB to ensure structured fields above always survive the 32 KB prompt budget
        // even when an adversarial website publishes a massive description block.
        if (!string.IsNullOrWhiteSpace(scraped.Description))
        {
            sb.AppendLine("---");
            var desc = scraped.Description.Length > DescriptionMaxChars
                ? scraped.Description[..DescriptionMaxChars]
                : scraped.Description;
            sb.AppendLine(desc);
        }

        return sb.ToString().Trim();
    }

    // ── Logging ───────────────────────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "AI extractor provider: website '{WebsiteUrl}' not reachable or returned no structured data")]
    private partial void LogScrapeNotFound(string websiteUrl);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "AI extractor provider: HTTP error fetching '{WebsiteUrl}'")]
    private partial void LogScrapeError(Exception ex, string websiteUrl);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "AI extractor provider: scraped '{WebsiteUrl}' but produced no text content for AI")]
    private partial void LogNoTextContent(string websiteUrl);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "AI extractor provider: AI returned no extractable data from '{WebsiteUrl}'")]
    private partial void LogNoExtraction(string websiteUrl);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "AI extractor provider: AI extracted data from '{WebsiteUrl}' but no fields mapped")]
    private partial void LogNoFields(string websiteUrl);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "AI extractor provider: enriched '{WebsiteUrl}' with {FieldCount} fields")]
    private partial void LogSuccess(string websiteUrl, int fieldCount);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "AI extractor provider: HTTP error calling Azure OpenAI extraction endpoint")]
    private partial void LogAiError(Exception ex);
}
