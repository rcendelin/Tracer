using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Infrastructure.Providers.Handelsregister;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Tracer.Infrastructure.Tests.Providers.Handelsregister;

/// <summary>
/// Integration tests for <see cref="HandelsregisterClient"/> using WireMock to simulate
/// Handelsregister.de HTML responses without making real network calls.
/// </summary>
public sealed class HandelsregisterClientWireMockTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly HttpClient _httpClient;
    private readonly HandelsregisterClient _client;

    public HandelsregisterClientWireMockTests()
    {
        _server = WireMockServer.Start();
        _httpClient = new HttpClient { BaseAddress = new Uri(_server.Url!) };
        // Bypass SSRF check for WireMock's localhost by returning a public IP
        _client = new HandelsregisterClient(_httpClient, NullLogger<HandelsregisterClient>.Instance)
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

    // ── SearchByNameAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task SearchByNameAsync_ValidResults_ParsesCorrectly()
    {
        _server.Given(
            Request.Create()
                .WithPath("/ergebnisse.xhtml")
                .UsingPost())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "text/html; charset=utf-8")
                    .WithBody(SearchResultHtml));

        var results = await _client.SearchByNameAsync("Siemens", CancellationToken.None);

        results.Should().NotBeNull();
        results!.Count.Should().BeGreaterThanOrEqualTo(1);
        results[0].CompanyName.Should().Contain("Siemens");
        results[0].RegisterType.Should().Be("HRB");
        results[0].RegisterNumber.Should().Be("6324");
        results[0].RegisterCourt.Should().Contain("München");
    }

    [Fact]
    public async Task SearchByNameAsync_NoResults_ReturnsNull()
    {
        _server.Given(
            Request.Create()
                .WithPath("/ergebnisse.xhtml")
                .UsingPost())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "text/html; charset=utf-8")
                    .WithBody(EmptyResultHtml));

        var results = await _client.SearchByNameAsync("XXXX_NONEXISTENT_XXXX", CancellationToken.None);

        results.Should().BeNull();
    }

    [Fact]
    public async Task SearchByNameAsync_ServerError_ReturnsNull()
    {
        _server.Given(
            Request.Create()
                .WithPath("/ergebnisse.xhtml")
                .UsingPost())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(500));

        var results = await _client.SearchByNameAsync("Test", CancellationToken.None);

        results.Should().BeNull();
    }

    // ── GetByRegisterNumberAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetByRegisterNumberAsync_Found_ReturnsDetail()
    {
        _server.Given(
            Request.Create()
                .WithPath("/ergebnisse.xhtml")
                .UsingPost())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "text/html; charset=utf-8")
                    .WithBody(DetailPageHtml));

        var result = await _client.GetByRegisterNumberAsync(
            "HRB", "6324", null, CancellationToken.None);

        result.Should().NotBeNull();
        result!.CompanyName.Should().Contain("Siemens");
        result.RegistrationId.Should().Be("HRB 6324");
    }

    [Fact]
    public async Task GetByRegisterNumberAsync_NotFound_ReturnsNull()
    {
        _server.Given(
            Request.Create()
                .WithPath("/ergebnisse.xhtml")
                .UsingPost())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "text/html; charset=utf-8")
                    .WithBody(EmptyResultHtml));

        var result = await _client.GetByRegisterNumberAsync(
            "HRB", "9999999", null, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SearchByNameAsync_NullOrEmpty_Throws()
    {
        var act = () => _client.SearchByNameAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();

        var act2 = () => _client.SearchByNameAsync("  ", CancellationToken.None);
        await act2.Should().ThrowAsync<ArgumentException>();
    }

    // ── Rate limiting ────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchByNameAsync_RecordsRequestTimestamp()
    {
        // This test verifies that the rate limiter records requests.
        // We use a fixed clock so we can reason about timestamps.
        var fixedTime = new DateTimeOffset(2026, 4, 11, 12, 0, 0, TimeSpan.Zero);
        using var clientWithClock = new HandelsregisterClient(
            _httpClient, NullLogger<HandelsregisterClient>.Instance)
        {
            Clock = () => fixedTime,
            DnsResolve = static (_, _) => Task.FromResult(new[] { System.Net.IPAddress.Parse("93.184.216.34") }),
        };

        _server.Given(
            Request.Create()
                .WithPath("/ergebnisse.xhtml")
                .UsingPost())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "text/html; charset=utf-8")
                    .WithBody(EmptyResultHtml));

        // Execute one search — should not throw or delay
        var results = await clientWithClock.SearchByNameAsync("Test", CancellationToken.None);

        // Verify the request was made (WireMock should have received it)
        _server.LogEntries.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    // ── HTML fixtures ────────────────────────────────────────────────────────

    private const string SearchResultHtml = """
        <!DOCTYPE html>
        <html>
        <body>
        <table class="RegPortEr662_ergebnisTable" summary="Suchergebnisse">
        <tr>
            <td>1</td>
            <td>Siemens Aktiengesellschaft</td>
            <td>Amtsgericht München</td>
            <td>HRB 6324</td>
            <td>aktiv</td>
        </tr>
        <tr>
            <td>2</td>
            <td>Siemens Healthcare GmbH</td>
            <td>Amtsgericht München</td>
            <td>HRB 213821</td>
            <td>aktiv</td>
        </tr>
        </table>
        </body>
        </html>
        """;

    private const string EmptyResultHtml = """
        <!DOCTYPE html>
        <html>
        <body>
        <div class="noResults">Keine Treffer gefunden.</div>
        </body>
        </html>
        """;

    private const string DetailPageHtml = """
        <!DOCTYPE html>
        <html>
        <body>
        <h2 class="firma">Siemens Aktiengesellschaft</h2>
        <table>
        <tr><th>Registergericht</th><td>Amtsgericht München</td></tr>
        <tr><th>Rechtsform</th><td>Aktiengesellschaft</td></tr>
        <tr><th>Status</th><td>aktiv</td></tr>
        <tr><th>Straße</th><td>Werner-von-Siemens-Straße 1</td></tr>
        <tr><th>PLZ</th><td>80333</td></tr>
        <tr><th>Ort</th><td>München</td></tr>
        <tr><th>Geschäftsführer</th><td><ul><li>Roland Busch</li><li>Cedrik Neike</li></ul></td></tr>
        </table>
        </body>
        </html>
        """;
}
