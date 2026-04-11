using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;
using Tracer.Infrastructure.Providers.Handelsregister;

namespace Tracer.Infrastructure.Tests.Providers.Handelsregister;

/// <summary>
/// Unit tests for <see cref="HandelsregisterProvider"/>.
/// Uses NSubstitute for <see cref="IHandelsregisterClient"/> and NullLogger
/// (required because the provider is internal sealed — NSubstitute cannot proxy the generic
/// logger with an internal type argument from a strong-named assembly).
/// </summary>
public sealed class HandelsregisterProviderTests
{
    private readonly IHandelsregisterClient _client = Substitute.For<IHandelsregisterClient>();
    private readonly HandelsregisterProvider _sut;

    public HandelsregisterProviderTests()
    {
        _sut = new HandelsregisterProvider(_client, NullLogger<HandelsregisterProvider>.Instance);
    }

    // ── Context helpers ──────────────────────────────────────────────────────

    private static TraceContext CreateContext(
        string? country = "DE",
        string? companyName = "Siemens AG",
        string? registrationId = null,
        TraceDepth depth = TraceDepth.Standard) =>
        new()
        {
            Request = new Domain.Entities.TraceRequest(
                companyName: companyName,
                phone: null, email: null, website: null, address: null,
                city: null, country: country,
                registrationId: registrationId, taxId: null, industryHint: null,
                depth: depth,
                callbackUrl: null,
                source: "test"),
        };

    private static HandelsregisterCompanyDetail SiemensDetail() =>
        new()
        {
            CompanyName = "Siemens Aktiengesellschaft",
            RegistrationId = "HRB 6324",
            RegisterCourt = "Amtsgericht München",
            LegalForm = "Aktiengesellschaft",
            Status = "aktiv",
            Street = "Werner-von-Siemens-Straße 1",
            PostalCode = "80333",
            City = "München",
            Officers = ["Roland Busch", "Cedrik Neike"],
        };

    private static IReadOnlyList<HandelsregisterSearchResult> SiemensSearchResults() =>
    [
        new()
        {
            CompanyName = "Siemens Aktiengesellschaft",
            RegisterType = "HRB",
            RegisterNumber = "6324",
            RegisterCourt = "Amtsgericht München",
            Status = "aktiv",
        },
    ];

    // ── Provider metadata ────────────────────────────────────────────────────

    [Fact]
    public void Properties_AreCorrect()
    {
        _sut.ProviderId.Should().Be("handelsregister");
        _sut.Priority.Should().Be(200);
        _sut.SourceQuality.Should().Be(0.85);
    }

    // ── CanHandle ────────────────────────────────────────────────────────────

    [Fact]
    public void CanHandle_GermanyWithName_Standard_ReturnsTrue()
    {
        _sut.CanHandle(CreateContext(country: "DE", companyName: "Test GmbH", depth: TraceDepth.Standard))
            .Should().BeTrue();
    }

    [Fact]
    public void CanHandle_GermanyWithName_Deep_ReturnsTrue()
    {
        _sut.CanHandle(CreateContext(country: "DE", companyName: "Test GmbH", depth: TraceDepth.Deep))
            .Should().BeTrue();
    }

    [Fact]
    public void CanHandle_QuickDepth_ReturnsFalse()
    {
        _sut.CanHandle(CreateContext(country: "DE", companyName: "Test GmbH", depth: TraceDepth.Quick))
            .Should().BeFalse("Quick traces skip registry scraping to stay within the 5s latency target");
    }

    [Fact]
    public void CanHandle_NonGermanCountry_ReturnsFalse()
    {
        _sut.CanHandle(CreateContext(country: "CZ", companyName: "Test s.r.o."))
            .Should().BeFalse();
    }

    [Fact]
    public void CanHandle_NoCountry_WithHrbRegistrationId_ReturnsTrue()
    {
        _sut.CanHandle(CreateContext(country: null, companyName: null, registrationId: "HRB 6324"))
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("HRB 12345")]
    [InlineData("HRA 1")]
    [InlineData("HRB 1234567")]
    [InlineData("GnR 456")]
    [InlineData("VR 78901")]
    public void CanHandle_GermanRegisterIdFormats_ReturnsTrue(string registrationId)
    {
        _sut.CanHandle(CreateContext(country: "US", companyName: null, registrationId: registrationId))
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("12345678")]
    [InlineData("CRN 1234")]
    public void CanHandle_NonGermanRegisterIdFormats_WithNonDeCountry_ReturnsFalse(string registrationId)
    {
        // These registration IDs don't match HRB/HRA pattern, and country is not DE
        _sut.CanHandle(CreateContext(country: "US", companyName: "Some Corp", registrationId: registrationId))
            .Should().BeFalse();
    }

    [Fact]
    public void CanHandle_NullContext_Throws()
    {
        var act = () => _sut.CanHandle(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── EnrichAsync — happy path (detail lookup) ─────────────────────────────

    [Fact]
    public async Task EnrichAsync_ByRegistrationId_ReturnsAllFields()
    {
        _client.GetByRegisterNumberAsync("HRB", "6324", null, Arg.Any<CancellationToken>())
            .Returns(SiemensDetail());

        var result = await _sut.EnrichAsync(
            CreateContext(registrationId: "HRB 6324"), CancellationToken.None);

        result.Found.Should().BeTrue();
        result.Status.Should().Be(SourceStatus.Success);
        result.Fields.Should().ContainKey(FieldName.LegalName);
        result.Fields[FieldName.LegalName].Should().Be("Siemens Aktiengesellschaft");
        result.Fields.Should().ContainKey(FieldName.RegistrationId);
        result.Fields[FieldName.RegistrationId].Should().Be("HRB 6324");
        result.Fields.Should().ContainKey(FieldName.LegalForm);
        result.Fields[FieldName.LegalForm].Should().Be("Aktiengesellschaft");
        result.Fields.Should().ContainKey(FieldName.EntityStatus);
        result.Fields[FieldName.EntityStatus].Should().Be("active");
        result.Fields.Should().ContainKey(FieldName.RegisteredAddress);
    }

    [Fact]
    public async Task EnrichAsync_RegisteredAddress_MappedCorrectly()
    {
        _client.GetByRegisterNumberAsync("HRB", "6324", null, Arg.Any<CancellationToken>())
            .Returns(SiemensDetail());

        var result = await _sut.EnrichAsync(
            CreateContext(registrationId: "HRB 6324"), CancellationToken.None);

        var addr = result.Fields[FieldName.RegisteredAddress].Should().BeOfType<Address>().Subject;
        addr.Street.Should().Be("Werner-von-Siemens-Straße 1");
        addr.City.Should().Be("München");
        addr.PostalCode.Should().Be("80333");
        addr.Country.Should().Be("DE");
    }

    [Fact]
    public async Task EnrichAsync_StatusNormalization_GermanToEnglish()
    {
        var detail = SiemensDetail() with { Status = "gelöscht" };
        _client.GetByRegisterNumberAsync("HRB", "6324", null, Arg.Any<CancellationToken>())
            .Returns(detail);

        var result = await _sut.EnrichAsync(
            CreateContext(registrationId: "HRB 6324"), CancellationToken.None);

        result.Fields[FieldName.EntityStatus].Should().Be("dissolved");
    }

    // ── EnrichAsync — happy path (name search fallback) ──────────────────────

    [Fact]
    public async Task EnrichAsync_ByNameOnly_SearchesAndMapsFields()
    {
        _client.SearchByNameAsync("Siemens AG", Arg.Any<CancellationToken>())
            .Returns(SiemensSearchResults());

        var result = await _sut.EnrichAsync(
            CreateContext(companyName: "Siemens AG"), CancellationToken.None);

        result.Found.Should().BeTrue();
        result.Fields.Should().ContainKey(FieldName.LegalName);
        result.Fields[FieldName.LegalName].Should().Be("Siemens Aktiengesellschaft");
        result.Fields.Should().ContainKey(FieldName.RegistrationId);
        result.Fields[FieldName.RegistrationId].Should().Be("HRB 6324");
    }

    // ── EnrichAsync — not found ──────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_DetailNotFound_FallsBackToNameSearch()
    {
        // Register number lookup returns null, name search succeeds
        _client.GetByRegisterNumberAsync("HRB", "99999", null, Arg.Any<CancellationToken>())
            .Returns((HandelsregisterCompanyDetail?)null);
        _client.SearchByNameAsync("Test GmbH", Arg.Any<CancellationToken>())
            .Returns(SiemensSearchResults());

        var result = await _sut.EnrichAsync(
            CreateContext(companyName: "Test GmbH", registrationId: "HRB 99999"), CancellationToken.None);

        result.Found.Should().BeTrue("name search fallback should find a result");
    }

    [Fact]
    public async Task EnrichAsync_NothingFound_ReturnsNotFound()
    {
        _client.SearchByNameAsync("NONEXISTENT GmbH", Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<HandelsregisterSearchResult>?)null);

        var result = await _sut.EnrichAsync(
            CreateContext(companyName: "NONEXISTENT GmbH"), CancellationToken.None);

        result.Found.Should().BeFalse();
        result.Status.Should().Be(SourceStatus.NotFound);
    }

    // ── EnrichAsync — error cases ────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_HttpRequestException_ReturnsError()
    {
        _client.SearchByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await _sut.EnrichAsync(
            CreateContext(companyName: "Test GmbH"), CancellationToken.None);

        result.Status.Should().Be(SourceStatus.Error);
        result.ErrorMessage.Should().Be("Handelsregister search failed");
        result.Found.Should().BeFalse();
    }

    [Fact]
    public async Task EnrichAsync_PollyTimeout_ReturnsTimeout()
    {
        using var pollyTokenSource = new CancellationTokenSource();
        _client.SearchByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(pollyTokenSource.Token));

        var result = await _sut.EnrichAsync(
            CreateContext(companyName: "Test GmbH"), CancellationToken.None);

        result.Status.Should().Be(SourceStatus.Timeout);
    }

    [Fact]
    public async Task EnrichAsync_CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _client.SearchByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var act = () => _sut.EnrichAsync(CreateContext(companyName: "Test GmbH"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task EnrichAsync_NullContext_Throws()
    {
        var act = () => _sut.EnrichAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ── EnrichAsync — address edge cases ─────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_NoAddress_OmitsAddressField()
    {
        var detail = SiemensDetail() with { Street = null, PostalCode = null, City = null };
        _client.GetByRegisterNumberAsync("HRB", "6324", null, Arg.Any<CancellationToken>())
            .Returns(detail);

        var result = await _sut.EnrichAsync(
            CreateContext(registrationId: "HRB 6324"), CancellationToken.None);

        result.Found.Should().BeTrue();
        result.Fields.Should().NotContainKey(FieldName.RegisteredAddress);
    }

    [Fact]
    public async Task EnrichAsync_CityOnly_IncludesAddress()
    {
        var detail = SiemensDetail() with { Street = null, PostalCode = null, City = "München" };
        _client.GetByRegisterNumberAsync("HRB", "6324", null, Arg.Any<CancellationToken>())
            .Returns(detail);

        var result = await _sut.EnrichAsync(
            CreateContext(registrationId: "HRB 6324"), CancellationToken.None);

        result.Fields.Should().ContainKey(FieldName.RegisteredAddress);
        var addr = (Address)result.Fields[FieldName.RegisteredAddress]!;
        addr.City.Should().Be("München");
    }
}
