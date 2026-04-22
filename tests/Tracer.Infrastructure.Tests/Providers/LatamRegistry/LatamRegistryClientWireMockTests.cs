using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Infrastructure.Providers.LatamRegistry;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Tracer.Infrastructure.Tests.Providers.LatamRegistry;

/// <summary>
/// Integration tests for <see cref="LatamRegistryClient"/> using WireMock to
/// simulate registry HTML responses. Uses a minimal in-test adapter so the
/// client's dispatch, rate limit, SSRF guard and body reader are exercised
/// without depending on a specific country adapter.
/// </summary>
public sealed class LatamRegistryClientWireMockTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly HttpClient _httpClient;
    private readonly LatamRegistryClient _client;
    private readonly FakeAdapter _fakeAdapter;

    public LatamRegistryClientWireMockTests()
    {
        _server = WireMockServer.Start();
        _httpClient = new HttpClient();
        _fakeAdapter = new FakeAdapter(_server.Url!);

        _client = new LatamRegistryClient(
            _httpClient,
            new ILatamRegistryAdapter[] { _fakeAdapter },
            NullLogger<LatamRegistryClient>.Instance)
        {
            // Default stub resolves any host to a public IP so SSRF guard passes;
            // specific tests override this to simulate private / reserved IPs.
            DnsResolve = static (_, _) =>
                Task.FromResult(new[] { System.Net.IPAddress.Parse("93.184.216.34") }),
        };
    }

    public void Dispose()
    {
        _client.Dispose();
        _httpClient.Dispose();
        _server.Dispose();
    }

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task LookupAsync_Match_ReturnsParsedResult()
    {
        _server.Given(Request.Create().WithPath("/lookup").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "text/html; charset=utf-8")
                .WithBody("""
                    <html><body>
                    <table><tr><td>Razón Social</td><td>ACME SA</td></tr></table>
                    </body></html>
                    """));

        var result = await _client.LookupAsync("XX", "123", CancellationToken.None);

        result.Should().NotBeNull();
        result!.EntityName.Should().Be("ACME SA");
    }

    // ── Dispatch ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task LookupAsync_NoAdapterForCountry_ReturnsNull()
    {
        var result = await _client.LookupAsync("ZZ", "whatever", CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task LookupAsync_InvalidIdentifier_ReturnsNull()
    {
        // FakeAdapter normalizes by stripping non-alphanumerics and requires ≥1 char
        var result = await _client.LookupAsync("XX", "   ---   ", CancellationToken.None);
        result.Should().BeNull();
    }

    // ── SSRF ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LookupAsync_ResolvesToPrivateIp_IsBlocked()
    {
        _server.Given(Request.Create().WithPath("/lookup").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("should not reach"));

        using var http = new HttpClient();
        var ssrfAdapter = new FakeAdapter(_server.Url!);
        using var ssrfClient = new LatamRegistryClient(
            http,
            new ILatamRegistryAdapter[] { ssrfAdapter },
            NullLogger<LatamRegistryClient>.Instance)
        {
            DnsResolve = static (_, _) =>
                Task.FromResult(new[] { System.Net.IPAddress.Parse("10.0.0.5") }),
        };

        var result = await ssrfClient.LookupAsync("XX", "123", CancellationToken.None);
        result.Should().BeNull("SSRF guard must block private-IP hosts");
    }

    [Fact]
    public async Task LookupAsync_ResolvesToLoopback_IsBlocked()
    {
        using var http = new HttpClient();
        var adapter = new FakeAdapter(_server.Url!);
        using var client = new LatamRegistryClient(
            http,
            new ILatamRegistryAdapter[] { adapter },
            NullLogger<LatamRegistryClient>.Instance)
        {
            DnsResolve = static (_, _) =>
                Task.FromResult(new[] { System.Net.IPAddress.Loopback }),
        };

        var result = await client.LookupAsync("XX", "123", CancellationToken.None);
        result.Should().BeNull();
    }

    // ── Errors ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task LookupAsync_ServerError_ReturnsNull()
    {
        _server.Given(Request.Create().WithPath("/lookup").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500));

        var result = await _client.LookupAsync("XX", "123", CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task LookupAsync_EmptyBody_ReturnsNull()
    {
        _server.Given(Request.Create().WithPath("/lookup").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(""));

        var result = await _client.LookupAsync("XX", "123", CancellationToken.None);
        result.Should().BeNull();
    }

    // ── Rate limiting ────────────────────────────────────────────────────────

    [Fact]
    public async Task LookupAsync_RateLimitShared_AcrossCalls()
    {
        // With Clock pinned, the 11th request within the minute would be forced
        // to wait; fewer than the per-minute limit pass straight through.
        var now = DateTimeOffset.UtcNow;
        using var http = new HttpClient();
        using var client = new LatamRegistryClient(
            http,
            new ILatamRegistryAdapter[] { new FakeAdapter(_server.Url!) },
            NullLogger<LatamRegistryClient>.Instance)
        {
            Clock = () => now,
            DnsResolve = static (_, _) =>
                Task.FromResult(new[] { System.Net.IPAddress.Parse("93.184.216.34") }),
        };

        _server.Given(Request.Create().WithPath("/lookup").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("<html><body><table><tr><td>Razón Social</td><td>ACME</td></tr></table></body></html>"));

        // 10 calls fit inside the 10 req/minute window.
        for (var i = 0; i < 10; i++)
        {
            var r = await client.LookupAsync("XX", "id" + i, CancellationToken.None);
            r.Should().NotBeNull();
        }
    }

    // ── Test adapter ─────────────────────────────────────────────────────────

    private sealed class FakeAdapter : ILatamRegistryAdapter
    {
        private readonly string _baseUrl;
        public FakeAdapter(string baseUrl) => _baseUrl = baseUrl;
        public string CountryCode => "XX";
        public string BaseUrl => _baseUrl;

        public string? NormalizeIdentifier(string identifier)
        {
            var cleaned = new string(identifier.Where(char.IsLetterOrDigit).ToArray());
            return cleaned.Length > 0 ? cleaned : null;
        }

        public HttpRequestMessage BuildLookupRequest(string normalizedIdentifier) =>
            new(HttpMethod.Get, new Uri(new Uri(_baseUrl), $"lookup?q={normalizedIdentifier}"));

        public LatamRegistrySearchResult? Parse(string body, string normalizedIdentifier)
        {
            // Delegate to a tiny HTML parser: search for "Razón Social" rows.
            var idx = body.IndexOf("Razón Social", StringComparison.Ordinal);
            if (idx < 0) return null;
            var td = body.IndexOf("<td>", idx + "Razón Social".Length, StringComparison.Ordinal);
            if (td < 0) return null;
            var end = body.IndexOf("</td>", td, StringComparison.Ordinal);
            if (end < 0) return null;

            var name = body[(td + 4)..end].Trim();
            return new LatamRegistrySearchResult
            {
                EntityName = name,
                RegistrationId = normalizedIdentifier,
                CountryCode = CountryCode,
            };
        }
    }
}
