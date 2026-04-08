using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Infrastructure.Providers.WebScraper;

namespace Tracer.Infrastructure.Tests.Providers.WebScraper;


/// <summary>
/// Unit tests for <see cref="WebScraperClient"/> using a <see cref="FakeHttpMessageHandler"/>.
/// Covers JSON-LD extraction, Open Graph fallback, HTML pattern fallback, and safety guards.
/// </summary>
public sealed class WebScraperClientTests
{
    // Public Cloudflare anycast IP — used as stub for all non-SSRF test hostnames so that
    // SSRF DNS validation never performs a real network lookup.
    private static readonly IPAddress[] PublicStubAddresses = [IPAddress.Parse("1.1.1.1")];

    // Stub resolver: returns a public IP for any hostname, simulating a public-facing site.
    private static Task<IPAddress[]> StubPublicDns(string host, CancellationToken ct)
        => Task.FromResult(PublicStubAddresses);

    private static SutContext CreateSut(Func<HttpRequestMessage, HttpResponseMessage> respond,
        Func<string, CancellationToken, Task<IPAddress[]>>? dnsResolver = null)
    {
        var handler = new FakeHttpMessageHandler(respond);
        var httpClient = new HttpClient(handler);
        var sut = new WebScraperClient(httpClient, NullLogger<WebScraperClient>.Instance)
        {
            // Default: return a public IP to avoid real DNS lookups in unit tests.
            DnsResolve = dnsResolver ?? StubPublicDns,
        };
        return new SutContext(handler, httpClient, sut);
    }

    private static HttpResponseMessage HtmlResponse(string html) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(html, Encoding.UTF8, "text/html"),
        };

    private sealed class SutContext(
        FakeHttpMessageHandler handler,
        HttpClient httpClient,
        WebScraperClient client) : IDisposable
    {
        public WebScraperClient Client { get; } = client;

        public void Dispose()
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    // ── JSON-LD extraction ─────────────────────────────────────────────────

    [Fact]
    public async Task ScrapeAsync_JsonLdOrganization_ExtractsAllFields()
    {
        const string html = """
            <html><head>
            <script type="application/ld+json">
            {
              "@context": "https://schema.org",
              "@type": "Organization",
              "name": "Acme Corporation",
              "telephone": "+420 800 123 456",
              "email": "info@acme.cz",
              "url": "https://www.acme.cz",
              "description": "Leading supplier of industrial tools.",
              "address": {
                "@type": "PostalAddress",
                "streetAddress": "Průmyslová 1",
                "addressLocality": "Praha",
                "postalCode": "11000",
                "addressCountry": "CZ"
              }
            }
            </script>
            </head><body><p>Welcome</p></body></html>
            """;

        using var ctx = CreateSut(_ => HtmlResponse(html));
        var result = await ctx.Client.ScrapeAsync("https://www.acme.cz", CancellationToken.None);

        result.Should().NotBeNull();
        result!.CompanyName.Should().Be("Acme Corporation");
        result.Phone.Should().Be("+420 800 123 456");
        result.Email.Should().Be("info@acme.cz");
        result.Website.Should().Be("https://www.acme.cz");
        result.Description.Should().Be("Leading supplier of industrial tools.");
        result.Address.Should().NotBeNull();
        result.Address!.City.Should().Be("Praha");
        result.Address.PostalCode.Should().Be("11000");
        result.Address.Country.Should().Be("CZ");
        result.SourceUrl.Should().Be("https://www.acme.cz");
    }

    [Fact]
    public async Task ScrapeAsync_JsonLdLocalBusiness_SetsIndustryFromType()
    {
        const string html = """
            <html><head>
            <script type="application/ld+json">
            {"@type":"Restaurant","name":"Dobrá Restaurace","telephone":"+420 555 000 111"}
            </script>
            </head></html>
            """;

        using var ctx = CreateSut(_ => HtmlResponse(html));
        var result = await ctx.Client.ScrapeAsync("https://dobra.cz", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Industry.Should().Be("Restaurant");
    }

    [Fact]
    public async Task ScrapeAsync_JsonLdOrganizationType_IndustryIsNull()
    {
        // Generic "Organization" type should NOT set Industry (too broad)
        const string html = """
            <html><head>
            <script type="application/ld+json">
            {"@type":"Organization","name":"Generic Corp"}
            </script>
            </head></html>
            """;

        using var ctx = CreateSut(_ => HtmlResponse(html));
        var result = await ctx.Client.ScrapeAsync("https://generic.cz", CancellationToken.None);

        result!.Industry.Should().BeNull();
    }

    [Fact]
    public async Task ScrapeAsync_JsonLdArray_ExtractsFirstOrganization()
    {
        const string html = """
            <html><head>
            <script type="application/ld+json">
            [
              {"@type":"WebSite","name":"Example"},
              {"@type":"Organization","name":"Real Company","telephone":"+1 555 000"}
            ]
            </script>
            </head></html>
            """;

        using var ctx = CreateSut(_ => HtmlResponse(html));
        var result = await ctx.Client.ScrapeAsync("https://example.com", CancellationToken.None);

        result!.CompanyName.Should().Be("Real Company");
    }

    [Fact]
    public async Task ScrapeAsync_JsonLdTypeArray_MatchedWhenOrganizationInArray()
    {
        // schema.org allows @type to be an array: ["Organization","LocalBusiness"]
        const string html = """
            <html><head>
            <script type="application/ld+json">
            {"@type":["Organization","LocalBusiness"],"name":"Multi-Type Corp","telephone":"+1 800 000"}
            </script>
            </head></html>
            """;

        using var ctx = CreateSut(_ => HtmlResponse(html));
        var result = await ctx.Client.ScrapeAsync("https://multitype.com", CancellationToken.None);

        result.Should().NotBeNull();
        result!.CompanyName.Should().Be("Multi-Type Corp");
    }

    [Fact]
    public async Task ScrapeAsync_JsonLdWithStringAddress_ExtractsStreet()
    {
        const string html = """
            <html><head>
            <script type="application/ld+json">
            {"@type":"Organization","name":"Firma","address":"Náměstí 1, Praha"}
            </script>
            </head></html>
            """;

        using var ctx = CreateSut(_ => HtmlResponse(html));
        var result = await ctx.Client.ScrapeAsync("https://firma.cz", CancellationToken.None);

        result!.Address!.Street.Should().Be("Náměstí 1, Praha");
    }

    [Fact]
    public async Task ScrapeAsync_JsonLdWithoutName_SkipsJsonLd()
    {
        // JSON-LD without "name" should be skipped — not useful enough
        const string html = """
            <html><head>
            <script type="application/ld+json">
            {"@type":"Organization","telephone":"+420 555 000"}
            </script>
            <meta property="og:site_name" content="Fallback Name" />
            </head></html>
            """;

        using var ctx = CreateSut(_ => HtmlResponse(html));
        var result = await ctx.Client.ScrapeAsync("https://fallback.cz", CancellationToken.None);

        // Should fall back to Open Graph
        result!.CompanyName.Should().Be("Fallback Name");
    }

    [Fact]
    public async Task ScrapeAsync_MalformedJsonLd_FallsBackToOpenGraph()
    {
        const string html = """
            <html><head>
            <script type="application/ld+json">{ invalid json {{ </script>
            <meta property="og:site_name" content="OG Company" />
            </head></html>
            """;

        using var ctx = CreateSut(_ => HtmlResponse(html));
        var result = await ctx.Client.ScrapeAsync("https://test.cz", CancellationToken.None);

        result!.CompanyName.Should().Be("OG Company");
    }

    // ── Open Graph extraction ──────────────────────────────────────────────

    [Fact]
    public async Task ScrapeAsync_OpenGraph_ExtractsNameAndDescription()
    {
        const string html = """
            <html><head>
            <meta property="og:site_name" content="ŠKODA AUTO" />
            <meta property="og:description" content="Czech car manufacturer." />
            </head><body></body></html>
            """;

        using var ctx = CreateSut(_ => HtmlResponse(html));
        var result = await ctx.Client.ScrapeAsync("https://skoda-auto.cz", CancellationToken.None);

        result.Should().NotBeNull();
        result!.CompanyName.Should().Be("ŠKODA AUTO");
        result.Description.Should().Be("Czech car manufacturer.");
    }

    [Fact]
    public async Task ScrapeAsync_OpenGraphTitle_UsedWhenSiteNameMissing()
    {
        const string html = """
            <html><head>
            <meta property="og:title" content="Acme | Home" />
            </head></html>
            """;

        using var ctx = CreateSut(_ => HtmlResponse(html));
        var result = await ctx.Client.ScrapeAsync("https://acme.cz", CancellationToken.None);

        result!.CompanyName.Should().Be("Acme | Home");
    }

    // ── HTML pattern extraction ────────────────────────────────────────────

    [Fact]
    public async Task ScrapeAsync_HtmlMailtoLink_ExtractsEmail()
    {
        const string html = """
            <html><body>
            <a href="mailto:contact@firma.cz">Kontaktujte nás</a>
            </body></html>
            """;

        using var ctx = CreateSut(_ => HtmlResponse(html));
        var result = await ctx.Client.ScrapeAsync("https://firma.cz", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Email.Should().Be("contact@firma.cz");
    }

    [Fact]
    public async Task ScrapeAsync_HtmlMailtoWithQueryString_StripsQueryString()
    {
        const string html = """
            <html><body>
            <a href="mailto:info@co.cz?subject=Hello">Mail us</a>
            </body></html>
            """;

        using var ctx = CreateSut(_ => HtmlResponse(html));
        var result = await ctx.Client.ScrapeAsync("https://co.cz", CancellationToken.None);

        result!.Email.Should().Be("info@co.cz");
    }

    [Fact]
    public async Task ScrapeAsync_HtmlTelLink_ExtractsPhone()
    {
        const string html = """
            <html><body>
            <a href="tel:+420800123456">Zavolat</a>
            </body></html>
            """;

        using var ctx = CreateSut(_ => HtmlResponse(html));
        var result = await ctx.Client.ScrapeAsync("https://firma.cz", CancellationToken.None);

        result!.Phone.Should().Be("+420800123456");
    }

    [Fact]
    public async Task ScrapeAsync_PageTitle_CleanedAndUsedAsFallback()
    {
        const string html = """
            <html><head><title>Acme Corp | Home</title></head><body></body></html>
            """;

        using var ctx = CreateSut(_ => HtmlResponse(html));
        var result = await ctx.Client.ScrapeAsync("https://acme.cz", CancellationToken.None);

        result!.CompanyName.Should().Be("Acme Corp");
    }

    [Fact]
    public async Task ScrapeAsync_TitleWithDash_CleanedCorrectly()
    {
        const string html = """
            <html><head><title>BHP Group - Global Resources</title></head><body></body></html>
            """;

        using var ctx = CreateSut(_ => HtmlResponse(html));
        var result = await ctx.Client.ScrapeAsync("https://bhp.com", CancellationToken.None);

        result!.CompanyName.Should().Be("BHP Group");
    }

    // ── Merge — JSON-LD wins, gaps filled from lower-priority ────────────────

    [Fact]
    public async Task ScrapeAsync_JsonLdMissingPhone_FilledFromTelLink()
    {
        const string html = """
            <html><head>
            <script type="application/ld+json">
            {"@type":"Organization","name":"TestCo"}
            </script>
            </head><body>
            <a href="tel:+420123456789">Telefon</a>
            </body></html>
            """;

        using var ctx = CreateSut(_ => HtmlResponse(html));
        var result = await ctx.Client.ScrapeAsync("https://testco.cz", CancellationToken.None);

        result!.CompanyName.Should().Be("TestCo");      // from JSON-LD
        result.Phone.Should().Be("+420123456789");       // filled from HTML
    }

    // ── Safety guards ──────────────────────────────────────────────────────

    [Fact]
    public async Task ScrapeAsync_NonHtmlContentType_ReturnsNull()
    {
        using var ctx = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        });

        var result = await ctx.Client.ScrapeAsync("https://api.example.com/data", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ScrapeAsync_NotFound_ReturnsNull()
    {
        using var ctx = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await ctx.Client.ScrapeAsync("https://example.com/missing", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ScrapeAsync_HttpRequestException_ReturnsNull()
    {
        using var handler = new ThrowingHttpMessageHandler(new HttpRequestException("Network error"));
        using var httpClient = new HttpClient(handler);
        var sut = new WebScraperClient(httpClient, NullLogger<WebScraperClient>.Instance);

        var result = await sut.ScrapeAsync("https://unreachable.example.com", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ScrapeAsync_PollyTimeout_ReturnsNull()
    {
        // Simulate Polly AttemptTimeout: OperationCanceledException with a non-cancelled caller token
        using var callerCts = new CancellationTokenSource();
        using var handler = new ThrowingHttpMessageHandler(new OperationCanceledException("Polly timeout"));
        using var httpClient = new HttpClient(handler);
        var sut = new WebScraperClient(httpClient, NullLogger<WebScraperClient>.Instance);

        // Caller's token is NOT cancelled — the exception comes from Polly's internal token
        var result = await sut.ScrapeAsync("https://slow.example.com", callerCts.Token);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ScrapeAsync_InvalidUrl_ReturnsNull()
    {
        using var ctx = CreateSut(_ => HtmlResponse("<html></html>"));

        var result = await ctx.Client.ScrapeAsync("not-a-url", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ScrapeAsync_FtpUrl_ReturnsNull()
    {
        using var ctx = CreateSut(_ => HtmlResponse("<html></html>"));

        var result = await ctx.Client.ScrapeAsync("ftp://files.example.com/data", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ScrapeAsync_EmptyPage_ReturnsNull()
    {
        using var ctx = CreateSut(_ => HtmlResponse(string.Empty));

        var result = await ctx.Client.ScrapeAsync("https://empty.example.com", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ScrapeAsync_NoStructuredData_ReturnsNull()
    {
        const string html = """
            <html><head><title>   </title></head>
            <body><p>Some text without any contact info or structure</p></body></html>
            """;

        using var ctx = CreateSut(_ => HtmlResponse(html));
        var result = await ctx.Client.ScrapeAsync("https://nodata.example.com", CancellationToken.None);

        result.Should().BeNull();
    }

    // ── Email normalization ────────────────────────────────────────────────

    [Fact]
    public async Task ScrapeAsync_EmailWithUpperCase_Lowercased()
    {
        const string html = """
            <html><body><a href="mailto:Info@Company.CZ">email</a></body></html>
            """;

        using var ctx = CreateSut(_ => HtmlResponse(html));
        var result = await ctx.Client.ScrapeAsync("https://company.cz", CancellationToken.None);

        result!.Email.Should().Be("info@company.cz");
    }

    [Fact]
    public async Task ScrapeAsync_InvalidEmailInMailtoLink_Ignored()
    {
        const string html = """
            <html><body><a href="mailto:not-an-email">contact</a></body></html>
            """;

        using var ctx = CreateSut(_ => HtmlResponse(html));
        var result = await ctx.Client.ScrapeAsync("https://company.cz", CancellationToken.None);

        result.Should().BeNull();
    }

    // ── Phone normalization ────────────────────────────────────────────────

    [Fact]
    public async Task ScrapeAsync_ShortPhone_Ignored()
    {
        // tel links shorter than 7 chars are invalid and should be discarded
        const string html = """
            <html><body><a href="tel:123">short</a></body></html>
            """;

        using var ctx = CreateSut(_ => HtmlResponse(html));
        var result = await ctx.Client.ScrapeAsync("https://company.cz", CancellationToken.None);

        result.Should().BeNull();
    }

    // ── Description truncation ─────────────────────────────────────────────

    [Fact]
    public async Task ScrapeAsync_LongDescription_TruncatedAt500Chars()
    {
        var longDesc = new string('x', 600);
        // $$"""...""" uses {{expr}} for interpolation, so bare { and } are literal
        var html = $$"""
            <html><head>
            <script type="application/ld+json">
            {"@type":"Organization","name":"TestCo","description":"{{longDesc}}"}
            </script>
            </head></html>
            """;

        using var ctx = CreateSut(_ => HtmlResponse(html));
        var result = await ctx.Client.ScrapeAsync("https://testco.cz", CancellationToken.None);

        result!.Description.Should().HaveLength(501); // 500 chars + '…'
        result.Description.Should().EndWith("…");
    }

    // ── SSRF protection ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://127.0.0.1:8080/admin")]
    [InlineData("http://10.0.0.1/")]
    [InlineData("http://10.255.255.255/internal")]
    [InlineData("http://172.16.0.1/")]
    [InlineData("http://172.31.255.255/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://192.168.0.1/router")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://169.254.1.1/")]
    [InlineData("http://100.64.0.1/")]
    [InlineData("http://100.127.255.255/")]
    public async Task ScrapeAsync_PrivateOrReservedIp_ReturnsNull(string address)
    {
        // The HTTP handler must NOT be called — IsBlockedUrlAsync should block before the request.
        var wasCalled = false;
        using var ctx = CreateSut(_ =>
        {
            wasCalled = true;
            return HtmlResponse("<html></html>");
        });

        var result = await ctx.Client.ScrapeAsync(address, CancellationToken.None);

        result.Should().BeNull();
        wasCalled.Should().BeFalse("HTTP handler should not be invoked for private IPs");
    }

    [Fact]
    public async Task ScrapeAsync_LoopbackHostname_ReturnsNull()
    {
        // Stub DNS resolver that mimics "localhost" → 127.0.0.1 without real DNS.
        static Task<IPAddress[]> LoopbackDns(string host, CancellationToken ct)
            => Task.FromResult<IPAddress[]>([IPAddress.Loopback]);

        var wasCalled = false;
        using var ctx = CreateSut(_ =>
        {
            wasCalled = true;
            return HtmlResponse("<html></html>");
        }, dnsResolver: LoopbackDns);

        var result = await ctx.Client.ScrapeAsync("http://localhost/", CancellationToken.None);

        result.Should().BeNull();
        wasCalled.Should().BeFalse("HTTP handler should not be invoked for loopback hostnames");
    }

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("172.16.0.1")]
    public async Task ScrapeAsync_PrivateIpHostname_ReturnsNull(string ip)
    {
        // Stub resolver returns a private IP — simulates DNS rebinding or internal hostname.
        var wasCalled = false;
        using var ctx = CreateSut(_ =>
        {
            wasCalled = true;
            return HtmlResponse("<html></html>");
        }, dnsResolver: (_, _) => Task.FromResult<IPAddress[]>([IPAddress.Parse(ip)]));

        var result = await ctx.Client.ScrapeAsync("http://internal.corp.example/", CancellationToken.None);

        result.Should().BeNull();
        wasCalled.Should().BeFalse("HTTP handler should not be invoked when DNS resolves to private IP");
    }

    [Fact]
    public async Task ScrapeAsync_UnresolvableHost_ReturnsNull()
    {
        // Stub resolver throws SocketException — simulates NXDOMAIN / DNS failure.
        static Task<IPAddress[]> NxDomainResolver(string host, CancellationToken ct)
            => Task.FromException<IPAddress[]>(new SocketException());

        var wasCalled = false;
        using var ctx = CreateSut(_ =>
        {
            wasCalled = true;
            return HtmlResponse("<html></html>");
        }, dnsResolver: NxDomainResolver);

        var result = await ctx.Client.ScrapeAsync("http://nonexistent.invalid/", CancellationToken.None);

        result.Should().BeNull();
        wasCalled.Should().BeFalse("HTTP handler should not be invoked when hostname cannot be resolved");
    }

    // ── FakeHttpMessageHandler ─────────────────────────────────────────────

    private sealed class ThrowingHttpMessageHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(exception);
    }
}

/// <summary>
/// Minimal <see cref="HttpMessageHandler"/> that delegates to a user-supplied function.
/// Shared across scraper + SEC EDGAR tests to avoid duplication in the test assembly.
/// </summary>
internal sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(handler(request));
}
