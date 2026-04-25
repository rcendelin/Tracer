using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;

namespace Tracer.Infrastructure.Providers.WebScraper;

/// <summary>
/// Fetches company websites and extracts structured data using AngleSharp HTML parsing.
/// <para>
/// Extraction priority (highest to lowest):
/// <list type="number">
///   <item>JSON-LD (schema.org Organization / LocalBusiness)</item>
///   <item>Open Graph meta tags</item>
///   <item>HTML semantic patterns (mailto links, tel links, meta description)</item>
/// </list>
/// Higher-priority sources win for each field; lower-priority sources fill in missing fields.
/// </para>
/// <para>
/// Safety constraints:
/// <list type="bullet">
///   <item>Only HTTP/HTTPS URLs accepted.</item>
///   <item>Only <c>text/html</c> content type processed.</item>
///   <item>Content capped at <see cref="MaxHtmlChars"/> to prevent memory exhaustion.</item>
///   <item>No JavaScript execution — static HTML parsing only.</item>
/// </list>
/// </para>
/// </summary>
internal sealed partial class WebScraperClient(HttpClient http, ILogger<WebScraperClient> logger)
    : IWebScraperClient
{
    // 2 MB cap — prevents memory exhaustion on unusually large pages.
    // Truncated HTML still contains <head> and the visible body, which is sufficient for extraction.
    private const int MaxHtmlChars = 2 * 1024 * 1024;

    // Allows injecting a stub DNS resolver in unit tests so tests do not perform real network calls.
    // Production code always uses the default (Dns.GetHostAddressesAsync).
    internal Func<string, CancellationToken, Task<IPAddress[]>> DnsResolve { get; init; } =
        static (host, ct) => Dns.GetHostAddressesAsync(host, ct);

    // schema.org types recognised as "company" entities
    private static readonly HashSet<string> OrganizationTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Organization", "LocalBusiness", "Corporation", "Company",
            "Store", "Restaurant", "Hotel", "MedicalBusiness", "FinancialService",
            "ProfessionalService", "AutoDealer", "InsuranceAgency",
        };

    /// <inheritdoc />
    public async Task<WebScrapingResult?> ScrapeAsync(string url, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url, nameof(url));

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            LogInvalidUrl(url);
            return null;
        }

        // SSRF guard — reject URLs that resolve to private/reserved IP ranges.
        // This prevents attackers from using the scraper to probe internal infrastructure.
        if (await IsBlockedUrlAsync(uri, DnsResolve, ct).ConfigureAwait(false))
        {
            LogBlockedUrl(url);
            return null;
        }

        HttpResponseMessage response;
        try
        {
            response = await http
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Polly AttemptTimeout — not caller cancellation
            LogTimeout(url);
            return null;
        }
        catch (HttpRequestException ex)
        {
            LogFetchFailed(url, ex);
            return null;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                LogNonSuccess(url, (int)response.StatusCode);
                return null;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (!contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
            {
                LogNotHtml(url, contentType);
                return null;
            }

            var html = await ReadHtmlAsync(response.Content, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(html))
                return null;

            return await ParseAsync(html, url, ct).ConfigureAwait(false);
        }
    }

    // ── HTML reading ────────────────────────────────────────────────────────

    private static async Task<string> ReadHtmlAsync(HttpContent content, CancellationToken ct)
    {
        // Read up to MaxHtmlChars; truncation is intentional — <head> and top-of-body
        // contain the structured data we care about.
        var encoding = TryGetEncoding(content.Headers.ContentType?.CharSet);
        using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, encoding ?? Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true, bufferSize: 8192, leaveOpen: false);

        var sb = new StringBuilder(Math.Min(64 * 1024, MaxHtmlChars));
        var buffer = new char[4096];
        int read;

        while (sb.Length < MaxHtmlChars)
        {
            var remaining = MaxHtmlChars - sb.Length;
            var charsToRead = Math.Min(buffer.Length, remaining);
            read = await reader.ReadAsync(buffer.AsMemory(0, charsToRead), ct)
                .ConfigureAwait(false);
            if (read == 0) break;
            sb.Append(buffer, 0, read);
        }

        return sb.ToString();
    }

    private static Encoding? TryGetEncoding(string? charSet)
    {
        if (string.IsNullOrWhiteSpace(charSet)) return null;
        try { return Encoding.GetEncoding(charSet); }
        catch (ArgumentException) { return null; }
    }

    // ── AngleSharp parsing ──────────────────────────────────────────────────

    private static async Task<WebScrapingResult?> ParseAsync(string html, string sourceUrl, CancellationToken ct)
    {
        var config = Configuration.Default;
        using var context = BrowsingContext.New(config);

        // AngleSharp OpenAsync does not accept a CancellationToken directly on the content overload;
        // the document is parsed in-process from a string (no I/O), so this is synchronous in practice.
        var document = await context
            .OpenAsync(req => req.Content(html), ct)
            .ConfigureAwait(false);

        // Extract using priority chain — each method returns null for fields it cannot find.
        var jsonLd = ExtractFromJsonLd(document, sourceUrl);
        var og = ExtractFromOpenGraph(document, sourceUrl);
        var html2 = ExtractFromHtmlPatterns(document, sourceUrl);

        return MergeResults(sourceUrl, jsonLd, og, html2);
    }

    // ── JSON-LD extraction ──────────────────────────────────────────────────

    private static WebScrapingResult? ExtractFromJsonLd(IDocument document, string sourceUrl)
    {
        var scripts = document.QuerySelectorAll("script[type='application/ld+json']");

        foreach (var script in scripts)
        {
            var json = script.TextContent.Trim();
            if (string.IsNullOrWhiteSpace(json)) continue;

            // Cap JSON-LD block size to prevent DoS via deeply nested or enormous payloads.
            if (json.Length > 64 * 1024) continue;

            try
            {
                using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
                var root = doc.RootElement;

                // JSON-LD may be a single object or an array — iterate without materialising
                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in root.EnumerateArray())
                    {
                        var result = TryParseOrganizationElement(element, sourceUrl);
                        if (result is not null)
                            return result;
                    }
                }
                else
                {
                    var result = TryParseOrganizationElement(root, sourceUrl);
                    if (result is not null)
                        return result;
                }
            }
            catch (JsonException)
            {
                // Malformed JSON-LD — skip this script block
            }
        }

        return null;
    }

    private static WebScrapingResult? TryParseOrganizationElement(JsonElement element, string sourceUrl)
    {
        if (!element.TryGetProperty("@type", out var typeProp))
            return null;

        // @type may be a string ("Organization") or an array (["Organization","LocalBusiness"])
        var type = typeProp.ValueKind switch
        {
            JsonValueKind.String => typeProp.GetString(),
            JsonValueKind.Array => typeProp.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString())
                .FirstOrDefault(t => t is not null && OrganizationTypes.Contains(t!)),
            _ => null,
        };

        if (type is null || !OrganizationTypes.Contains(type))
            return null;

        var name = GetStringProp(element, "name");
        var phone = NormalizePhone(GetStringProp(element, "telephone"));
        var email = NormalizeEmail(GetStringProp(element, "email"));
        var url = GetStringProp(element, "url");
        var description = TruncateDescription(GetStringProp(element, "description"));
        ScrapedAddress? address = null;

        if (element.TryGetProperty("address", out var addrProp))
            address = ParsePostalAddress(addrProp);

        // Derive industry hint from schema.org sub-type (anything more specific than "Organization")
        var industry = !string.Equals(type, "Organization", StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(type, "Corporation", StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(type, "Company", StringComparison.OrdinalIgnoreCase)
            ? type
            : null;

        // Require at least a name to consider the JSON-LD valid
        if (string.IsNullOrWhiteSpace(name))
            return null;

        return new WebScrapingResult
        {
            SourceUrl = sourceUrl,
            CompanyName = name,
            Phone = phone,
            Email = email,
            Website = url,
            Description = description,
            Industry = industry,
            Address = address,
        };
    }

    private static ScrapedAddress? ParsePostalAddress(JsonElement addrElement)
    {
        // address may be a string (unstructured) or a PostalAddress object
        if (addrElement.ValueKind == JsonValueKind.String)
            return new ScrapedAddress { Street = addrElement.GetString() };

        if (addrElement.ValueKind != JsonValueKind.Object)
            return null;

        return new ScrapedAddress
        {
            Street = GetStringProp(addrElement, "streetAddress"),
            City = GetStringProp(addrElement, "addressLocality"),
            PostalCode = GetStringProp(addrElement, "postalCode"),
            Country = GetStringProp(addrElement, "addressCountry"),
            Region = GetStringProp(addrElement, "addressRegion"),
        };
    }

    // ── Open Graph extraction ───────────────────────────────────────────────

    private static WebScrapingResult? ExtractFromOpenGraph(IDocument document, string sourceUrl)
    {
        var name = GetMeta(document, "og:site_name")
                ?? GetMeta(document, "og:title");
        var description = TruncateDescription(GetMeta(document, "og:description"));

        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(description))
            return null;

        return new WebScrapingResult
        {
            SourceUrl = sourceUrl,
            CompanyName = name,
            Description = description,
        };
    }

    // ── HTML pattern extraction ─────────────────────────────────────────────

    private static WebScrapingResult? ExtractFromHtmlPatterns(IDocument document, string sourceUrl)
    {
        // mailto: links — most reliable HTML source for email
        var email = NormalizeEmail(FindMailtoLink(document));

        // tel: links — most reliable HTML source for phone
        var phone = NormalizePhone(FindTelLink(document));

        // Page title as last-resort company name
        var title = CleanTitle(document.Title);

        // meta description
        var description = TruncateDescription(GetMeta(document, "description"));

        if (email is null && phone is null && title is null)
            return null;

        return new WebScrapingResult
        {
            SourceUrl = sourceUrl,
            CompanyName = title,
            Phone = phone,
            Email = email,
            Description = description,
        };
    }

    private static string? FindMailtoLink(IDocument document)
    {
        var link = document.QuerySelector("a[href^='mailto:']");
        var href = link?.GetAttribute("href");
        if (string.IsNullOrWhiteSpace(href)) return null;

        // Strip "mailto:" prefix and any query string (mailto:?subject=...)
        var addr = href["mailto:".Length..];
        var q = addr.IndexOf('?', StringComparison.Ordinal);
        return q >= 0 ? addr[..q] : addr;
    }

    private static string? FindTelLink(IDocument document)
    {
        var link = document.QuerySelector("a[href^='tel:']");
        var href = link?.GetAttribute("href");
        if (string.IsNullOrWhiteSpace(href)) return null;

        // Strip "tel:" prefix
        return href["tel:".Length..].Trim();
    }

    private static string? CleanTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        // Many pages have "Company Name | Tagline" or "Company Name - Description"
        // Keep only the first segment before "|" or " - " or " – "
        var separators = new[] { " | ", " - ", " – ", " — ", " · " };
        foreach (var sep in separators)
        {
            var idx = title.IndexOf(sep, StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
            {
                title = title[..idx].Trim();
                break;
            }
        }

        return string.IsNullOrWhiteSpace(title) ? null : title;
    }

    // ── Merge ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Merges results from multiple extraction strategies.
    /// JSON-LD wins for each field; gaps are filled from Open Graph then HTML patterns.
    /// Returns null if no useful data was found.
    /// </summary>
    private static WebScrapingResult? MergeResults(
        string sourceUrl,
        WebScrapingResult? jsonLd,
        WebScrapingResult? og,
        WebScrapingResult? html)
    {
        // If all sources returned null, there is nothing useful
        if (jsonLd is null && og is null && html is null)
            return null;

        return new WebScrapingResult
        {
            SourceUrl = sourceUrl,
            CompanyName = jsonLd?.CompanyName ?? og?.CompanyName ?? html?.CompanyName,
            Phone = jsonLd?.Phone ?? og?.Phone ?? html?.Phone,
            Email = jsonLd?.Email ?? og?.Email ?? html?.Email,
            Website = jsonLd?.Website,
            Description = jsonLd?.Description ?? og?.Description ?? html?.Description,
            Industry = jsonLd?.Industry,
            Address = jsonLd?.Address,
        };
    }

    // ── Normalization helpers ───────────────────────────────────────────────

    private static string? NormalizePhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        // Collapse runs of whitespace to a single space; non-numeric characters (dashes, parens)
        // are intentionally kept — they are valid in E.164 and human-readable formats.
        var cleaned = PhoneCleanRegex().Replace(raw.Trim(), " ").Trim();
        return cleaned.Length < 7 ? null : cleaned;
    }

    private static string? NormalizeEmail(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        // Email addresses are case-insensitive; lowercase is the canonical form.
        // CA1308 suppressed: ToUpperInvariant is inappropriate for email addresses.
        #pragma warning disable CA1308
        var email = raw.Trim().ToLowerInvariant();
        #pragma warning restore CA1308
        return EmailRegex().IsMatch(email) ? email : null;
    }

    private static string? TruncateDescription(string? desc)
    {
        if (string.IsNullOrWhiteSpace(desc)) return null;
        const int MaxDescLen = 500;
        return desc.Length > MaxDescLen ? string.Concat(desc.AsSpan(0, MaxDescLen), "…") : desc;
    }

    // ── DOM helpers ─────────────────────────────────────────────────────────

    private static string? GetMeta(IDocument document, string nameOrProperty)
    {
        // Try property attribute first (Open Graph), then name attribute (standard meta)
        var el = document.QuerySelector($"meta[property='{nameOrProperty}']")
                 ?? document.QuerySelector($"meta[name='{nameOrProperty}']");
        return el?.GetAttribute("content")?.Trim();
    }

    private static string? GetStringProp(JsonElement element, string propName)
    {
        return element.TryGetProperty(propName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()?.Trim()
            : null;
    }

    // ── SSRF protection ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> if the URL resolves to a private, loopback, or reserved
    /// IP address that must not be reached from a server-side HTTP client.
    /// Covers: loopback, RFC 1918, link-local (IMDS 169.254.x.x), CGNAT (100.64.x.x),
    /// IPv6 link-local / site-local / multicast.
    /// </summary>
    /// <param name="resolve">
    /// DNS resolver delegate. Defaults to <see cref="Dns.GetHostAddressesAsync(string, CancellationToken)"/>;
    /// injectable for unit testing to avoid real DNS lookups.
    /// </param>
    private static async Task<bool> IsBlockedUrlAsync(
        Uri uri,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        CancellationToken ct)
    {
        IPAddress[] addresses;
        try
        {
            if (IPAddress.TryParse(uri.Host, out var directIp))
                addresses = [directIp];
            else
                addresses = await resolve(uri.Host, ct).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            // Unresolvable host — treat as blocked (no valid public target).
            // An unresolvable hostname cannot reach any resource, internal or external,
            // but we block preemptively to be conservative.
            return true;
        }

        return addresses.Any(IsPrivateOrReservedIp);
    }

    private static bool IsPrivateOrReservedIp(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return true;

        // Only IPv4 private ranges beyond here
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;

        var b = ip.GetAddressBytes();
        return b[0] == 10                                          // 10.0.0.0/8       RFC 1918
               || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)      // 172.16.0.0/12    RFC 1918
               || (b[0] == 192 && b[1] == 168)                    // 192.168.0.0/16   RFC 1918
               || (b[0] == 169 && b[1] == 254)                    // 169.254.0.0/16   Link-local / IMDS
               || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);    // 100.64.0.0/10    CGNAT RFC 6598
    }

    // ── Compiled regexes ────────────────────────────────────────────────────

    // Keeps only characters valid in phone numbers; collapses runs of whitespace
    [GeneratedRegex(@"\s+")]
    private static partial Regex PhoneCleanRegex();

    [GeneratedRegex(@"^[a-z0-9._%+\-]+@[a-z0-9.\-]+\.[a-z]{2,}$")]
    private static partial Regex EmailRegex();

    // ── Logging ─────────────────────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Debug, Message = "WebScraper: Invalid or non-HTTP URL '{Url}'")]
    private partial void LogInvalidUrl(string url);

    [LoggerMessage(Level = LogLevel.Warning, Message = "WebScraper: Blocked SSRF attempt — '{Url}' resolves to a private/reserved IP")]
    private partial void LogBlockedUrl(string url);

    [LoggerMessage(Level = LogLevel.Debug, Message = "WebScraper: Timeout fetching '{Url}'")]
    private partial void LogTimeout(string url);

    [LoggerMessage(Level = LogLevel.Debug, Message = "WebScraper: Failed to fetch '{Url}'")]
    private partial void LogFetchFailed(string url, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "WebScraper: '{Url}' returned status {StatusCode}")]
    private partial void LogNonSuccess(string url, int statusCode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "WebScraper: '{Url}' is not HTML (content-type: {ContentType})")]
    private partial void LogNotHtml(string url, string contentType);
}
