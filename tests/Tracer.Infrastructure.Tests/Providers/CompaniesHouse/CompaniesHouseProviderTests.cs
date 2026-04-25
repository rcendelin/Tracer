using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;
using Tracer.Infrastructure.Providers.CompaniesHouse;

namespace Tracer.Infrastructure.Tests.Providers.CompaniesHouse;

public sealed class CompaniesHouseProviderTests
{
    private readonly ICompaniesHouseClient _client = Substitute.For<ICompaniesHouseClient>();
    private readonly ILogger<CompaniesHouseProvider> _logger = Substitute.For<ILogger<CompaniesHouseProvider>>();

    private CompaniesHouseProvider CreateSut() => new(_client, _logger);

    private static TraceContext CreateContext(
        string? companyName = "Tesco PLC",
        string? country = "GB",
        string? registrationId = null) =>
        new()
        {
            Request = new TraceRequest(
                companyName: companyName,
                phone: null, email: null, website: null, address: null,
                city: null, country: country,
                registrationId: registrationId,
                taxId: null, industryHint: null,
                depth: TraceDepth.Standard,
                callbackUrl: null,
                source: "test"),
        };

    [Theory]
    [InlineData("GB")]
    [InlineData("IE")]
    public void CanHandle_UkIeCountry_ReturnsTrue(string country)
    {
        CreateSut().CanHandle(CreateContext(country: country)).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_CrnFormat_ReturnsTrue()
    {
        CreateSut().CanHandle(CreateContext(country: "XX", registrationId: "00445790")).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_NonUkNoCrn_ReturnsFalse()
    {
        CreateSut().CanHandle(CreateContext(country: "DE")).Should().BeFalse();
    }

    [Fact]
    public async Task EnrichAsync_ByNameSearch_ReturnsFields()
    {
        var sut = CreateSut();
        _client.SearchByNameAsync("Tesco PLC", Arg.Any<CancellationToken>())
            .Returns(new[] { new CompanySearchItem { CompanyNumber = "00445790", Title = "TESCO PLC" } });
        _client.GetCompanyAsync("00445790", Arg.Any<CancellationToken>())
            .Returns(CreateTescoProfile());

        var result = await sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Found.Should().BeTrue();
        result.Fields.Should().ContainKey(FieldName.LegalName);
        result.Fields[FieldName.LegalName].Should().Be("TESCO PLC");
        result.Fields.Should().ContainKey(FieldName.RegisteredAddress);
        result.Fields[FieldName.RegisteredAddress].Should().BeOfType<Address>();
    }

    [Fact]
    public async Task EnrichAsync_ByCrn_ReturnsFields()
    {
        var sut = CreateSut();
        _client.GetCompanyAsync("00445790", Arg.Any<CancellationToken>())
            .Returns(CreateTescoProfile());

        var result = await sut.EnrichAsync(
            CreateContext(registrationId: "00445790"), CancellationToken.None);

        result.Found.Should().BeTrue();
        result.Fields.Should().ContainKey(FieldName.EntityStatus);
        result.Fields[FieldName.EntityStatus].Should().Be("active");
    }

    [Fact]
    public async Task EnrichAsync_NotFound_ReturnsNotFound()
    {
        var sut = CreateSut();
        _client.GetCompanyAsync("99999999", Arg.Any<CancellationToken>())
            .Returns((CompaniesHouseCompanyProfile?)null);

        var result = await sut.EnrichAsync(
            CreateContext(registrationId: "99999999"), CancellationToken.None);

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
        result.ErrorMessage.Should().Be("Companies House API call failed");
    }

    [Fact]
    public void Properties_AreCorrect()
    {
        var sut = CreateSut();
        sut.ProviderId.Should().Be("companies-house");
        sut.Priority.Should().Be(10);
        sut.SourceQuality.Should().Be(0.95);
    }

    private static CompaniesHouseCompanyProfile CreateTescoProfile() => new CompaniesHouseCompanyProfile
    {
        CompanyName = "TESCO PLC",
        CompanyNumber = "00445790",
        CompanyStatus = "active",
        Type = "plc",
        SicCodes = ["47110"],
        DateOfCreation = "1947-11-27",
        RegisteredOfficeAddress = new CompanyAddress
        {
            AddressLine1 = "Tesco House",
            AddressLine2 = "Shire Park, Kestrel Way",
            Locality = "Welwyn Garden City",
            Region = "Hertfordshire",
            PostalCode = "AL7 1GA",
            Country = "United Kingdom",
        },
    };
}
