using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Infrastructure.Providers.LatamRegistry;
using Tracer.Infrastructure.Providers.LatamRegistry.Adapters;
using Tracer.Infrastructure.Providers.LatamRegistry.Providers;

namespace Tracer.Infrastructure.Tests.Providers.LatamRegistry;

public sealed class MexicoSatProviderTests
{
    private readonly ILatamRegistryClient _client = Substitute.For<ILatamRegistryClient>();
    private readonly MexicoSatProvider _sut;

    public MexicoSatProviderTests()
    {
        _sut = new MexicoSatProvider(_client, NullLogger<MexicoSatProvider>.Instance);
    }

    private static TraceContext CreateContext(
        string? country = "MX",
        string? registrationId = "WMT970714R10",
        TraceDepth depth = TraceDepth.Standard) =>
        new()
        {
            Request = new Domain.Entities.TraceRequest(
                companyName: "Walmart de Mexico", phone: null, email: null, website: null,
                address: null, city: null, country: country,
                registrationId: registrationId, taxId: null, industryHint: null,
                depth: depth, callbackUrl: null, source: "test"),
            AccumulatedFields = ImmutableHashSet<FieldName>.Empty,
        };

    private static LatamRegistrySearchResult Result() => new()
    {
        EntityName = "WAL-MART DE MEXICO SAB DE CV",
        RegistrationId = "WMT970714R10",
        CountryCode = "MX",
        Status = "ACTIVO",
        EntityType = "PERSONA MORAL",
    };

    [Fact]
    public void Properties_AreCorrect()
    {
        _sut.ProviderId.Should().Be("latam-sat");
        _sut.Priority.Should().Be(200);
        _sut.SourceQuality.Should().Be(0.80);
    }

    [Fact]
    public void CanHandle_CountryMx_ReturnsTrue()
    {
        _sut.CanHandle(CreateContext()).Should().BeTrue();
    }

    [Theory]
    [InlineData("WMT970714R10")]          // 12 chars — legal person
    [InlineData("JUAP850514H14")]         // 13 chars — individual (proxy)
    [InlineData("wmt970714r10")]          // lowercase — normalization upcases
    public void CanHandle_OtherCountryWithRfcFormat_ReturnsTrue(string rfc)
    {
        _sut.CanHandle(CreateContext(country: "AR", registrationId: rfc))
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("1234567")]           // too short
    [InlineData("INVALID!!")]         // bad chars
    public void CanHandle_WrongFormat_OtherCountry_ReturnsFalse(string notAnRfc)
    {
        _sut.CanHandle(CreateContext(country: "AR", registrationId: notAnRfc))
            .Should().BeFalse();
    }

    [Fact]
    public async Task EnrichAsync_Found_MapsFields()
    {
        _client.LookupAsync("MX", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result());

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Status.Should().Be(SourceStatus.Success);
        result.Fields[FieldName.LegalName].Should().Be("WAL-MART DE MEXICO SAB DE CV");
        result.Fields[FieldName.RegistrationId].Should().Be("MX:WMT970714R10");
        result.Fields[FieldName.EntityStatus].Should().Be("active");
        result.Fields[FieldName.LegalForm].Should().Be("PERSONA MORAL");
    }

    [Theory]
    [InlineData("ACTIVO", "active")]
    [InlineData("Activo", "active")]
    [InlineData("INACTIVO", "inactive")]
    [InlineData("SUSPENDIDO", "suspended")]
    [InlineData("CANCELADO", "dissolved")]
    [InlineData("SIN ESTATUS", "SIN ESTATUS")]
    public void NormalizeStatus_SatToCanonical(string input, string expected)
    {
        MexicoSatAdapter.NormalizeStatus(input).Should().Be(expected);
    }

    [Fact]
    public async Task EnrichAsync_CaptchaWall_ReturnsNotFound()
    {
        // Simulates SAT's CAPTCHA-protected response — adapter returns null,
        // provider must surface NotFound, not Error.
        _client.LookupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((LatamRegistrySearchResult?)null);

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);
        result.Status.Should().Be(SourceStatus.NotFound);
    }

    [Fact]
    public async Task EnrichAsync_HttpError_ReturnsSanitizedError()
    {
        _client.LookupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("internal path C:\\Users\\x"));

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);
        result.Status.Should().Be(SourceStatus.Error);
        result.ErrorMessage.Should().Be("SAT Constancia lookup failed");
        result.ErrorMessage.Should().NotContain("C:\\");
    }

    [Fact]
    public async Task EnrichAsync_PollyTimeout_ReturnsTimeout()
    {
        using var polly = new CancellationTokenSource();
        _client.LookupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(polly.Token));

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);
        result.Status.Should().Be(SourceStatus.Timeout);
    }
}
