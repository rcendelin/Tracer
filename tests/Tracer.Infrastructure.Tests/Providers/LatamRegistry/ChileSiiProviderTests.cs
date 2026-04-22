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

public sealed class ChileSiiProviderTests
{
    private readonly ILatamRegistryClient _client = Substitute.For<ILatamRegistryClient>();
    private readonly ChileSiiProvider _sut;

    public ChileSiiProviderTests()
    {
        _sut = new ChileSiiProvider(_client, NullLogger<ChileSiiProvider>.Instance);
    }

    private static TraceContext CreateContext(
        string? country = "CL",
        string? registrationId = "96.790.240-3",
        TraceDepth depth = TraceDepth.Standard) =>
        new()
        {
            Request = new Domain.Entities.TraceRequest(
                companyName: "Some SpA", phone: null, email: null, website: null,
                address: null, city: null, country: country,
                registrationId: registrationId, taxId: null, industryHint: null,
                depth: depth, callbackUrl: null, source: "test"),
            AccumulatedFields = ImmutableHashSet<FieldName>.Empty,
        };

    private static LatamRegistrySearchResult CopecResult() => new()
    {
        EntityName = "COMPAÑIA DE PETROLEOS DE CHILE COPEC S.A.",
        RegistrationId = "99520000-7",
        CountryCode = "CL",
        Status = "Contribuyente Vigente",
        EntityType = "Venta de combustibles",
    };

    [Fact]
    public void Properties_AreCorrect()
    {
        _sut.ProviderId.Should().Be("latam-sii");
        _sut.Priority.Should().Be(200);
        _sut.SourceQuality.Should().Be(0.80);
    }

    [Fact]
    public void CanHandle_CountryCl_ReturnsTrue()
    {
        _sut.CanHandle(CreateContext()).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_Quick_ReturnsFalse()
    {
        _sut.CanHandle(CreateContext(depth: TraceDepth.Quick)).Should().BeFalse();
    }

    [Theory]
    [InlineData("96.790.240-3")]
    [InlineData("96790240-3")]
    [InlineData("1234567-K")]
    public void CanHandle_OtherCountryWithRutFormat_ReturnsTrue(string rut)
    {
        _sut.CanHandle(CreateContext(country: "US", registrationId: rut))
            .Should().BeTrue();
    }

    [Fact]
    public void CanHandle_OtherCountryNoId_ReturnsFalse()
    {
        _sut.CanHandle(CreateContext(country: "BR", registrationId: null))
            .Should().BeFalse();
    }

    [Fact]
    public async Task EnrichAsync_Found_MapsFields()
    {
        _client.LookupAsync("CL", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CopecResult());

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Status.Should().Be(SourceStatus.Success);
        result.Fields[FieldName.LegalName].Should()
            .Be("COMPAÑIA DE PETROLEOS DE CHILE COPEC S.A.");
        result.Fields[FieldName.RegistrationId].Should().Be("CL:99520000-7");
        result.Fields[FieldName.EntityStatus].Should().Be("active");
    }

    [Theory]
    [InlineData("Contribuyente Vigente", "active")]
    [InlineData("Activo", "active")]
    [InlineData("activa", "active")]
    [InlineData("Término de Giro", "dissolved")]
    [InlineData("Disuelta", "dissolved")]
    [InlineData("Suspendido", "suspended")]
    [InlineData("desconocido", "desconocido")]
    public void NormalizeStatus_SpanishChileanToCanonical(string input, string expected)
    {
        ChileSiiAdapter.NormalizeStatus(input).Should().Be(expected);
    }

    [Fact]
    public async Task EnrichAsync_NotFound_ReturnsNotFound()
    {
        _client.LookupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((LatamRegistrySearchResult?)null);

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);
        result.Status.Should().Be(SourceStatus.NotFound);
    }

    [Fact]
    public async Task EnrichAsync_HttpError_ReturnsSanitizedError()
    {
        _client.LookupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("DB credentials: secret=xxx"));

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);
        result.Status.Should().Be(SourceStatus.Error);
        result.ErrorMessage.Should().Be("SII Situación Tributaria lookup failed");
        result.ErrorMessage.Should().NotContain("secret");
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

    [Fact]
    public async Task EnrichAsync_CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _client.LookupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var act = () => _sut.EnrichAsync(CreateContext(), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
