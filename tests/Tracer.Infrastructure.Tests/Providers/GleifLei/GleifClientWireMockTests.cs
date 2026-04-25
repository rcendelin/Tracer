using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Infrastructure.Providers.GleifLei;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Tracer.Infrastructure.Tests.Providers.GleifLei;

/// <summary>
/// Integration tests for <see cref="GleifClient"/> using WireMock.
/// No real GLEIF API calls — all responses are recorded fixtures.
/// GLEIF uses relative URLs, so WireMock base address substitution works transparently.
/// </summary>
public sealed class GleifClientWireMockTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly HttpClient _httpClient;
    private readonly GleifClient _client;

    public GleifClientWireMockTests()
    {
        _server = WireMockServer.Start();
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_server.Url! + "/"),
        };

        _client = new GleifClient(
            _httpClient,
            NullLogger<GleifClient>.Instance);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _server.Dispose();
    }

    // ── SearchByNameAsync ────────────────────────────────────────────────

    [Fact]
    public async Task SearchByNameAsync_ValidName_ReturnsRecords()
    {
        // Match path only — query param name encoding varies by runtime/WireMock version
        _server.Given(
            Request.Create()
                .WithPath("/lei-records")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(BhpSearchResponse));

        var results = await _client.SearchByNameAsync("BHP Group Limited", null, CancellationToken.None);

        results.Should().HaveCount(1);
        results.First().Attributes!.Lei.Should().Be("549300EXAMPLELEIBHP1");
        results.First().Attributes!.Entity!.LegalName!.Name.Should().Be("BHP GROUP LIMITED");
    }

    [Fact]
    public async Task SearchByNameAsync_WithCountry_AppendsCountryFilter()
    {
        // Country filter appended as additional query param
        _server.Given(
            Request.Create()
                .WithPath("/lei-records")
                .WithParam("filter[entity.legalAddress.country]", "AU")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(BhpSearchResponse));

        var results = await _client.SearchByNameAsync("BHP Group Limited", "AU", CancellationToken.None);

        results.Should().HaveCount(1);
    }

    [Fact]
    public async Task SearchByNameAsync_NoResults_ReturnsEmpty()
    {
        _server.Given(
            Request.Create()
                .WithPath("/lei-records")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("""{"data": []}"""));

        var results = await _client.SearchByNameAsync("NonExistentCompanyXYZ", null, CancellationToken.None);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchByNameAsync_ServerError_ThrowsHttpRequestException()
    {
        _server.Given(
            Request.Create()
                .WithPath("/lei-records")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(500));

        var act = () => _client.SearchByNameAsync("BHP Group Limited", null, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    // ── GetByLeiAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetByLeiAsync_ValidLei_ReturnsRecord()
    {
        const string lei = "549300EXAMPLELEIBHP1";

        _server.Given(
            Request.Create()
                .WithPath($"/lei-records/{lei}")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(BhpSingleResponse));

        var result = await _client.GetByLeiAsync(lei, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Attributes!.Lei.Should().Be(lei);
        result.Attributes.Entity!.Status.Should().Be("ACTIVE");
    }

    [Fact]
    public async Task GetByLeiAsync_LeiNotFound_ReturnsNull()
    {
        _server.Given(
            Request.Create()
                .WithPath("/lei-records/INVALIDLEIXXXXXXXX00")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode((int)HttpStatusCode.NotFound));

        var result = await _client.GetByLeiAsync("INVALIDLEIXXXXXXXX00", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByLeiAsync_ServerError_ThrowsHttpRequestException()
    {
        _server.Given(
            Request.Create()
                .WithPath("/lei-records/ERRORLEIXXXXXXXXXX00")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(503));

        var act = () => _client.GetByLeiAsync("ERRORLEIXXXXXXXXXX00", CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    // ── GetDirectParentAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetDirectParentAsync_HasParent_ReturnsParentNode()
    {
        const string lei = "549300EXAMPLELEIBHP1";

        _server.Given(
            Request.Create()
                .WithPath($"/lei-records/{lei}/direct-parent")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(ParentResponse));

        var result = await _client.GetDirectParentAsync(lei, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be("549300EXAMPLEPARENTLEI");
        result.Name.Should().Be("BHP PARENT CORP");
    }

    [Fact]
    public async Task GetDirectParentAsync_NoParent_ReturnsNull()
    {
        const string lei = "549300EXAMPLELEIBHP1";

        _server.Given(
            Request.Create()
                .WithPath($"/lei-records/{lei}/direct-parent")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode((int)HttpStatusCode.NotFound));

        var result = await _client.GetDirectParentAsync(lei, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetDirectParentAsync_ServerError_ReturnsNull()
    {
        // GleifClient swallows HttpRequestException for parent lookup and returns null
        const string lei = "549300EXAMPLELEIBHP1";

        _server.Given(
            Request.Create()
                .WithPath($"/lei-records/{lei}/direct-parent")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(500));

        var result = await _client.GetDirectParentAsync(lei, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetDirectParentAsync_CancelledToken_PropagatesOperationCanceledException()
    {
        // GleifClient only catches HttpRequestException — caller cancellation must propagate.
        // This guards against future widening of the catch block to swallow all exceptions.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(true);

        const string lei = "549300EXAMPLELEIBHP1";
        var act = () => _client.GetDirectParentAsync(lei, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Response fixtures ─────────────────────────────────────────────────

    private const string BhpSearchResponse = """
    {
        "data": [
            {
                "id": "549300EXAMPLELEIBHP1",
                "attributes": {
                    "lei": "549300EXAMPLELEIBHP1",
                    "entity": {
                        "legalName": { "name": "BHP GROUP LIMITED", "language": "en" },
                        "legalAddress": {
                            "addressLines": ["171 Collins Street"],
                            "city": "Melbourne",
                            "country": "AU",
                            "postalCode": "3000"
                        },
                        "status": "ACTIVE",
                        "jurisdiction": "AU"
                    },
                    "registration": {
                        "status": "ISSUED"
                    }
                }
            }
        ]
    }
    """;

    private const string BhpSingleResponse = """
    {
        "data": {
            "id": "549300EXAMPLELEIBHP1",
            "attributes": {
                "lei": "549300EXAMPLELEIBHP1",
                "entity": {
                    "legalName": { "name": "BHP GROUP LIMITED", "language": "en" },
                    "legalAddress": {
                        "addressLines": ["171 Collins Street"],
                        "city": "Melbourne",
                        "country": "AU",
                        "postalCode": "3000"
                    },
                    "status": "ACTIVE",
                    "jurisdiction": "AU"
                },
                "registration": {
                    "status": "ISSUED"
                }
            }
        }
    }
    """;

    private const string ParentResponse = """
    {
        "data": [
            {
                "attributes": {
                    "relationship": {
                        "type": "IS_DIRECTLY_CONSOLIDATED_BY",
                        "startNode": { "id": "549300EXAMPLELEIBHP1", "name": "BHP GROUP LIMITED" },
                        "endNode": { "id": "549300EXAMPLEPARENTLEI", "name": "BHP PARENT CORP" }
                    }
                }
            }
        ]
    }
    """;
}
