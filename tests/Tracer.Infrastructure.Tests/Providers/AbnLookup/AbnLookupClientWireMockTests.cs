using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Infrastructure.Providers.AbnLookup;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Tracer.Infrastructure.Tests.Providers.AbnLookup;

/// <summary>
/// Integration tests for <see cref="AbnLookupClient"/> using WireMock.
/// No real ABN Lookup API calls — all responses are recorded fixtures.
/// Note: ABN Lookup returns a JSON body with a <c>Message</c> field for not-found cases
/// instead of a 404 HTTP status code.
/// </summary>
public sealed class AbnLookupClientWireMockTests : IDisposable
{
    private const string TestGuid = "test-guid-1234";

    private readonly WireMockServer _server;
    private readonly HttpClient _httpClient;
    private readonly AbnLookupClient _client;

    public AbnLookupClientWireMockTests()
    {
        _server = WireMockServer.Start();
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_server.Url!),
        };
        // The client reads the GUID from a custom header set during DI registration
        _httpClient.DefaultRequestHeaders.Add("X-Abn-Guid", TestGuid);

        _client = new AbnLookupClient(
            _httpClient,
            NullLogger<AbnLookupClient>.Instance);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _server.Dispose();
    }

    // ── GetByAbnAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetByAbnAsync_ActiveAbn_ReturnsDetails()
    {
        _server.Given(
            Request.Create()
                .WithPath("/AbnDetails.aspx")
                .WithParam("abn", "49004028077")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(BhpAbnDetailsResponse));

        var result = await _client.GetByAbnAsync("49004028077", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Abn.Should().Be("49004028077");
        result.EntityName.Should().Be("BHP GROUP LIMITED");
        result.AbnStatus.Should().Be("Active");
        result.EntityTypeCode.Should().Be("PUB");
        result.AddressState.Should().Be("VIC");
    }

    [Fact]
    public async Task GetByAbnAsync_AbnNotFound_ReturnsNull()
    {
        // ABN Lookup returns HTTP 200 with a Message field for not-found, not a 404
        _server.Given(
            Request.Create()
                .WithPath("/AbnDetails.aspx")
                .WithParam("abn", "99999999999")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("""{"Message": "No records found for ABN 99999999999."}"""));

        var result = await _client.GetByAbnAsync("99999999999", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByAbnAsync_ServerError_ThrowsHttpRequestException()
    {
        _server.Given(
            Request.Create()
                .WithPath("/AbnDetails.aspx")
                .WithParam("abn", "49004028077")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(500)
                    .WithBody("Service unavailable"));

        var act = () => _client.GetByAbnAsync("49004028077", CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GetByAbnAsync_ServiceUnavailable_ThrowsHttpRequestException()
    {
        _server.Given(
            Request.Create()
                .WithPath("/AbnDetails.aspx")
                .WithParam("abn", "49004028077")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode((int)HttpStatusCode.ServiceUnavailable));

        var act = () => _client.GetByAbnAsync("49004028077", CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    // ── SearchByNameAsync ────────────────────────────────────────────

    [Fact]
    public async Task SearchByNameAsync_ValidName_ReturnsResults()
    {
        _server.Given(
            Request.Create()
                .WithPath("/MatchingNames.aspx")
                .WithParam("name", "BHP Group")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(BhpSearchResponse));

        var results = await _client.SearchByNameAsync("BHP Group", CancellationToken.None);

        results.Should().HaveCount(2);
        results.First().Abn.Should().Be("49004028077");
        results.First().Name.Should().Be("BHP GROUP LIMITED");
        results.Last().Abn.Should().Be("49004028078");
    }

    [Fact]
    public async Task SearchByNameAsync_NoResults_ReturnsEmpty()
    {
        _server.Given(
            Request.Create()
                .WithPath("/MatchingNames.aspx")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("""{"Names": [], "Message": ""}"""));

        var results = await _client.SearchByNameAsync("NonExistentCompanyXYZ", CancellationToken.None);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchByNameAsync_ServerError_ThrowsHttpRequestException()
    {
        _server.Given(
            Request.Create()
                .WithPath("/MatchingNames.aspx")
                .WithParam("name", "BHP Group")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(503));

        var act = () => _client.SearchByNameAsync("BHP Group", CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    // ── Response fixtures ─────────────────────────────────────────────

    private const string BhpAbnDetailsResponse = """
    {
        "Abn": "49004028077",
        "AbnStatus": "Active",
        "AbnStatusEffectiveFrom": "1999-11-01",
        "Gst": "2000-07-01",
        "EntityName": "BHP GROUP LIMITED",
        "EntityTypeCode": "PUB",
        "EntityTypeName": "Australian Public Company",
        "AddressState": "VIC",
        "AddressPostcode": "3000",
        "BusinessName": [],
        "Message": ""
    }
    """;

    private const string BhpSearchResponse = """
    {
        "Names": [
            {
                "Abn": "49004028077",
                "Name": "BHP GROUP LIMITED",
                "NameType": "LGL",
                "State": "VIC",
                "Postcode": "3000",
                "Score": 100
            },
            {
                "Abn": "49004028078",
                "Name": "BHP BILLITON LIMITED",
                "NameType": "TRD",
                "State": "VIC",
                "Postcode": "3000",
                "Score": 85
            }
        ],
        "Message": ""
    }
    """;
}
