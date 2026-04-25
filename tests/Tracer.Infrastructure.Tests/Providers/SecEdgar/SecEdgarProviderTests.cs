using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;
using Tracer.Infrastructure.Providers.SecEdgar;

namespace Tracer.Infrastructure.Tests.Providers.SecEdgar;

public sealed class SecEdgarProviderTests
{
    private readonly ISecEdgarClient _client = Substitute.For<ISecEdgarClient>();
    private readonly ILogger<SecEdgarProvider> _logger = Substitute.For<ILogger<SecEdgarProvider>>();

    private SecEdgarProvider CreateSut() => new(_client, _logger);

    private static TraceContext CreateContext(
        string? companyName = "Tesla Inc",
        string? country = "US") =>
        new()
        {
            Request = new TraceRequest(
                companyName: companyName,
                phone: null, email: null, website: null, address: null,
                city: null, country: country,
                registrationId: null, taxId: null, industryHint: null,
                depth: TraceDepth.Standard,
                callbackUrl: null,
                source: "test"),
        };

    [Fact]
    public void CanHandle_UsCountryWithName_ReturnsTrue()
    {
        CreateSut().CanHandle(CreateContext()).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_NonUs_ReturnsFalse()
    {
        CreateSut().CanHandle(CreateContext(country: "GB")).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_UsNoName_ReturnsFalse()
    {
        CreateSut().CanHandle(CreateContext(companyName: null)).Should().BeFalse();
    }

    [Fact]
    public async Task EnrichAsync_FoundCompany_ReturnsFields()
    {
        var sut = CreateSut();
        _client.SearchByNameAsync("Tesla Inc", Arg.Any<CancellationToken>())
            .Returns(new[] { new EdgarSearchSource { EntityName = "Tesla, Inc.", EntityId = "1318605" } });
        _client.GetSubmissionsAsync("1318605", Arg.Any<CancellationToken>())
            .Returns(CreateTeslaSubmissions());

        var result = await sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Found.Should().BeTrue();
        result.Fields.Should().ContainKey(FieldName.LegalName);
        result.Fields[FieldName.LegalName].Should().Be("Tesla, Inc.");
        result.Fields.Should().ContainKey(FieldName.Industry);
        result.Fields.Should().ContainKey(FieldName.RegisteredAddress);
        result.Fields[FieldName.RegisteredAddress].Should().BeOfType<Address>();
        result.Fields.Should().ContainKey(FieldName.TaxId);
    }

    [Fact]
    public async Task EnrichAsync_NoSearchResult_ReturnsNotFound()
    {
        var sut = CreateSut();
        _client.SearchByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<EdgarSearchSource>());

        var result = await sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Found.Should().BeFalse();
        result.Status.Should().Be(SourceStatus.NotFound);
    }

    [Fact]
    public async Task EnrichAsync_HttpError_ReturnsError()
    {
        var sut = CreateSut();
        _client.SearchByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Rate limited"));

        var result = await sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Status.Should().Be(SourceStatus.Error);
        result.ErrorMessage.Should().Be("SEC EDGAR API call failed");
    }

    [Fact]
    public void Properties_AreCorrect()
    {
        var sut = CreateSut();
        sut.ProviderId.Should().Be("sec-edgar");
        sut.Priority.Should().Be(20);
        sut.SourceQuality.Should().Be(0.90);
    }

    private static EdgarSubmissions CreateTeslaSubmissions() => new()
    {
        Cik = "1318605",
        Name = "Tesla, Inc.",
        Sic = "3711",
        SicDescription = "Motor Vehicles & Passenger Car Bodies",
        EntityType = "operating",
        StateOfIncorporation = "DE",
        Ein = "912197729",
        Tickers = ["TSLA"],
        Exchanges = ["Nasdaq"],
        Addresses = new EdgarAddresses
        {
            Business = new EdgarAddress
            {
                Street1 = "1 Tesla Road",
                City = "Austin",
                StateOrCountry = "TX",
                ZipCode = "78725",
            },
        },
    };
}
