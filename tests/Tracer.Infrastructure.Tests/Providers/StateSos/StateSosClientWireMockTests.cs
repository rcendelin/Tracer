using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Infrastructure.Providers.StateSos;
using Tracer.Infrastructure.Providers.StateSos.Adapters;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Tracer.Infrastructure.Tests.Providers.StateSos;

/// <summary>
/// Integration tests for <see cref="StateSosClient"/> using WireMock to simulate
/// US Secretary of State registry HTML responses.
/// </summary>
public sealed class StateSosClientWireMockTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly HttpClient _httpClient;
    private readonly StateSosClient _client;

    // Create a California adapter that points to the WireMock server
    private sealed class TestCaliforniaAdapter : IStateSosAdapter
    {
        private readonly string _baseUrl;
        public TestCaliforniaAdapter(string baseUrl) => _baseUrl = baseUrl;
        public string StateCode => "CA";
        public string BaseUrl => _baseUrl;
        public string SearchPath => "CBS/SearchResults";

        public Dictionary<string, string> BuildSearchForm(string companyName) =>
            new()
            {
                ["SearchType"] = "CORP",
                ["SearchCriteria"] = companyName,
                ["SearchSubType"] = "Keyword",
            };

        public List<StateSosSearchResult>? ParseResults(string html)
        {
            // Delegate to real adapter
            return new CaliforniaAdapter().ParseResults(html);
        }
    }

    public StateSosClientWireMockTests()
    {
        _server = WireMockServer.Start();
        _httpClient = new HttpClient();

        var testAdapter = new TestCaliforniaAdapter(_server.Url!);

        _client = new StateSosClient(
            _httpClient,
            new IStateSosAdapter[] { testAdapter },
            NullLogger<StateSosClient>.Instance)
        {
            DnsResolve = static (_, _) => Task.FromResult(new[] { System.Net.IPAddress.Parse("93.184.216.34") }),
        };
    }

    public void Dispose()
    {
        _client.Dispose();
        _httpClient.Dispose();
        _server.Dispose();
    }

    // ── SearchAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_CaliforniaFound_ReturnsResults()
    {
        _server.Given(
            Request.Create()
                .WithPath("/CBS/SearchResults")
                .UsingPost())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "text/html; charset=utf-8")
                    .WithBody(CaliforniaResultsHtml));

        var results = await _client.SearchAsync("Apple Inc", "CA", CancellationToken.None);

        results.Should().NotBeNull();
        results!.Count.Should().BeGreaterThanOrEqualTo(1);
        results[0].EntityName.Should().Contain("APPLE");
        results[0].FilingNumber.Should().Be("C0806592");
        results[0].StateCode.Should().Be("CA");
    }

    [Fact]
    public async Task SearchAsync_NoResults_ReturnsNull()
    {
        _server.Given(
            Request.Create()
                .WithPath("/CBS/SearchResults")
                .UsingPost())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "text/html; charset=utf-8")
                    .WithBody(EmptyResultsHtml));

        var results = await _client.SearchAsync("XXXX_NONEXISTENT_XXXX", "CA", CancellationToken.None);

        results.Should().BeNull();
    }

    [Fact]
    public async Task SearchAsync_ServerError_ReturnsNull()
    {
        _server.Given(
            Request.Create()
                .WithPath("/CBS/SearchResults")
                .UsingPost())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(500));

        var results = await _client.SearchAsync("Test", "CA", CancellationToken.None);

        results.Should().BeNull();
    }

    [Fact]
    public async Task SearchAsync_NullCompanyName_Throws()
    {
        var act = () => _client.SearchAsync(null!, "CA", CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SearchAsync_UnsupportedState_ReturnsNull()
    {
        // Only CA adapter is registered in tests
        var results = await _client.SearchAsync("Test Corp", "TX", CancellationToken.None);

        results.Should().BeNull();
    }

    // ── HTML fixtures ────────────────────────────────────────────────────────

    private const string CaliforniaResultsHtml = """
        <!DOCTYPE html>
        <html>
        <body>
        <table>
        <thead><tr><th>Entity Name</th><th>Filing Number</th><th>Status</th><th>Type</th><th>Formation</th></tr></thead>
        <tbody>
        <tr>
            <td>APPLE INC.</td>
            <td>C0806592</td>
            <td>Active</td>
            <td>Corporation</td>
            <td>01/03/1977</td>
        </tr>
        <tr>
            <td>APPLE LEISURE GROUP LLC</td>
            <td>201711710255</td>
            <td>Active</td>
            <td>LLC</td>
            <td>05/15/2017</td>
        </tr>
        </tbody>
        </table>
        </body>
        </html>
        """;

    private const string EmptyResultsHtml = """
        <!DOCTYPE html>
        <html>
        <body>
        <div class="no-results">No entities found matching your search criteria.</div>
        </body>
        </html>
        """;
}
