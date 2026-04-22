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

public sealed class ColombiaRuesProviderTests
{
    private readonly ILatamRegistryClient _client = Substitute.For<ILatamRegistryClient>();
    private readonly ColombiaRuesProvider _sut;

    public ColombiaRuesProviderTests()
    {
        _sut = new ColombiaRuesProvider(_client, NullLogger<ColombiaRuesProvider>.Instance);
    }

    private static TraceContext CreateContext(
        string? country = "CO",
        string? registrationId = "890903938-8",
        TraceDepth depth = TraceDepth.Standard) =>
        new()
        {
            Request = new Domain.Entities.TraceRequest(
                companyName: "Test SA", phone: null, email: null, website: null,
                address: null, city: null, country: country,
                registrationId: registrationId, taxId: null, industryHint: null,
                depth: depth, callbackUrl: null, source: "test"),
            AccumulatedFields = ImmutableHashSet<FieldName>.Empty,
        };

    private static LatamRegistrySearchResult Result() => new()
    {
        EntityName = "BANCOLOMBIA S.A.",
        RegistrationId = "890903938",
        CountryCode = "CO",
        Status = "ACTIVA",
        EntityType = "SOCIEDAD ANONIMA",
        Address = "Carrera 48 No. 26-85, Medellín",
    };

    [Fact]
    public void Properties_AreCorrect()
    {
        _sut.ProviderId.Should().Be("latam-rues");
        _sut.Priority.Should().Be(200);
        _sut.SourceQuality.Should().Be(0.80);
    }

    [Fact]
    public void CanHandle_CountryCo_ReturnsTrue()
    {
        _sut.CanHandle(CreateContext()).Should().BeTrue();
    }

    [Theory]
    [InlineData("890903938")]
    [InlineData("890903938-8")]
    [InlineData("12345678")]
    public void CanHandle_OtherCountryWithNitFormat_ReturnsTrue(string nit)
    {
        _sut.CanHandle(CreateContext(country: "BR", registrationId: nit)).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_TooShortIdentifier_ReturnsFalse()
    {
        _sut.CanHandle(CreateContext(country: "BR", registrationId: "1234567"))
            .Should().BeFalse();
    }

    [Fact]
    public async Task EnrichAsync_Found_MapsFields()
    {
        _client.LookupAsync("CO", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result());

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Status.Should().Be(SourceStatus.Success);
        result.Fields[FieldName.LegalName].Should().Be("BANCOLOMBIA S.A.");
        result.Fields[FieldName.RegistrationId].Should().Be("CO:890903938");
        result.Fields[FieldName.EntityStatus].Should().Be("active");
        result.Fields[FieldName.LegalForm].Should().Be("SOCIEDAD ANONIMA");
    }

    [Theory]
    [InlineData("ACTIVA", "active")]
    [InlineData("Activa", "active")]
    [InlineData("INACTIVA", "inactive")]
    [InlineData("CANCELADA", "dissolved")]
    [InlineData("LIQUIDADA", "dissolved")]
    [InlineData("SUSPENDIDA", "suspended")]
    [InlineData("EN LIQUIDACION", "in_liquidation")]
    [InlineData("EN LIQUIDACIÓN", "in_liquidation")]
    public void NormalizeStatus_ColombianToCanonical(string input, string expected)
    {
        ColombiaRuesAdapter.NormalizeStatus(input).Should().Be(expected);
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
            .ThrowsAsync(new HttpRequestException("path=/secret/db"));

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);
        result.Status.Should().Be(SourceStatus.Error);
        result.ErrorMessage.Should().Be("RUES consulta failed");
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
}
