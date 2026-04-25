using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;
using Tracer.Infrastructure.Providers.WebScraper;

namespace Tracer.Infrastructure.Tests.Providers.WebScraper;

/// <summary>
/// Unit tests for <see cref="WebScraperProvider"/>.
/// Uses NSubstitute for <see cref="IWebScraperClient"/> and NullLogger
/// (required because <see cref="WebScraperProvider"/> is internal sealed — NSubstitute cannot
/// proxy the generic logger with an internal type argument from a strong-named assembly).
/// </summary>
public sealed class WebScraperProviderTests
{
    private readonly IWebScraperClient _client = Substitute.For<IWebScraperClient>();
    private readonly WebScraperProvider _sut;

    public WebScraperProviderTests()
    {
        _sut = new WebScraperProvider(_client, NullLogger<WebScraperProvider>.Instance);
    }

    // ── Context helpers ───────────────────────────────────────────────────────

    private static TraceContext CreateContext(
        string? website = "https://example.com",
        TraceDepth depth = TraceDepth.Standard,
        CompanyProfile? existingProfile = null) =>
        new()
        {
            Request = new TraceRequest(
                companyName: "Test Corp",
                phone: null, email: null, website: website, address: null,
                city: null, country: "CZ",
                registrationId: null, taxId: null, industryHint: null,
                depth: depth,
                callbackUrl: null,
                source: "test"),
            ExistingProfile = existingProfile,
        };

    private static WebScrapingResult FullScrapingResult(string sourceUrl = "https://example.com") =>
        new()
        {
            SourceUrl = sourceUrl,
            CompanyName = "Acme Corp s.r.o.",
            Phone = "+420 222 333 444",
            Email = "info@acme.cz",
            Website = "https://www.acme.cz",
            Description = "Leading supplier of anvils",
            Industry = "Manufacturing",
            Address = new ScrapedAddress
            {
                Street = "Průmyslová 1",
                City = "Praha",
                PostalCode = "190 00",
                Country = "CZ",
                Region = null,
            },
        };

    // ── CanHandle ─────────────────────────────────────────────────────────────

    [Fact]
    public void CanHandle_WebsiteInRequest_Standard_ReturnsTrue()
    {
        _sut.CanHandle(CreateContext(website: "https://example.com", depth: TraceDepth.Standard))
            .Should().BeTrue();
    }

    [Fact]
    public void CanHandle_WebsiteInRequest_Deep_ReturnsTrue()
    {
        _sut.CanHandle(CreateContext(website: "https://example.com", depth: TraceDepth.Deep))
            .Should().BeTrue();
    }

    [Fact]
    public void CanHandle_QuickDepth_ReturnsFalse()
    {
        _sut.CanHandle(CreateContext(website: "https://example.com", depth: TraceDepth.Quick))
            .Should().BeFalse("Quick traces skip web scraping to stay within the 5s latency target");
    }

    [Fact]
    public void CanHandle_NoWebsite_NoExistingProfile_ReturnsFalse()
    {
        _sut.CanHandle(CreateContext(website: null, existingProfile: null))
            .Should().BeFalse();
    }

    [Fact]
    public void CanHandle_NoWebsiteInRequest_ExistingProfileHasWebsite_ReturnsTrue()
    {
        // Arrange: profile was enriched in a prior trace and has a Website field
        var profile = new CompanyProfile("CZ:12345678", "CZ", "12345678");
        profile.UpdateField(FieldName.Website,
            new TracedField<string>
            {
                Value = "https://old-website.cz",
                Confidence = Confidence.Create(0.8),
                Source = "google-maps",
                EnrichedAt = DateTimeOffset.UtcNow,
            }, "google-maps");

        _sut.CanHandle(CreateContext(website: null, existingProfile: profile))
            .Should().BeTrue();
    }

    [Fact]
    public void CanHandle_NullContext_Throws()
    {
        var act = () => _sut.CanHandle(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── Provider metadata ──────────────────────────────────────────────────────

    [Fact]
    public void Properties_AreCorrect()
    {
        _sut.ProviderId.Should().Be("web-scraper");
        _sut.Priority.Should().Be(150);
        _sut.SourceQuality.Should().Be(0.50);
    }

    // ── EnrichAsync — happy path ──────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_FullResult_MapsAllExpectedFields()
    {
        _client.ScrapeAsync("https://example.com", Arg.Any<CancellationToken>())
            .Returns(FullScrapingResult());

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Found.Should().BeTrue();
        result.Fields.Should().ContainKey(FieldName.LegalName);
        result.Fields[FieldName.LegalName].Should().Be("Acme Corp s.r.o.");
        result.Fields.Should().ContainKey(FieldName.Phone);
        result.Fields[FieldName.Phone].Should().Be("+420 222 333 444");
        result.Fields.Should().ContainKey(FieldName.Email);
        result.Fields[FieldName.Email].Should().Be("info@acme.cz");
        result.Fields.Should().ContainKey(FieldName.Website);
        result.Fields[FieldName.Website].Should().Be("https://www.acme.cz");
        result.Fields.Should().ContainKey(FieldName.Industry);
        result.Fields[FieldName.Industry].Should().Be("Manufacturing");
        result.Fields.Should().ContainKey(FieldName.OperatingAddress);
        result.Fields[FieldName.OperatingAddress].Should().BeOfType<Address>();
    }

    [Fact]
    public async Task EnrichAsync_OperatingAddress_MappedCorrectly()
    {
        _client.ScrapeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(FullScrapingResult());

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        var addr = result.Fields[FieldName.OperatingAddress].Should().BeOfType<Address>().Subject;
        addr.Street.Should().Be("Průmyslová 1");
        addr.City.Should().Be("Praha");
        addr.PostalCode.Should().Be("190 00");
        addr.Country.Should().Be("CZ");
    }

    [Fact]
    public async Task EnrichAsync_CanonicalWebsiteFromJsonLd_UsedOverSourceUrl()
    {
        // JSON-LD may report a canonical URL different from the scraped URL
        var scraped = new WebScrapingResult
        {
            SourceUrl = "http://acme.cz",        // redirected/scraped URL
            Website = "https://www.acme.cz",     // canonical from JSON-LD
            Phone = "+420 111 222 333",
        };
        _client.ScrapeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(scraped);

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Fields[FieldName.Website].Should().Be("https://www.acme.cz",
            "canonical JSON-LD URL takes priority over the raw SourceUrl");
    }

    [Fact]
    public async Task EnrichAsync_NoCanonicalWebsite_WebsiteFieldOmitted()
    {
        // SourceUrl is already known to the caller — we don't echo it back as a field.
        // Website is only set when JSON-LD / OG provides a different canonical URL.
        var scraped = new WebScrapingResult
        {
            SourceUrl = "https://example.com",
            Website = null,  // JSON-LD didn't provide a canonical URL
            Phone = "+420 111 222 333",
        };
        _client.ScrapeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(scraped);

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Fields.Should().NotContainKey(FieldName.Website,
            "SourceUrl is the URL we scraped — storing it back as a field adds no new information");
        result.Fields.Should().ContainKey(FieldName.Phone);
    }

    [Fact]
    public async Task EnrichAsync_PrefersRequestWebsite_OverExistingProfileWebsite()
    {
        // If both request.Website and ExistingProfile.Website are set, request wins
        var profile = new CompanyProfile("CZ:11111111", "CZ", "11111111");
        profile.UpdateField(FieldName.Website,
            new TracedField<string>
            {
                Value = "https://old.cz",
                Confidence = Confidence.Create(0.8),
                Source = "google-maps",
                EnrichedAt = DateTimeOffset.UtcNow,
            }, "google-maps");

        var ctx = CreateContext(website: "https://new.cz", existingProfile: profile);
        _client.ScrapeAsync("https://new.cz", Arg.Any<CancellationToken>())
            .Returns(new WebScrapingResult { SourceUrl = "https://new.cz", Phone = "+420 111 222 333" });

        await _sut.EnrichAsync(ctx, CancellationToken.None);

        // ScrapeAsync must have been called with the request URL, not the profile URL
        await _client.Received(1).ScrapeAsync("https://new.cz", Arg.Any<CancellationToken>());
        await _client.DidNotReceive().ScrapeAsync("https://old.cz", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnrichAsync_NoRequestWebsite_UsesExistingProfileWebsite()
    {
        var profile = new CompanyProfile("CZ:22222222", "CZ", "22222222");
        profile.UpdateField(FieldName.Website,
            new TracedField<string>
            {
                Value = "https://profile.cz",
                Confidence = Confidence.Create(0.8),
                Source = "google-maps",
                EnrichedAt = DateTimeOffset.UtcNow,
            }, "google-maps");

        var ctx = CreateContext(website: null, existingProfile: profile);
        _client.ScrapeAsync("https://profile.cz", Arg.Any<CancellationToken>())
            .Returns(new WebScrapingResult { SourceUrl = "https://profile.cz", Phone = "+420 999 888 777" });

        var result = await _sut.EnrichAsync(ctx, CancellationToken.None);

        result.Found.Should().BeTrue();
        await _client.Received(1).ScrapeAsync("https://profile.cz", Arg.Any<CancellationToken>());
    }

    // ── EnrichAsync — address edge cases ──────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_AddressOnlyHasPostalCode_OmitsAddress()
    {
        // An address without street or city is not actionable
        var scraped = new WebScrapingResult
        {
            SourceUrl = "https://example.com",
            Phone = "+420 111 222 333",
            Address = new ScrapedAddress { PostalCode = "190 00" },
        };
        _client.ScrapeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(scraped);

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Fields.Should().NotContainKey(FieldName.OperatingAddress,
            "a PostalCode-only address has no actionable location data");
    }

    [Fact]
    public async Task EnrichAsync_AddressHasCityOnly_IncludesAddress()
    {
        var scraped = new WebScrapingResult
        {
            SourceUrl = "https://example.com",
            Phone = "+420 111 222 333",
            Address = new ScrapedAddress { City = "Praha" },
        };
        _client.ScrapeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(scraped);

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Fields.Should().ContainKey(FieldName.OperatingAddress);
        var addr = (Address)result.Fields[FieldName.OperatingAddress]!;
        addr.City.Should().Be("Praha");
    }

    // ── EnrichAsync — not found / null cases ──────────────────────────────────

    [Fact]
    public async Task EnrichAsync_ScraperReturnsNull_ReturnsNotFound()
    {
        _client.ScrapeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((WebScrapingResult?)null);

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Found.Should().BeFalse();
        result.Status.Should().Be(SourceStatus.NotFound);
    }

    [Fact]
    public async Task EnrichAsync_ScraperReturnsResultWithNoUsableFields_ReturnsNotFound()
    {
        // A page with only SourceUrl set — no extractable data
        _client.ScrapeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new WebScrapingResult { SourceUrl = "https://example.com" });

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Found.Should().BeFalse();
        result.Status.Should().Be(SourceStatus.NotFound);
    }

    [Fact]
    public async Task EnrichAsync_NoWebsiteUrl_ReturnsNotFound()
    {
        // CanHandle guards this, but EnrichAsync is defensive too
        var ctx = CreateContext(website: null, existingProfile: null);

        var result = await _sut.EnrichAsync(ctx, CancellationToken.None);

        result.Found.Should().BeFalse();
        await _client.DidNotReceive().ScrapeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── EnrichAsync — error cases ─────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_HttpRequestException_ReturnsError()
    {
        _client.ScrapeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Status.Should().Be(SourceStatus.Error);
        result.ErrorMessage.Should().Be("Web scraping request failed");
        result.Found.Should().BeFalse();
    }

    [Fact]
    public async Task EnrichAsync_PollyTimeout_ReturnsTimeout()
    {
        // Polly timeout fires an OperationCanceledException with its own token (not ct).
        // Use a separate CTS that is never passed as ct to EnrichAsync — simulates Polly's token.
        using var pollyTokenSource = new CancellationTokenSource();
        _client.ScrapeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(pollyTokenSource.Token));

        // Pass CancellationToken.None — not cancelled, so this must be treated as Polly timeout
        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Status.Should().Be(SourceStatus.Timeout);
    }

    [Fact]
    public async Task EnrichAsync_CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _client.ScrapeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var act = () => _sut.EnrichAsync(CreateContext(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task EnrichAsync_NullContext_Throws()
    {
        var act = () => _sut.EnrichAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
