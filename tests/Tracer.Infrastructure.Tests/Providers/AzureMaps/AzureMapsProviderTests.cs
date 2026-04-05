using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;
using Tracer.Infrastructure.Providers.AzureMaps;

namespace Tracer.Infrastructure.Tests.Providers.AzureMaps;

public sealed class AzureMapsProviderTests
{
    private readonly IAzureMapsClient _client = Substitute.For<IAzureMapsClient>();
    private readonly ILogger<AzureMapsProvider> _logger = Substitute.For<ILogger<AzureMapsProvider>>();

    private AzureMapsProvider CreateSut() => new(_client, _logger);

    private static TraceContext CreateContext(
        string? address = "tř. Václava Klementa",
        string? city = "Mladá Boleslav",
        string? country = "CZ",
        bool hasLocation = false) =>
        new()
        {
            Request = new TraceRequest(
                companyName: "Test",
                phone: null, email: null, website: null,
                address: address, city: city, country: country,
                registrationId: null, taxId: null, industryHint: null,
                depth: TraceDepth.Standard,
                callbackUrl: null,
                source: "test"),
            AccumulatedFields = hasLocation
                ? ImmutableHashSet.Create(FieldName.Location)
                : ImmutableHashSet<FieldName>.Empty,
        };

    // ── CanHandle ───────────────────────────────────────────────────

    [Fact]
    public void CanHandle_WithAddress_ReturnsTrue()
    {
        CreateSut().CanHandle(CreateContext()).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_AlreadyHasLocation_ReturnsFalse()
    {
        CreateSut().CanHandle(CreateContext(hasLocation: true)).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_NoAddressNoCity_ReturnsFalse()
    {
        CreateSut().CanHandle(CreateContext(address: null, city: null)).Should().BeFalse();
    }

    // ── EnrichAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_FoundResult_ReturnsLocationAndAddress()
    {
        var sut = CreateSut();
        _client.GeocodeAsync(Arg.Any<string>(), "CZ", Arg.Any<CancellationToken>())
            .Returns(CreateBoleslaveFeature());

        var result = await sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Found.Should().BeTrue();
        result.Status.Should().Be(SourceStatus.Success);
        result.Fields.Should().ContainKey(FieldName.Location);
        result.Fields[FieldName.Location].Should().BeOfType<GeoCoordinate>();
        var geo = (GeoCoordinate)result.Fields[FieldName.Location]!;
        geo.Latitude.Should().BeApproximately(50.41, 0.01);
        geo.Longitude.Should().BeApproximately(14.91, 0.01);
        result.Fields.Should().ContainKey(FieldName.OperatingAddress);
        result.Fields[FieldName.OperatingAddress].Should().BeOfType<Address>();
    }

    [Fact]
    public async Task EnrichAsync_NoMatch_ReturnsNotFound()
    {
        var sut = CreateSut();
        _client.GeocodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((GeocodeFeature?)null);

        var result = await sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Found.Should().BeFalse();
        result.Status.Should().Be(SourceStatus.NotFound);
    }

    [Fact]
    public async Task EnrichAsync_HttpError_ReturnsError()
    {
        var sut = CreateSut();
        _client.GeocodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Quota exceeded"));

        var result = await sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Status.Should().Be(SourceStatus.Error);
        result.ErrorMessage.Should().Be("Azure Maps API call failed");
    }

    [Fact]
    public async Task EnrichAsync_Timeout_ReturnsTimeout()
    {
        var sut = CreateSut();
        _client.GeocodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var result = await sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Status.Should().Be(SourceStatus.Timeout);
    }

    [Fact]
    public void Properties_AreCorrect()
    {
        var sut = CreateSut();
        sut.ProviderId.Should().Be("azure-maps");
        sut.Priority.Should().Be(50);
        sut.SourceQuality.Should().Be(0.75);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static GeocodeFeature CreateBoleslaveFeature() => new()
    {
        Type = "Feature",
        Geometry = new GeocodeGeometry
        {
            Type = "Point",
            Coordinates = [14.9069, 50.4117], // GeoJSON: [lng, lat]
        },
        Properties = new GeocodeProperties
        {
            Confidence = "High",
            MatchCodes = ["Good"],
            Address = new GeocodeAddress
            {
                AddressLine = "tř. Václava Klementa 869",
                Locality = "Mladá Boleslav",
                PostalCode = "293 01",
                CountryRegion = new GeocodeCountryRegion { Name = "Czech Republic", Iso = "CZ" },
                AdminDistricts = [new GeocodeAdminDistrict { Name = "Středočeský kraj" }],
                FormattedAddress = "tř. Václava Klementa 869, 293 01 Mladá Boleslav, Czechia",
            },
        },
    };
}
