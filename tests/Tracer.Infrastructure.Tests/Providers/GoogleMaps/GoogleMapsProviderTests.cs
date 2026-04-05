using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;
using Tracer.Infrastructure.Providers.GoogleMaps;

namespace Tracer.Infrastructure.Tests.Providers.GoogleMaps;

public sealed class GoogleMapsProviderTests
{
    private readonly IGoogleMapsClient _client = Substitute.For<IGoogleMapsClient>();
    private readonly ILogger<GoogleMapsProvider> _logger = Substitute.For<ILogger<GoogleMapsProvider>>();

    private GoogleMapsProvider CreateSut() => new(_client, _logger);

    private static TraceContext CreateContext(
        string? companyName = "Škoda Auto",
        string? address = null,
        string? city = "Mladá Boleslav",
        string? country = "CZ") =>
        new()
        {
            Request = new TraceRequest(
                companyName: companyName,
                phone: null, email: null, website: null,
                address: address, city: city, country: country,
                registrationId: null, taxId: null, industryHint: null,
                depth: TraceDepth.Standard,
                callbackUrl: null,
                source: "test"),
        };

    // ── CanHandle ───────────────────────────────────────────────────

    [Fact]
    public void CanHandle_WithCompanyName_ReturnsTrue()
    {
        CreateSut().CanHandle(CreateContext()).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_WithAddressOnly_ReturnsTrue()
    {
        CreateSut().CanHandle(CreateContext(companyName: null, address: "Hlavní 1")).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_NoNameNoAddress_ReturnsFalse()
    {
        CreateSut().CanHandle(CreateContext(companyName: null, address: null)).Should().BeFalse();
    }

    // ── EnrichAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_FoundPlace_ReturnsEnrichedFields()
    {
        var sut = CreateSut();
        _client.SearchTextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new[] { CreateSkodaPlace() });

        var result = await sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Found.Should().BeTrue();
        result.Status.Should().Be(SourceStatus.Success);
        result.Fields.Should().ContainKey(FieldName.OperatingAddress);
        result.Fields[FieldName.OperatingAddress].Should().BeOfType<Address>();
        result.Fields.Should().ContainKey(FieldName.Phone);
        result.Fields.Should().ContainKey(FieldName.Website);
        result.Fields.Should().ContainKey(FieldName.Location);
        result.Fields[FieldName.Location].Should().BeOfType<GeoCoordinate>();
        result.RawResponseJson.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task EnrichAsync_NoMatch_ReturnsNotFound()
    {
        var sut = CreateSut();
        _client.SearchTextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<PlaceResult>());

        var result = await sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Found.Should().BeFalse();
        result.Status.Should().Be(SourceStatus.NotFound);
    }

    [Fact]
    public async Task EnrichAsync_HttpError_ReturnsError()
    {
        var sut = CreateSut();
        _client.SearchTextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Quota exceeded"));

        var result = await sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Found.Should().BeFalse();
        result.Status.Should().Be(SourceStatus.Error);
        result.ErrorMessage.Should().Be("Google Maps API call failed");
    }

    [Fact]
    public async Task EnrichAsync_Timeout_ReturnsTimeout()
    {
        var sut = CreateSut();
        _client.SearchTextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var result = await sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Status.Should().Be(SourceStatus.Timeout);
    }

    [Fact]
    public async Task EnrichAsync_BuildsQueryFromContextFields()
    {
        var sut = CreateSut();
        _client.SearchTextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<PlaceResult>());

        await sut.EnrichAsync(
            CreateContext(companyName: "Acme", city: "Praha", country: "CZ"),
            CancellationToken.None);

        await _client.Received(1).SearchTextAsync(
            Arg.Is<string>(q => q.Contains("Acme") && q.Contains("Praha") && q.Contains("CZ")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Properties_AreCorrect()
    {
        var sut = CreateSut();
        sut.ProviderId.Should().Be("google-maps");
        sut.Priority.Should().Be(50);
        sut.SourceQuality.Should().Be(0.70);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static PlaceResult CreateSkodaPlace() => new()
    {
        Id = "ChIJN1t_tDeuEmsRUsoyG83frY4",
        DisplayName = new PlaceLocalizedText { Text = "ŠKODA AUTO a.s.", LanguageCode = "cs" },
        FormattedAddress = "tř. Václava Klementa 869, 293 01 Mladá Boleslav, Czechia",
        InternationalPhoneNumber = "+420 326 811 111",
        WebsiteUri = "https://www.skoda-auto.cz",
        PrimaryType = "car_manufacturer",
        BusinessStatus = "OPERATIONAL",
        Location = new PlaceLocation { Latitude = 50.4117, Longitude = 14.9069 },
        AddressComponents =
        [
            new() { LongText = "tř. Václava Klementa", ShortText = "tř. Václava Klementa", Types = ["route"] },
            new() { LongText = "869", ShortText = "869", Types = ["street_number"] },
            new() { LongText = "Mladá Boleslav", ShortText = "Mladá Boleslav", Types = ["locality"] },
            new() { LongText = "293 01", ShortText = "293 01", Types = ["postal_code"] },
            new() { LongText = "Czechia", ShortText = "CZ", Types = ["country"] },
            new() { LongText = "Středočeský kraj", ShortText = "Středočeský kraj", Types = ["administrative_area_level_1"] },
        ],
    };
}
