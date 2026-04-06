using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Tracer.Infrastructure.Providers.CompaniesHouse;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Tracer.Infrastructure.Tests.Providers.CompaniesHouse;

/// <summary>
/// Integration tests for CompaniesHouseClient using WireMock.
/// No real API calls — all responses are recorded fixtures.
/// </summary>
public sealed class CompaniesHouseClientWireMockTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly HttpClient _httpClient;
    private readonly CompaniesHouseClient _client;

    public CompaniesHouseClientWireMockTests()
    {
        _server = WireMockServer.Start();
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_server.Url!),
        };
        _client = new CompaniesHouseClient(
            _httpClient,
            Substitute.For<ILogger<CompaniesHouseClient>>());
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _server.Dispose();
    }

    // ── Search ──────────────────────────────────────────────────────

    [Fact]
    public async Task SearchByNameAsync_ExactMatch_ReturnsResults()
    {
        _server.Given(
            Request.Create()
                .WithPath("/search/companies")
                .WithParam("q", "Tesco")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(TescoSearchResponse));

        var results = await _client.SearchByNameAsync("Tesco", CancellationToken.None);

        results.Should().HaveCount(2);
        results.First().CompanyNumber.Should().Be("00445790");
        results.First().Title.Should().Be("TESCO PLC");
    }

    [Fact]
    public async Task SearchByNameAsync_NoResults_ReturnsEmpty()
    {
        _server.Given(
            Request.Create()
                .WithPath("/search/companies")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("""{"items": [], "total_results": 0}"""));

        var results = await _client.SearchByNameAsync("NonExistentCompany12345", CancellationToken.None);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchByNameAsync_ServerError_ThrowsHttpRequestException()
    {
        _server.Given(
            Request.Create()
                .WithPath("/search/companies")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(500)
                    .WithBody("Internal Server Error"));

        var act = () => _client.SearchByNameAsync("Test", CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    // ── Get Company ─────────────────────────────────────────────────

    [Fact]
    public async Task GetCompanyAsync_Found_ReturnsProfile()
    {
        _server.Given(
            Request.Create()
                .WithPath("/company/00445790")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(TescoCompanyResponse));

        var company = await _client.GetCompanyAsync("00445790", CancellationToken.None);

        company.Should().NotBeNull();
        company!.CompanyName.Should().Be("TESCO PLC");
        company.CompanyNumber.Should().Be("00445790");
        company.CompanyStatus.Should().Be("active");
        company.Type.Should().Be("plc");
        company.SicCodes.Should().Contain("47110");
        company.RegisteredOfficeAddress.Should().NotBeNull();
        company.RegisteredOfficeAddress!.PostalCode.Should().Be("AL7 1GA");
    }

    [Fact]
    public async Task GetCompanyAsync_NotFound_ReturnsNull()
    {
        _server.Given(
            Request.Create()
                .WithPath("/company/99999999")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(404)
                    .WithBody("""{"errors": [{"error": "company-profile-not-found"}]}"""));

        var company = await _client.GetCompanyAsync("99999999", CancellationToken.None);

        company.Should().BeNull();
    }

    [Fact]
    public async Task GetCompanyAsync_RateLimited_ThrowsHttpRequestException()
    {
        _server.Given(
            Request.Create()
                .WithPath("/company/00445790")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(429)
                    .WithHeader("Retry-After", "300")
                    .WithBody("Rate limit exceeded"));

        var act = () => _client.GetCompanyAsync("00445790", CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GetCompanyAsync_DissolvedCompany_MapsCessationDate()
    {
        _server.Given(
            Request.Create()
                .WithPath("/company/01234567")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(DissolvedCompanyResponse));

        var company = await _client.GetCompanyAsync("01234567", CancellationToken.None);

        company.Should().NotBeNull();
        company!.CompanyStatus.Should().Be("dissolved");
        company.DateOfCessation.Should().Be("2020-03-15");
    }

    // ── Recorded API response fixtures ──────────────────────────────

    private const string TescoSearchResponse = """
    {
        "items": [
            {
                "company_number": "00445790",
                "title": "TESCO PLC",
                "company_status": "active",
                "company_type": "plc",
                "address_snippet": "Tesco House, Shire Park, Kestrel Way, Welwyn Garden City, Hertfordshire, AL7 1GA"
            },
            {
                "company_number": "02402939",
                "title": "TESCO STORES LIMITED",
                "company_status": "active",
                "company_type": "ltd",
                "address_snippet": "Tesco House, Shire Park, Kestrel Way, Welwyn Garden City, Hertfordshire, AL7 1GA"
            }
        ],
        "total_results": 2
    }
    """;

    private const string TescoCompanyResponse = """
    {
        "company_name": "TESCO PLC",
        "company_number": "00445790",
        "company_status": "active",
        "type": "plc",
        "date_of_creation": "1947-11-27",
        "jurisdiction": "england-wales",
        "sic_codes": ["47110"],
        "registered_office_address": {
            "address_line_1": "Tesco House",
            "address_line_2": "Shire Park, Kestrel Way",
            "locality": "Welwyn Garden City",
            "region": "Hertfordshire",
            "postal_code": "AL7 1GA",
            "country": "United Kingdom"
        }
    }
    """;

    private const string DissolvedCompanyResponse = """
    {
        "company_name": "OLD DISSOLVED LTD",
        "company_number": "01234567",
        "company_status": "dissolved",
        "type": "ltd",
        "date_of_creation": "2010-01-01",
        "date_of_cessation": "2020-03-15",
        "registered_office_address": {
            "address_line_1": "1 High Street",
            "locality": "London",
            "postal_code": "EC1A 1BB",
            "country": "England"
        }
    }
    """;
}
