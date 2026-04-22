using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Tracer.Infrastructure.Providers.LatamRegistry;

/// <summary>
/// HTTP client for LATAM registry lookups (Argentina AFIP, Chile SII, Colombia RUES,
/// Mexico SAT). Dispatches to per-country <see cref="ILatamRegistryAdapter"/>
/// implementations.
/// <para>
/// Rate-limiting: 10 requests/minute shared across all countries. LATAM registries
/// are hosted behind shared ASN / WAFs that block aggressively — keep this low.
/// </para>
/// <para>
/// Safety: SSRF guard (DNS + private IP check), HTML size cap (2 MB), no auto-redirect
/// (enforced by <see cref="SocketsHttpHandler"/> config in DI registration).
/// </para>
/// </summary>
internal sealed partial class LatamRegistryClient : ILatamRegistryClient, IDisposable
{
    private const int MaxHtmlChars = 2 * 1024 * 1024;
    private const int MaxRequestsPerMinute = 10;

    private readonly HttpClient _http;
    private readonly IReadOnlyList<ILatamRegistryAdapter> _adapters;
    private readonly ILogger<LatamRegistryClient> _logger;

    private readonly ConcurrentQueue<DateTimeOffset> _requestTimestamps = new();
    private readonly SemaphoreSlim _rateLimitGate = new(1, 1);

    internal Func<DateTimeOffset> Clock { get; init; } = static () => DateTimeOffset.UtcNow;
    internal Func<string, CancellationToken, Task<IPAddress[]>> DnsResolve { get; init; } =
        static (host, ct) => Dns.GetHostAddressesAsync(host, ct);

    public LatamRegistryClient(
        HttpClient http,
        IEnumerable<ILatamRegistryAdapter> adapters,
        ILogger<LatamRegistryClient> logger)
    {
        _http = http;
        _adapters = adapters.ToList();
        _logger = logger;
    }

    public void Dispose() => _rateLimitGate.Dispose();

    /// <inheritdoc />
    public async Task<LatamRegistrySearchResult?> LookupAsync(
        string countryCode, string identifier, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(countryCode, nameof(countryCode));
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier, nameof(identifier));

        var adapter = _adapters.FirstOrDefault(
            a => string.Equals(a.CountryCode, countryCode, StringComparison.OrdinalIgnoreCase));

        if (adapter is null)
        {
            LogNoAdapterFound(countryCode);
            return null;
        }

        var normalized = adapter.NormalizeIdentifier(identifier);
        if (normalized is null)
        {
            LogInvalidIdentifier(adapter.CountryCode);
            return null;
        }

        using var request = adapter.BuildLookupRequest(normalized);

        if (request.RequestUri is null)
        {
            LogMissingRequestUri(adapter.CountryCode);
            return null;
        }

        // SSRF guard — DNS resolve and block private / reserved IPs. Prevents an
        // attacker-controlled adapter or URL from reaching internal infrastructure.
        if (await IsBlockedUrlAsync(request.RequestUri, ct).ConfigureAwait(false))
        {
            LogBlockedUrl(request.RequestUri.ToString());
            return null;
        }

        await EnforceRateLimitAsync(ct).ConfigureAwait(false);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            LogTimeout(adapter.CountryCode, request.RequestUri.ToString());
            return null;
        }
        catch (HttpRequestException ex)
        {
            LogFetchFailed(adapter.CountryCode, ex.GetType().Name);
            return null;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                LogNonSuccess(adapter.CountryCode, (int)response.StatusCode);
                return null;
            }

            var body = await ReadBodyAsync(response.Content, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body))
                return null;

            return adapter.Parse(body, normalized);
        }
    }

    // ── Body reading ─────────────────────────────────────────────────────────

    private static async Task<string> ReadBodyAsync(HttpContent content, CancellationToken ct)
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

    // ── Rate limiting ────────────────────────────────────────────────────────

    private async Task EnforceRateLimitAsync(CancellationToken ct)
    {
        await _rateLimitGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = Clock();
            var windowStart = now.AddMinutes(-1);

            while (_requestTimestamps.TryPeek(out var oldest) && oldest < windowStart)
                _requestTimestamps.TryDequeue(out _);

            if (_requestTimestamps.Count >= MaxRequestsPerMinute)
            {
                if (_requestTimestamps.TryPeek(out var nextExpiry))
                {
                    var waitTime = nextExpiry.AddMinutes(1) - now;
                    if (waitTime > TimeSpan.Zero)
                    {
                        LogRateLimitHit(waitTime);
                        await Task.Delay(waitTime, ct).ConfigureAwait(false);
                    }
                }

                while (_requestTimestamps.TryPeek(out var old) && old < Clock().AddMinutes(-1))
                    _requestTimestamps.TryDequeue(out _);
            }

            _requestTimestamps.Enqueue(Clock());
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
        return b[0] == 10
               || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
               || (b[0] == 192 && b[1] == 168)
               || (b[0] == 169 && b[1] == 254)
               || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);
    }

    // ── Logging ──────────────────────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "LatamRegistry: No adapter found for country '{CountryCode}'")]
    private partial void LogNoAdapterFound(string countryCode);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "LatamRegistry [{CountryCode}]: Identifier failed country-specific validation")]
    private partial void LogInvalidIdentifier(string countryCode);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "LatamRegistry [{CountryCode}]: Adapter produced a request without a URI")]
    private partial void LogMissingRequestUri(string countryCode);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "LatamRegistry: Blocked SSRF attempt — '{Url}' resolves to a private IP")]
    private partial void LogBlockedUrl(string url);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "LatamRegistry [{CountryCode}]: Timeout fetching '{Url}'")]
    private partial void LogTimeout(string countryCode, string url);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "LatamRegistry [{CountryCode}]: Fetch failed ({ExceptionType})")]
    private partial void LogFetchFailed(string countryCode, string exceptionType);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "LatamRegistry [{CountryCode}]: Returned status {StatusCode}")]
    private partial void LogNonSuccess(string countryCode, int statusCode);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "LatamRegistry: Rate limit hit, waiting {WaitTime}")]
    private partial void LogRateLimitHit(TimeSpan waitTime);
}
