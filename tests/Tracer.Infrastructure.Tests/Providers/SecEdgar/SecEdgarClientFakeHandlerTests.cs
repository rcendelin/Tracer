using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Infrastructure.Providers.SecEdgar;

namespace Tracer.Infrastructure.Tests.Providers.SecEdgar;

/// <summary>
/// Integration tests for <see cref="SecEdgarClient"/> using a <see cref="FakeHttpMessageHandler"/>.
/// <para>
/// SEC EDGAR uses absolute URLs (<c>https://efts.sec.gov/...</c> and <c>https://data.sec.gov/...</c>),
/// so WireMock base address substitution cannot intercept requests. Instead we use a custom
/// <see cref="FakeHttpMessageHandler"/> that pattern-matches the request URI and returns canned responses.
/// </para>
/// </summary>
public sealed class SecEdgarClientFakeHandlerTests
{
    // ── SearchByNameAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task SearchByNameAsync_ValidName_ReturnsDistinctSources()
    {
        using var ctx = MakeClient(request =>
            request.RequestUri!.Host == "efts.sec.gov"
                ? JsonResponse(AppleSearchResponse)
                : new HttpResponseMessage(HttpStatusCode.NotFound));

        var results = await ctx.Client.SearchByNameAsync("Apple Inc", CancellationToken.None);

        results.Should().HaveCount(2);
        results.Should().Contain(s => s.EntityName == "APPLE INC");
        results.Should().Contain(s => s.EntityName == "APPLE HOSPITALITY REIT INC");
    }

    [Fact]
    public async Task SearchByNameAsync_EmptyHits_ReturnsEmpty()
    {
        using var ctx = MakeClient(_ => JsonResponse("""{"hits":{"hits":[],"total":{"value":0}}}"""));

        var results = await ctx.Client.SearchByNameAsync("NonExistentCorpXYZ", CancellationToken.None);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchByNameAsync_ServerError_ThrowsHttpRequestException()
    {
        using var ctx = MakeClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var act = () => ctx.Client.SearchByNameAsync("Apple Inc", CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task SearchByNameAsync_DuplicateEntityIds_Deduplicated()
    {
        using var ctx = MakeClient(_ => JsonResponse(DuplicateEntityResponse));

        var results = await ctx.Client.SearchByNameAsync("Duplicate Corp", CancellationToken.None);

        results.Should().HaveCount(1, "duplicate entity IDs should be deduplicated via DistinctBy");
    }

    // ── GetSubmissionsAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetSubmissionsAsync_ValidCik_ReturnsSubmissions()
    {
        using var ctx = MakeClient(request =>
            request.RequestUri!.Host == "data.sec.gov" &&
            request.RequestUri.AbsolutePath.Contains("CIK0000320193.json", StringComparison.Ordinal)
                ? JsonResponse(AppleSubmissionsResponse)
                : new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await ctx.Client.GetSubmissionsAsync("320193", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Apple Inc.");
        result.Sic.Should().Be("3674");
        result.Tickers.Should().Contain("AAPL");
    }

    [Fact]
    public async Task GetSubmissionsAsync_CikPaddedToTenDigits()
    {
        Uri? capturedUri = null;
        using var ctx = MakeClient(request =>
        {
            capturedUri = request.RequestUri;
            return JsonResponse(AppleSubmissionsResponse);
        });

        await ctx.Client.GetSubmissionsAsync("320193", CancellationToken.None);

        capturedUri!.AbsolutePath.Should().Contain("CIK0000320193.json",
            "CIK must be zero-padded to 10 digits in the URL");
    }

    [Fact]
    public async Task GetSubmissionsAsync_CikNotFound_ReturnsNull()
    {
        using var ctx = MakeClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await ctx.Client.GetSubmissionsAsync("9999999999", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSubmissionsAsync_ServerError_ThrowsHttpRequestException()
    {
        using var ctx = MakeClient(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var act = () => ctx.Client.GetSubmissionsAsync("320193", CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static ClientContext MakeClient(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
#pragma warning disable CA2000 // Analyzer cannot track ownership transfer into ClientContext
        var handler = new FakeHttpMessageHandler(respond);
        var httpClient = new HttpClient(handler, disposeHandler: false);
        var secClient = new SecEdgarClient(httpClient, NullLogger<SecEdgarClient>.Instance);
        return new ClientContext(handler, httpClient, secClient);
#pragma warning restore CA2000
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    // ── Response fixtures ─────────────────────────────────────────────────

    private const string AppleSearchResponse = """
    {
        "hits": {
            "total": { "value": 2 },
            "hits": [
                {
                    "_source": {
                        "entity_name": "APPLE INC",
                        "entity_id": "0000320193"
                    }
                },
                {
                    "_source": {
                        "entity_name": "APPLE HOSPITALITY REIT INC",
                        "entity_id": "0001418121"
                    }
                }
            ]
        }
    }
    """;

    private const string DuplicateEntityResponse = """
    {
        "hits": {
            "total": { "value": 2 },
            "hits": [
                {
                    "_source": {
                        "entity_name": "DUPLICATE CORP",
                        "entity_id": "0000111111"
                    }
                },
                {
                    "_source": {
                        "entity_name": "DUPLICATE CORP",
                        "entity_id": "0000111111"
                    }
                }
            ]
        }
    }
    """;

    private const string AppleSubmissionsResponse = """
    {
        "cik": "320193",
        "name": "Apple Inc.",
        "sic": "3674",
        "sicDescription": "Semiconductors And Related Devices",
        "stateOfIncorporation": "CA",
        "entityType": "operating",
        "tickers": ["AAPL"],
        "exchanges": ["Nasdaq"],
        "ein": "94-2404110",
        "addresses": {
            "business": {
                "street1": "ONE APPLE PARK WAY",
                "city": "CUPERTINO",
                "stateOrCountry": "CA",
                "zipCode": "95014"
            }
        }
    }
    """;

    // ── Context type ──────────────────────────────────────────────────────

    // SecEdgarClient does not implement IDisposable (it wraps an injected HttpClient).
    // Only httpClient and handler require explicit disposal.
    private sealed class ClientContext(
        FakeHttpMessageHandler handler,
        HttpClient httpClient,
        SecEdgarClient client) : IDisposable
    {
        public SecEdgarClient Client { get; } = client;

        public void Dispose()
        {
            httpClient.Dispose(); // FakeHttpMessageHandler disposed here only if disposeHandler:true
            handler.Dispose();   // explicit — disposeHandler:false was passed to HttpClient
        }
    }
}

/// <summary>
/// Minimal <see cref="HttpMessageHandler"/> that delegates to a user-supplied function.
/// Used instead of WireMock for providers that use absolute URLs.
/// </summary>
internal sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(handler(request));
}
