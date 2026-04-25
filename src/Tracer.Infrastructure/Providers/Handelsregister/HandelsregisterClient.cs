using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;

namespace Tracer.Infrastructure.Providers.Handelsregister;

/// <summary>
/// Scrapes the German commercial register portal (handelsregister.de) for company data.
/// <para>
/// The portal has no official API; this client submits POST form requests and parses
/// the returned HTML using AngleSharp. Search results are in tabular form; detail pages
/// contain structured registration data.
/// </para>
/// <para>
/// Rate-limiting: the German Data Usage Act (§9) mandates a maximum of 60 requests per hour.
/// This client enforces the limit via a sliding-window counter.
/// </para>
/// <para>
/// Safety constraints:
/// <list type="bullet">
///   <item>Only HTTP/HTTPS URLs processed.</item>
///   <item>Response body capped at <see cref="MaxHtmlChars"/> to prevent memory exhaustion.</item>
///   <item>SSRF protection — private/reserved IP ranges are blocked.</item>
///   <item>No auto-redirect — prevents SSRF bypass via 302 to internal hosts.</item>
/// </list>
/// </para>
/// </summary>
internal sealed partial class HandelsregisterClient : IHandelsregisterClient, IDisposable
{
    /// <summary>2 MB cap — prevents memory exhaustion on unusually large pages.</summary>
    private const int MaxHtmlChars = 2 * 1024 * 1024;

    /// <summary>
    /// Maximum requests per hour mandated by German law (Data Usage Act §9).
    /// Note: This limit is enforced per application instance. If running multiple replicas,
    /// consider distributed rate limiting (e.g. Redis) to maintain compliance.
    /// </summary>
    private const int MaxRequestsPerHour = 60;

    /// <summary>Search endpoint (normal search mode — searches all federal states).</summary>
    private const string SearchPath = "ergebnisse.xhtml";

    private static readonly string[] OfficerLabels =
        ["Geschäftsführer", "Vorstand", "Vertretungsberechtigte", "Persönlich haftende Gesellschafter", "Inhaber"];

    private readonly HttpClient _http;
    private readonly ILogger<HandelsregisterClient> _logger;

    // Sliding-window rate limiter: tracks timestamps of recent requests
    private readonly ConcurrentQueue<DateTimeOffset> _requestTimestamps = new();
    private readonly SemaphoreSlim _rateLimitGate = new(1, 1);

    public HandelsregisterClient(HttpClient http, ILogger<HandelsregisterClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    // Allow injecting a clock for testability
    internal Func<DateTimeOffset> Clock { get; init; } = static () => DateTimeOffset.UtcNow;

    // Allow injecting a stub DNS resolver for unit tests
    internal Func<string, CancellationToken, Task<IPAddress[]>> DnsResolve { get; init; } =
        static (host, ct) => Dns.GetHostAddressesAsync(host, ct);

    public void Dispose()
    {
        _rateLimitGate.Dispose();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<HandelsregisterSearchResult>?> SearchByNameAsync(
        string companyName, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companyName, nameof(companyName));

        await EnforceRateLimitAsync(ct).ConfigureAwait(false);

        var formData = new Dictionary<string, string>
        {
            ["schlagwoerter"] = companyName,
            ["schlagwortOptionen"] = "2", // contain at least one keyword
        };

        var html = await PostFormAsync(SearchPath, formData, ct).ConfigureAwait(false);
        if (html is null)
            return null;

        return ParseSearchResults(html);
    }

    /// <inheritdoc />
    public async Task<HandelsregisterCompanyDetail?> GetByRegisterNumberAsync(
        string registerType, string registerNumber, string? court, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerType, nameof(registerType));
        ArgumentException.ThrowIfNullOrWhiteSpace(registerNumber, nameof(registerNumber));

        await EnforceRateLimitAsync(ct).ConfigureAwait(false);

        var formData = new Dictionary<string, string>
        {
            ["Registerart"] = registerType.ToUpperInvariant(),
            ["Registernummer"] = registerNumber,
        };

        if (!string.IsNullOrWhiteSpace(court))
            formData["Registergericht"] = court;

        var html = await PostFormAsync(SearchPath, formData, ct).ConfigureAwait(false);
        if (html is null)
            return null;

        // The register number search may return a result table or go directly to detail page.
        // Try parsing as detail first, then fall back to search results.
        var detail = ParseCompanyDetail(html, registerType, registerNumber);
        if (detail is not null)
            return detail;

        // If search returned a results table, pick the first exact match
        var searchResults = ParseSearchResults(html);
        if (searchResults is null || searchResults.Count == 0)
            return null;

        var match = searchResults.FirstOrDefault(r =>
            string.Equals(r.RegisterType, registerType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.RegisterNumber, registerNumber, StringComparison.OrdinalIgnoreCase));

        if (match is null)
            return null;

        return new HandelsregisterCompanyDetail
        {
            CompanyName = match.CompanyName,
            RegistrationId = $"{match.RegisterType} {match.RegisterNumber}",
            RegisterCourt = match.RegisterCourt,
            Status = match.Status,
        };
    }

    // ── HTTP ─────────────────────────────────────────────────────────────────

    private async Task<string?> PostFormAsync(
        string path, Dictionary<string, string> formData, CancellationToken ct)
    {
        var baseUri = _http.BaseAddress
            ?? new Uri("https://www.handelsregister.de/rp_web/");

        var requestUri = new Uri(baseUri, path);

        // SSRF guard — reject URLs that resolve to private/reserved IP ranges
        if (await IsBlockedUrlAsync(requestUri, ct).ConfigureAwait(false))
        {
            var blockedUrl = requestUri.ToString();
            LogBlockedUrl(blockedUrl);
            return null;
        }

        using var content = new FormUrlEncodedContent(formData);

        HttpResponseMessage response;
        try
        {
            response = await _http
                .PostAsync(requestUri, content, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            var timeoutUrl = requestUri.ToString();
            LogTimeout(timeoutUrl);
            return null;
        }
        catch (HttpRequestException ex)
        {
            var failedUrl = requestUri.ToString();
            var exceptionType = ex.GetType().Name;
            LogFetchFailed(failedUrl, exceptionType);
            return null;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var nonSuccessUrl = requestUri.ToString();
                LogNonSuccess(nonSuccessUrl, (int)response.StatusCode);
                return null;
            }

            return await ReadHtmlAsync(response.Content, ct).ConfigureAwait(false);
        }
    }

    // ── HTML reading ─────────────────────────────────────────────────────────

    private static async Task<string> ReadHtmlAsync(HttpContent content, CancellationToken ct)
    {
        var encoding = TryGetEncoding(content.Headers.ContentType?.CharSet);
        var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var _ = stream.ConfigureAwait(false);
        using var reader = new StreamReader(stream, encoding ?? Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true, bufferSize: 8192, leaveOpen: false);

        var sb = new StringBuilder(Math.Min(64 * 1024, MaxHtmlChars));
        var buffer = new char[4096];

        while (sb.Length < MaxHtmlChars)
        {
            var remaining = MaxHtmlChars - sb.Length;
            var charsToRead = Math.Min(buffer.Length, remaining);
            var read = await reader.ReadAsync(buffer.AsMemory(0, charsToRead), ct)
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

    // ── HTML parsing (search results) ────────────────────────────────────────

    // AngleSharp OpenAsync with Content() performs in-process string parsing — no I/O.
    // The Task completes synchronously; .GetAwaiter().GetResult() is safe here.
    private static List<HandelsregisterSearchResult>? ParseSearchResults(string html)
    {
        var config = Configuration.Default;
        var context = BrowsingContext.New(config);
#pragma warning disable CA1849 // AngleSharp Content() parsing is synchronous in practice — no I/O
        var document = context.OpenAsync(req => req.Content(html)).GetAwaiter().GetResult();
#pragma warning restore CA1849

        var rows = document.QuerySelectorAll("table.RegPortEr662_ergebnisTable tr, table[summary] tr");
        if (rows.Length == 0)
            return null;

        var results = new List<HandelsregisterSearchResult>();

        foreach (var row in rows)
        {
            var cells = row.QuerySelectorAll("td");
            if (cells.Length < 3)
                continue;

            var companyName = CleanText(cells.Length > 1 ? cells[1].TextContent : null);
            var courtText = CleanText(cells.Length > 2 ? cells[2].TextContent : null);
            var registerText = CleanText(cells.Length > 3 ? cells[3].TextContent : null);
            var statusText = CleanText(cells.Length > 4 ? cells[4].TextContent : null);

            if (string.IsNullOrWhiteSpace(companyName) || string.IsNullOrWhiteSpace(registerText))
                continue;

            var (regType, regNumber) = ParseRegistrationId(registerText);
            if (regType is null || regNumber is null)
                continue;

            results.Add(new HandelsregisterSearchResult
            {
                CompanyName = companyName,
                RegisterType = regType,
                RegisterNumber = regNumber,
                RegisterCourt = courtText ?? string.Empty,
                Status = statusText,
            });
        }

        return results.Count > 0 ? results : null;
    }

    // ── HTML parsing (detail page) ───────────────────────────────────────────

    private static HandelsregisterCompanyDetail? ParseCompanyDetail(
        string html, string expectedType, string expectedNumber)
    {
        var config = Configuration.Default;
        var context = BrowsingContext.New(config);
#pragma warning disable CA1849 // AngleSharp Content() parsing is synchronous in practice — no I/O
        var document = context.OpenAsync(req => req.Content(html)).GetAwaiter().GetResult();
#pragma warning restore CA1849

        var companyName = ExtractLabeledValue(document, "Firma", "Name", "Bezeichnung")
            ?? ExtractFromHeading(document);

        if (string.IsNullOrWhiteSpace(companyName))
            return null;

        var registrationId = $"{expectedType} {expectedNumber}";
        var registerCourt = ExtractLabeledValue(document, "Registergericht", "Gericht") ?? string.Empty;
        var legalForm = ExtractLabeledValue(document, "Rechtsform");
        var status = ExtractLabeledValue(document, "Status", "Geschäftsstatus");
        var street = ExtractLabeledValue(document, "Straße", "Anschrift", "Sitz");
        var postalCode = ExtractPostalCode(document);
        var city = ExtractLabeledValue(document, "Ort", "Stadt");

        if (!string.IsNullOrWhiteSpace(street) && string.IsNullOrWhiteSpace(city))
        {
            var (parsedStreet, parsedPostalCode, parsedCity) = SplitGermanAddress(street);
            street = parsedStreet;
            postalCode ??= parsedPostalCode;
            city ??= parsedCity;
        }

        var officers = ExtractOfficers(document);

        return new HandelsregisterCompanyDetail
        {
            CompanyName = companyName,
            RegistrationId = registrationId,
            RegisterCourt = registerCourt,
            LegalForm = legalForm,
            Status = status,
            Street = street,
            PostalCode = postalCode,
            City = city,
            Officers = officers,
        };
    }

    // ── Extraction helpers ───────────────────────────────────────────────────

    private static string? ExtractLabeledValue(IDocument document, params string[] labels)
    {
        foreach (var label in labels)
        {
            var th = document.QuerySelectorAll("th, dt, label")
                .FirstOrDefault(el => el.TextContent.Trim()
                    .StartsWith(label, StringComparison.OrdinalIgnoreCase));

            var value = th?.NextElementSibling?.TextContent;
            var cleaned = CleanText(value);
            if (!string.IsNullOrWhiteSpace(cleaned))
                return cleaned;
        }

        return null;
    }

    private static string? ExtractFromHeading(IDocument document)
    {
        var heading = document.QuerySelector("h1, h2.firma, h2, h3");
        return CleanText(heading?.TextContent);
    }

    private static string? ExtractPostalCode(IDocument document) =>
        ExtractLabeledValue(document, "PLZ", "Postleitzahl");

    private static List<string> ExtractOfficers(IDocument document)
    {
        var officers = new List<string>();

        foreach (var label in OfficerLabels)
        {
            var header = document.QuerySelectorAll("th, dt, h3, h4, strong")
                .FirstOrDefault(el => el.TextContent.Trim()
                    .Contains(label, StringComparison.OrdinalIgnoreCase));

            if (header is null) continue;

            var sibling = header.NextElementSibling;
            if (sibling is null) continue;

            var items = sibling.QuerySelectorAll("li, td");
            if (items.Length > 0)
            {
                foreach (var item in items)
                {
                    var name = CleanText(item.TextContent);
                    if (!string.IsNullOrWhiteSpace(name) && name.Length > 2)
                        officers.Add(name);
                }
            }
            else
            {
                var text = CleanText(sibling.TextContent);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    foreach (var rawName in text.Split([';', '\n'], StringSplitOptions.RemoveEmptyEntries))
                    {
                        var trimmed = rawName.Trim();
                        if (trimmed.Length > 2)
                            officers.Add(trimmed);
                    }
                }
            }

            break; // Take first matching officer section
        }

        return officers;
    }

    private static (string? Street, string? PostalCode, string? City) SplitGermanAddress(string fullAddress)
    {
        var parts = fullAddress.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            var plzMatch = Regex.Match(fullAddress, @"(\d{5})\s+(.+)$");
            if (plzMatch.Success)
            {
                var streetPart = fullAddress[..plzMatch.Index].Trim().TrimEnd(',');
                return (streetPart, plzMatch.Groups[1].Value, plzMatch.Groups[2].Value.Trim());
            }

            return (fullAddress, null, null);
        }

        var street = parts[0].Trim();
        var cityPart = parts[^1].Trim();

        var cityPlzMatch = Regex.Match(cityPart, @"^(\d{5})\s+(.+)$");
        if (cityPlzMatch.Success)
            return (street, cityPlzMatch.Groups[1].Value, cityPlzMatch.Groups[2].Value.Trim());

        return (street, null, cityPart);
    }

    private static (string? Type, string? Number) ParseRegistrationId(string registerText)
    {
        var match = RegisterIdRegex().Match(registerText.Trim());
        return match.Success
            ? (match.Groups[1].Value.ToUpperInvariant(), match.Groups[2].Value)
            : (null, null);
    }

    private static string? CleanText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var cleaned = WhitespaceRegex().Replace(text.Trim(), " ");
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    // ── Rate limiting ────────────────────────────────────────────────────────

    private async Task EnforceRateLimitAsync(CancellationToken ct)
    {
        await _rateLimitGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = Clock();
            var windowStart = now.AddHours(-1);

            while (_requestTimestamps.TryPeek(out var oldest) && oldest < windowStart)
                _requestTimestamps.TryDequeue(out _);

            if (_requestTimestamps.Count >= MaxRequestsPerHour)
            {
                if (_requestTimestamps.TryPeek(out var nextExpiry))
                {
                    var waitTime = nextExpiry.AddHours(1) - now;
                    if (waitTime > TimeSpan.Zero)
                    {
                        LogRateLimitHit(waitTime);
                        await Task.Delay(waitTime, ct).ConfigureAwait(false);
                    }
                }

                while (_requestTimestamps.TryPeek(out var old) && old < Clock().AddHours(-1))
                    _requestTimestamps.TryDequeue(out _);
            }

            _requestTimestamps.Enqueue(now);
        }
        finally
        {
            _rateLimitGate.Release();
        }
    }

    // ── SSRF protection ──────────────────────────────────────────────────────

    private async Task<bool> IsBlockedUrlAsync(Uri uri, CancellationToken ct)
    {
        IPAddress[] addresses;
        try
        {
            if (IPAddress.TryParse(uri.Host, out var directIp))
                addresses = [directIp];
            else
                addresses = await DnsResolve(uri.Host, ct).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            return true;
        }

        return addresses.Any(IsPrivateOrReservedIp);
    }

    private static bool IsPrivateOrReservedIp(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return true;

        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;

        var b = ip.GetAddressBytes();
        return b[0] == 10                                          // 10.0.0.0/8       RFC 1918
               || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)      // 172.16.0.0/12    RFC 1918
               || (b[0] == 192 && b[1] == 168)                    // 192.168.0.0/16   RFC 1918
               || (b[0] == 169 && b[1] == 254)                    // 169.254.0.0/16   Link-local / IMDS
               || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);    // 100.64.0.0/10    CGNAT RFC 6598
    }

    // ── Compiled regexes ────────────────────────────────────────────────────

    [GeneratedRegex(@"^(HR[AB]|GnR|PR|VR)\s*(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex RegisterIdRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    // ── Logging ──────────────────────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Handelsregister: Blocked SSRF attempt — '{Url}' resolves to a private/reserved IP")]
    private partial void LogBlockedUrl(string url);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Handelsregister: Timeout fetching '{Url}'")]
    private partial void LogTimeout(string url);

    // Log exception type only — per project convention (CWE-209), no raw stack traces in logs.
    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Handelsregister: Failed to fetch '{Url}' ({ExceptionType})")]
    private partial void LogFetchFailed(string url, string exceptionType);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Handelsregister: '{Url}' returned status {StatusCode}")]
    private partial void LogNonSuccess(string url, int statusCode);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Handelsregister: Rate limit hit, waiting {WaitTime}")]
    private partial void LogRateLimitHit(TimeSpan waitTime);
}
