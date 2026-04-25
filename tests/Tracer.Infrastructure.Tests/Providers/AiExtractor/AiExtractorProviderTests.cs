using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;
using Tracer.Infrastructure.Providers.AiExtractor;
using Tracer.Infrastructure.Providers.WebScraper;

namespace Tracer.Infrastructure.Tests.Providers.AiExtractor;

/// <summary>
/// Unit tests for <see cref="AiExtractorProvider"/>.
/// Both <see cref="IAiExtractorClient"/> and <see cref="IWebScraperClient"/> are mocked via
/// NSubstitute. <c>NullLogger</c> is used because the provider is <c>internal sealed</c>
/// and NSubstitute cannot proxy generic ILogger with internal type arguments from strong-named assemblies.
/// </summary>
public sealed class AiExtractorProviderTests
{
    private readonly IAiExtractorClient _aiClient = Substitute.For<IAiExtractorClient>();
    private readonly IWebScraperClient _scraperClient = Substitute.For<IWebScraperClient>();
    private readonly AiExtractorProvider _sut;

    public AiExtractorProviderTests()
    {
        _sut = new AiExtractorProvider(
            _aiClient,
            _scraperClient,
            NullLogger<AiExtractorProvider>.Instance);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TraceContext CreateContext(
        string? website = "https://example.com",
        TraceDepth depth = TraceDepth.Deep,
        string? companyName = "Acme Corp",
        string? country = "CZ",
        CompanyProfile? existingProfile = null) =>
        new()
        {
            Request = new TraceRequest(
                companyName: companyName,
                phone: null, email: null, website: website, address: null,
                city: null, country: country,
                registrationId: null, taxId: null, industryHint: null,
                depth: depth,
                callbackUrl: null,
                source: "test"),
            ExistingProfile = existingProfile,
        };

    private static WebScrapingResult MakeScrapedResult(
        string? description = "Leading manufacturer of industrial parts.") =>
        new()
        {
            SourceUrl = "https://example.com",
            CompanyName = "Acme s.r.o.",
            Phone = "+420 123 456",
            Email = "info@acme.cz",
            Description = description,
            Industry = "Industrial",
        };

    private static AiExtractedData FullAiResult() => new()
    {
        LegalName = "Acme s.r.o.",
        Phone = "+420 123 456",
        Email = "info@acme.cz",
        Industry = "Manufacturing",
        EmployeeRange = "51-200",
        Address = new AiExtractedAddress
        {
            Street = "Průmyslová 1",
            City = "Brno",
            PostalCode = "602 00",
            Country = "CZ",
            Region = "South Moravian",
        },
    };

    // ── Provider metadata ─────────────────────────────────────────────────────

    [Fact]
    public void Properties_AreCorrect()
    {
        _sut.ProviderId.Should().Be("ai-extractor");
        _sut.Priority.Should().Be(250);
        _sut.SourceQuality.Should().Be(0.40);
    }

    // ── CanHandle ─────────────────────────────────────────────────────────────

    [Fact]
    public void CanHandle_DeepWithWebsite_ReturnsTrue()
    {
        _sut.CanHandle(CreateContext(depth: TraceDepth.Deep, website: "https://example.com"))
            .Should().BeTrue();
    }

    [Fact]
    public void CanHandle_StandardDepth_ReturnsFalse()
    {
        _sut.CanHandle(CreateContext(depth: TraceDepth.Standard, website: "https://example.com"))
            .Should().BeFalse("AI extraction only runs on Deep traces");
    }

    [Fact]
    public void CanHandle_QuickDepth_ReturnsFalse()
    {
        _sut.CanHandle(CreateContext(depth: TraceDepth.Quick, website: "https://example.com"))
            .Should().BeFalse();
    }

    [Fact]
    public void CanHandle_DeepNoWebsiteNoProfile_ReturnsFalse()
    {
        _sut.CanHandle(CreateContext(depth: TraceDepth.Deep, website: null, existingProfile: null))
            .Should().BeFalse("no website means no content to feed to the AI");
    }

    [Fact]
    public void CanHandle_DeepNoRequestWebsite_ExistingProfileHasWebsite_ReturnsTrue()
    {
        var profile = new CompanyProfile("CZ:12345678", "CZ", "12345678");
        profile.UpdateField(FieldName.Website,
            new TracedField<string>
            {
                Value = "https://acme.cz",
                Confidence = Confidence.Create(0.8),
                Source = "google-maps",
                EnrichedAt = DateTimeOffset.UtcNow,
            }, "google-maps");

        _sut.CanHandle(CreateContext(website: null, existingProfile: profile, depth: TraceDepth.Deep))
            .Should().BeTrue();
    }

    [Fact]
    public void CanHandle_NullContext_Throws()
    {
        var act = () => _sut.CanHandle(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── EnrichAsync — happy path ───────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_FullExtractionResult_MapsAllFields()
    {
        _scraperClient.ScrapeAsync("https://example.com", Arg.Any<CancellationToken>())
            .Returns(MakeScrapedResult());
        _aiClient.ExtractCompanyInfoAsync(Arg.Any<string>(), Arg.Any<TraceContext>(), Arg.Any<CancellationToken>())
            .Returns(FullAiResult());

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Found.Should().BeTrue();
        result.Status.Should().Be(SourceStatus.Success);
        result.Fields.Should().ContainKey(FieldName.LegalName);
        result.Fields[FieldName.LegalName].Should().Be("Acme s.r.o.");
        result.Fields.Should().ContainKey(FieldName.Phone);
        result.Fields.Should().ContainKey(FieldName.Email);
        result.Fields.Should().ContainKey(FieldName.Industry);
        result.Fields[FieldName.Industry].Should().Be("Manufacturing");
        result.Fields.Should().ContainKey(FieldName.EmployeeRange);
        result.Fields[FieldName.EmployeeRange].Should().Be("51-200");
        result.Fields.Should().ContainKey(FieldName.OperatingAddress);
    }

    [Fact]
    public async Task EnrichAsync_EmployeeRange_IsMappedFromAiResult()
    {
        // EmployeeRange is the key field the AI provider adds; scrapers typically can't extract it.
        _scraperClient.ScrapeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(MakeScrapedResult());
        _aiClient.ExtractCompanyInfoAsync(Arg.Any<string>(), Arg.Any<TraceContext>(), Arg.Any<CancellationToken>())
            .Returns(new AiExtractedData { EmployeeRange = "1000+" });

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Found.Should().BeTrue();
        result.Fields[FieldName.EmployeeRange].Should().Be("1000+");
    }

    [Fact]
    public async Task EnrichAsync_OperatingAddress_MappedWithAllParts()
    {
        _scraperClient.ScrapeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(MakeScrapedResult());
        _aiClient.ExtractCompanyInfoAsync(Arg.Any<string>(), Arg.Any<TraceContext>(), Arg.Any<CancellationToken>())
            .Returns(FullAiResult());

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        var addr = result.Fields[FieldName.OperatingAddress].Should().BeOfType<Address>().Subject;
        addr.Street.Should().Be("Průmyslová 1");
        addr.City.Should().Be("Brno");
        addr.PostalCode.Should().Be("602 00");
        addr.Country.Should().Be("CZ");
        addr.Region.Should().Be("South Moravian");
    }

    // ── EnrichAsync — text construction ───────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_TextPassedToAi_ContainsScrapedData()
    {
        _scraperClient.ScrapeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(MakeScrapedResult("Industrial parts manufacturer since 1990."));
        string? capturedText = null;
        _aiClient.ExtractCompanyInfoAsync(
            Arg.Do<string>(t => capturedText = t),
            Arg.Any<TraceContext>(),
            Arg.Any<CancellationToken>())
            .Returns(new AiExtractedData { EmployeeRange = "51-200" });

        await _sut.EnrichAsync(CreateContext(companyName: "Acme Corp", country: "CZ"), CancellationToken.None);

        capturedText.Should().Contain("Acme Corp");      // request hint
        capturedText.Should().Contain("CZ");             // country hint
        capturedText.Should().Contain("Acme s.r.o.");    // scraped name
        capturedText.Should().Contain("Industrial parts manufacturer since 1990."); // description
    }

    [Fact]
    public async Task EnrichAsync_PrefersRequestWebsite_OverProfileWebsite()
    {
        var profile = new CompanyProfile("CZ:12345678", "CZ", "12345678");
        profile.UpdateField(FieldName.Website,
            new TracedField<string>
            {
                Value = "https://old.cz",
                Confidence = Confidence.Create(0.8),
                Source = "google-maps",
                EnrichedAt = DateTimeOffset.UtcNow,
            }, "google-maps");

        _scraperClient.ScrapeAsync("https://new.cz", Arg.Any<CancellationToken>())
            .Returns(MakeScrapedResult());
        _aiClient.ExtractCompanyInfoAsync(Arg.Any<string>(), Arg.Any<TraceContext>(), Arg.Any<CancellationToken>())
            .Returns(new AiExtractedData { EmployeeRange = "11-50" });

        await _sut.EnrichAsync(
            CreateContext(website: "https://new.cz", existingProfile: profile),
            CancellationToken.None);

        await _scraperClient.Received(1).ScrapeAsync("https://new.cz", Arg.Any<CancellationToken>());
        await _scraperClient.DidNotReceive().ScrapeAsync("https://old.cz", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnrichAsync_NoRequestWebsite_UsesProfileWebsite()
    {
        var profile = new CompanyProfile("CZ:87654321", "CZ", "87654321");
        profile.UpdateField(FieldName.Website,
            new TracedField<string>
            {
                Value = "https://profile.cz",
                Confidence = Confidence.Create(0.8),
                Source = "google-maps",
                EnrichedAt = DateTimeOffset.UtcNow,
            }, "google-maps");

        _scraperClient.ScrapeAsync("https://profile.cz", Arg.Any<CancellationToken>())
            .Returns(MakeScrapedResult());
        _aiClient.ExtractCompanyInfoAsync(Arg.Any<string>(), Arg.Any<TraceContext>(), Arg.Any<CancellationToken>())
            .Returns(new AiExtractedData { Industry = "Technology" });

        var result = await _sut.EnrichAsync(
            CreateContext(website: null, existingProfile: profile),
            CancellationToken.None);

        result.Found.Should().BeTrue();
        await _scraperClient.Received(1).ScrapeAsync("https://profile.cz", Arg.Any<CancellationToken>());
    }

    // ── EnrichAsync — address edge cases ──────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_AiAddressOnlyPostalCode_OmitsAddress()
    {
        _scraperClient.ScrapeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(MakeScrapedResult());
        _aiClient.ExtractCompanyInfoAsync(Arg.Any<string>(), Arg.Any<TraceContext>(), Arg.Any<CancellationToken>())
            .Returns(new AiExtractedData
            {
                Industry = "Manufacturing",
                Address = new AiExtractedAddress { PostalCode = "602 00" }, // no street or city
            });

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Fields.Should().NotContainKey(FieldName.OperatingAddress,
            "address without street or city is not actionable");
    }

    // ── EnrichAsync — not found / null paths ──────────────────────────────────

    [Fact]
    public async Task EnrichAsync_ScraperReturnsNull_ReturnsNotFound()
    {
        _scraperClient.ScrapeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((WebScrapingResult?)null);

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Found.Should().BeFalse();
        result.Status.Should().Be(SourceStatus.NotFound);
        await _aiClient.DidNotReceive()
            .ExtractCompanyInfoAsync(Arg.Any<string>(), Arg.Any<TraceContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnrichAsync_EmptyScrapedContent_ReturnsNotFound()
    {
        // Scraped result with no description or other text fields → nothing to pass to AI
        _scraperClient.ScrapeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new WebScrapingResult { SourceUrl = "https://example.com" });

        var result = await _sut.EnrichAsync(
            CreateContext(companyName: null, country: null),
            CancellationToken.None);

        result.Found.Should().BeFalse();
        await _aiClient.DidNotReceive()
            .ExtractCompanyInfoAsync(Arg.Any<string>(), Arg.Any<TraceContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnrichAsync_AiReturnsNull_ReturnsNotFound()
    {
        _scraperClient.ScrapeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(MakeScrapedResult());
        _aiClient.ExtractCompanyInfoAsync(Arg.Any<string>(), Arg.Any<TraceContext>(), Arg.Any<CancellationToken>())
            .Returns((AiExtractedData?)null);

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Found.Should().BeFalse();
        result.Status.Should().Be(SourceStatus.NotFound);
    }

    [Fact]
    public async Task EnrichAsync_AiReturnsAllNullFields_ReturnsNotFound()
    {
        _scraperClient.ScrapeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(MakeScrapedResult());
        _aiClient.ExtractCompanyInfoAsync(Arg.Any<string>(), Arg.Any<TraceContext>(), Arg.Any<CancellationToken>())
            .Returns(new AiExtractedData()); // all fields null

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Found.Should().BeFalse();
        result.Status.Should().Be(SourceStatus.NotFound);
    }

    [Fact]
    public async Task EnrichAsync_NoWebsiteUrl_ReturnsNotFoundWithoutCallingClients()
    {
        var result = await _sut.EnrichAsync(
            CreateContext(website: null, existingProfile: null),
            CancellationToken.None);

        result.Found.Should().BeFalse();
        await _scraperClient.DidNotReceive().ScrapeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _aiClient.DidNotReceive()
            .ExtractCompanyInfoAsync(Arg.Any<string>(), Arg.Any<TraceContext>(), Arg.Any<CancellationToken>());
    }

    // ── EnrichAsync — error / timeout handling ────────────────────────────────

    [Fact]
    public async Task EnrichAsync_ScrapeHttpError_ReturnsError()
    {
        _scraperClient.ScrapeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Status.Should().Be(SourceStatus.Error);
        result.ErrorMessage.Should().Be("AI extractor: website fetch failed");
        result.Found.Should().BeFalse();
    }

    [Fact]
    public async Task EnrichAsync_ScrapePollyTimeout_ReturnsTimeout()
    {
        using var pollyTokenSource = new CancellationTokenSource();
        _scraperClient.ScrapeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(pollyTokenSource.Token));

        // Pass CancellationToken.None — not cancelled, so this must be treated as Polly timeout
        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Status.Should().Be(SourceStatus.Timeout);
    }

    [Fact]
    public async Task EnrichAsync_AiPollyTimeout_ReturnsTimeout()
    {
        using var pollyTokenSource = new CancellationTokenSource();
        _scraperClient.ScrapeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(MakeScrapedResult());
        _aiClient.ExtractCompanyInfoAsync(Arg.Any<string>(), Arg.Any<TraceContext>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(pollyTokenSource.Token));

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Status.Should().Be(SourceStatus.Timeout);
    }

    [Fact]
    public async Task EnrichAsync_AiHttpError_ReturnsError()
    {
        _scraperClient.ScrapeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(MakeScrapedResult());
        _aiClient.ExtractCompanyInfoAsync(Arg.Any<string>(), Arg.Any<TraceContext>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Azure endpoint unreachable"));

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Status.Should().Be(SourceStatus.Error);
        result.ErrorMessage.Should().Be("AI extractor: extraction failed");
        result.Found.Should().BeFalse();
    }

    [Fact]
    public async Task EnrichAsync_CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _scraperClient.ScrapeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        Func<Task> act = () => _sut.EnrichAsync(CreateContext(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task EnrichAsync_NullContext_Throws()
    {
        Func<Task> act = () => _sut.EnrichAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
