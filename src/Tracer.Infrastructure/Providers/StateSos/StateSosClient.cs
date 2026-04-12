using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Tracer.Infrastructure.Providers.StateSos;

/// <summary>
/// HTTP client for US Secretary of State business registries.
/// Dispatches search requests to per-state <see cref="IStateSosAdapter"/> implementations.
/// <para>
/// Rate-limiting: 20 requests per minute (conservative, shared across all states).
/// US SoS sites have anti-bot protections; exceeding limits risks IP blocking.
/// </para>
/// <para>
/// Safety: SSRF protection, HTML size cap, no auto-redirect (via SocketsHttpHandler config).
/// </para>
/// </summary>
internal sealed partial class StateSosClient : IStateSosClient, IDisposable
{
    private const int MaxHtmlChars = 2 * 1024 * 1024;
    private const int MaxRequestsPerMinute = 20;

    private readonly HttpClient _http;
    private readonly IReadOnlyList<IStateSosAdapter> _adapters;
    private readonly ILogger<StateSosClient> _logger;

    private readonly ConcurrentQueue<DateTimeOffset> _requestTimestamps = new();
    private readonly SemaphoreSlim _rateLimitGate = new(1, 1);

    internal Func<DateTimeOffset> Clock { get; init; } = static () => DateTimeOffset.UtcNow;
    internal Func<string, CancellationToken, Task<IPAddress[]>> DnsResolve { get; init; } =
        static (host, ct) => Dns.GetHostAddressesAsync(host, ct);

    public StateSosClient(
        HttpClient http,
        IEnumerable<IStateSosAdapter> adapters,
        ILogger<StateSosClient> logger)
    {
        _http = http;
        _adapters = adapters.ToList();
        _logger = logger;
    }

    public void Dispose()
    {
        _rateLimitGate.Dispose();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StateSosSearchResult>?> SearchAsync(
        string companyName, string? stateCode, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companyName, nameof(companyName));

        var matchingAdapters = stateCode is not null
            ? _adapters.Where(a => string.Equals(a.StateCode, stateCode, StringComparison.OrdinalIgnoreCase)).ToList()
            : _adapters.ToList();

        if (matchingAdapters.Count == 0)
        {
            LogNoAdapterFound(stateCode ?? "(all)");
            return null;
        }

        var allResults = new List<StateSosSearchResult>();

        // Search states sequentially (rate limiting + anti-bot)
        foreach (var adapter in matchingAdapters)
        {
            ct.ThrowIfCancellationRequested();

            var results = await SearchStateAsync(adapter, companyName, ct).ConfigureAwait(false);
            if (results is not null)
                allResults.AddRange(results);

            // Stop after first state with results (avoid unnecessary requests)
            if (allResults.Count > 0)
                break;
        }

        return allResults.Count > 0 ? allResults : null;
    }

    private async Task<List<StateSosSearchResult>?> SearchStateAsync(
        IStateSosAdapter adapter, string companyName, CancellationToken ct)
    {
        await EnforceRateLimitAsync(ct).ConfigureAwait(false);

        var requestUri = new Uri(new Uri(adapter.BaseUrl), adapter.SearchPath);

        // SSRF guard
        if (await IsBlockedUrlAsync(requestUri, ct).ConfigureAwait(false))
        {
            var blockedUrl = requestUri.ToString();
            LogBlockedUrl(blockedUrl);
            return null;
        }

        var formData = adapter.BuildSearchForm(companyName);
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
            LogTimeout(adapter.StateCode, timeoutUrl);
            return null;
        }
        catch (HttpRequestException ex)
        {
            var exType = ex.GetType().Name;
            LogFetchFailed(adapter.StateCode, exType);
            return null;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                LogNonSuccess(adapter.StateCode, (int)response.StatusCode);
                return null;
            }

            var html = await ReadHtmlAsync(response.Content, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(html))
                return null;

            return adapter.ParseResults(html);
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
        return b[0] == 10
               || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
               || (b[0] == 192 && b[1] == 168)
               || (b[0] == 169 && b[1] == 254)
               || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);
    }

    // ── Logging ──────────────────────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "StateSoS: No adapter found for state '{StateCode}'")]
    private partial void LogNoAdapterFound(string stateCode);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "StateSoS: Blocked SSRF attempt — '{Url}' resolves to a private IP")]
    private partial void LogBlockedUrl(string url);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "StateSoS [{StateCode}]: Timeout fetching '{Url}'")]
    private partial void LogTimeout(string stateCode, string url);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "StateSoS [{StateCode}]: Fetch failed ({ExceptionType})")]
    private partial void LogFetchFailed(string stateCode, string exceptionType);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "StateSoS [{StateCode}]: Returned status {StatusCode}")]
    private partial void LogNonSuccess(string stateCode, int statusCode);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "StateSoS: Rate limit hit, waiting {WaitTime}")]
    private partial void LogRateLimitHit(TimeSpan waitTime);
}
