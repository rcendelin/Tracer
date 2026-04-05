using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;
using Tracer.Infrastructure.Providers.Ares;

namespace Tracer.Infrastructure.Tests.Providers.Ares;

public sealed class AresProviderTests
{
    private readonly IAresClient _client = Substitute.For<IAresClient>();
    private readonly ILogger<AresProvider> _logger = Substitute.For<ILogger<AresProvider>>();

    private AresProvider CreateSut() => new(_client, _logger);

    private static TraceContext CreateContext(
        string? country = "CZ",
        string? registrationId = null,
        string? companyName = null) =>
        new()
        {
            Request = new TraceRequest(
                companyName: companyName,
                phone: null, email: null, website: null, address: null,
                city: null,
                country: country,
                registrationId: registrationId,
                taxId: null, industryHint: null,
                depth: TraceDepth.Standard,
                callbackUrl: null,
                source: "test"),
        };

    // ── CanHandle ───────────────────────────────────────────────────

    [Theory]
    [InlineData("CZ")]
    [InlineData("SK")]
    public void CanHandle_CzSkCountry_ReturnsTrue(string country)
    {
        var sut = CreateSut();
        sut.CanHandle(CreateContext(country: country, companyName: "Test")).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_IcoLikeRegistrationId_ReturnsTrue()
    {
        var sut = CreateSut();
        sut.CanHandle(CreateContext(country: "DE", registrationId: "12345678")).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_NonCzWithoutIco_ReturnsFalse()
    {
        var sut = CreateSut();
        sut.CanHandle(CreateContext(country: "GB", companyName: "Test")).Should().BeFalse();
    }

    // ── EnrichAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_WithIco_ReturnsEnrichedFields()
    {
        var sut = CreateSut();
        _client.GetByIcoAsync("00027006", Arg.Any<CancellationToken>())
            .Returns(CreateSkodaSubject());

        var result = await sut.EnrichAsync(
            CreateContext(registrationId: "00027006"), CancellationToken.None);

        result.Found.Should().BeTrue();
        result.Status.Should().Be(SourceStatus.Success);
        result.Fields.Should().ContainKey(FieldName.LegalName);
        result.Fields[FieldName.LegalName].Should().Be("ŠKODA AUTO a.s.");
        result.Fields.Should().ContainKey(FieldName.TaxId);
        result.Fields.Should().ContainKey(FieldName.RegisteredAddress);
        result.Fields[FieldName.RegisteredAddress].Should().BeOfType<Address>();
        result.RawResponseJson.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task EnrichAsync_WithNameSearch_ResolvesIcoThenFetches()
    {
        var sut = CreateSut();
        _client.SearchByNameAsync("Škoda Auto", Arg.Any<CancellationToken>())
            .Returns("00027006");
        _client.GetByIcoAsync("00027006", Arg.Any<CancellationToken>())
            .Returns(CreateSkodaSubject());

        var result = await sut.EnrichAsync(
            CreateContext(companyName: "Škoda Auto"), CancellationToken.None);

        result.Found.Should().BeTrue();
        result.Fields.Should().ContainKey(FieldName.LegalName);
    }

    [Fact]
    public async Task EnrichAsync_IcoNotFound_ReturnsNotFound()
    {
        var sut = CreateSut();
        _client.GetByIcoAsync("99999999", Arg.Any<CancellationToken>())
            .Returns((AresEkonomickySubjekt?)null);

        var result = await sut.EnrichAsync(
            CreateContext(registrationId: "99999999"), CancellationToken.None);

        result.Found.Should().BeFalse();
        result.Status.Should().Be(SourceStatus.NotFound);
    }

    [Fact]
    public async Task EnrichAsync_NameSearchFails_ReturnsNotFound()
    {
        var sut = CreateSut();
        _client.SearchByNameAsync("NonExistent", Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var result = await sut.EnrichAsync(
            CreateContext(companyName: "NonExistent"), CancellationToken.None);

        result.Found.Should().BeFalse();
        result.Status.Should().Be(SourceStatus.NotFound);
    }

    [Fact]
    public async Task EnrichAsync_HttpError_ReturnsError()
    {
        var sut = CreateSut();
        _client.GetByIcoAsync("00027006", Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("ARES unavailable"));

        var result = await sut.EnrichAsync(
            CreateContext(registrationId: "00027006"), CancellationToken.None);

        result.Found.Should().BeFalse();
        result.Status.Should().Be(SourceStatus.Error);
        result.ErrorMessage.Should().Contain("ARES API call failed");
    }

    [Fact]
    public async Task EnrichAsync_Cancelled_ReturnsTimeout()
    {
        var sut = CreateSut();
        _client.GetByIcoAsync("00027006", Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var result = await sut.EnrichAsync(
            CreateContext(registrationId: "00027006"), CancellationToken.None);

        result.Found.Should().BeFalse();
        result.Status.Should().Be(SourceStatus.Timeout);
    }

    [Fact]
    public async Task EnrichAsync_DissolvedCompany_SetsEntityStatusDissolved()
    {
        var sut = CreateSut();
        var subject = new AresEkonomickySubjekt
        {
            Ico = "00027006",
            ObchodniJmeno = "Zaniklá firma",
            DatumZaniku = "2024-01-01",
            DatumVzniku = "2000-01-01",
        };
        _client.GetByIcoAsync("00027006", Arg.Any<CancellationToken>())
            .Returns(subject);

        var result = await sut.EnrichAsync(
            CreateContext(registrationId: "00027006"), CancellationToken.None);

        result.Fields[FieldName.EntityStatus].Should().Be("dissolved");
    }

    [Fact]
    public void Properties_AreCorrect()
    {
        var sut = CreateSut();
        sut.ProviderId.Should().Be("ares");
        sut.Priority.Should().Be(10);
        sut.SourceQuality.Should().Be(0.95);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static AresEkonomickySubjekt CreateSkodaSubject() => new()
    {
        Ico = "00027006",
        ObchodniJmeno = "ŠKODA AUTO a.s.",
        Dic = "CZ00027006",
        PravniForma = "121",
        DatumVzniku = "1991-04-16",
        CzNace = ["29110"],
        Sidlo = new AresSidlo
        {
            NazevUlice = "tř. Václava Klementa",
            CisloDomovni = 869,
            NazevObce = "Mladá Boleslav",
            Psc = 29301,
            KodStatu = "CZ",
            NazevKraje = "Středočeský kraj",
            TextovaAdresa = "tř. Václava Klementa 869, 293 01 Mladá Boleslav",
        },
    };
}
