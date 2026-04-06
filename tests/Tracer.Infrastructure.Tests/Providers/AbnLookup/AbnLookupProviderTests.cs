using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;
using Tracer.Infrastructure.Providers.AbnLookup;

namespace Tracer.Infrastructure.Tests.Providers.AbnLookup;

public sealed class AbnLookupProviderTests
{
    private readonly IAbnLookupClient _client = Substitute.For<IAbnLookupClient>();
    private readonly ILogger<AbnLookupProvider> _logger = Substitute.For<ILogger<AbnLookupProvider>>();

    private AbnLookupProvider CreateSut() => new(_client, _logger);

    private static TraceContext CreateContext(
        string? companyName = "BHP Group",
        string? country = "AU",
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

    [Fact]
    public void CanHandle_AuCountry_ReturnsTrue()
    {
        CreateSut().CanHandle(CreateContext()).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_AbnFormat_ReturnsTrue()
    {
        CreateSut().CanHandle(CreateContext(country: "XX", registrationId: "49004028077")).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_AbnWithSpaces_ReturnsTrue()
    {
        CreateSut().CanHandle(CreateContext(country: "XX", registrationId: "49 004 028 077")).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_NonAuNoAbn_ReturnsFalse()
    {
        CreateSut().CanHandle(CreateContext(country: "GB")).Should().BeFalse();
    }

    [Fact]
    public async Task EnrichAsync_ByAbn_ReturnsFields()
    {
        var sut = CreateSut();
        _client.GetByAbnAsync("49004028077", Arg.Any<CancellationToken>())
            .Returns(CreateBhpDetails());

        var result = await sut.EnrichAsync(
            CreateContext(registrationId: "49004028077"), CancellationToken.None);

        result.Found.Should().BeTrue();
        result.Fields.Should().ContainKey(FieldName.LegalName);
        result.Fields[FieldName.LegalName].Should().Be("BHP GROUP LIMITED");
        result.Fields.Should().ContainKey(FieldName.EntityStatus);
        result.Fields[FieldName.EntityStatus].Should().Be("active");
        result.Fields.Should().ContainKey(FieldName.RegisteredAddress);
    }

    [Fact]
    public async Task EnrichAsync_ByNameSearch_ResolvesAbn()
    {
        var sut = CreateSut();
        _client.SearchByNameAsync("BHP Group", Arg.Any<CancellationToken>())
            .Returns(new[] { new AbnSearchResult { Abn = "49004028077", Name = "BHP GROUP LIMITED" } });
        _client.GetByAbnAsync("49004028077", Arg.Any<CancellationToken>())
            .Returns(CreateBhpDetails());

        var result = await sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Found.Should().BeTrue();
        result.Fields.Should().ContainKey(FieldName.LegalName);
    }

    [Fact]
    public async Task EnrichAsync_NotFound_ReturnsNotFound()
    {
        var sut = CreateSut();
        _client.GetByAbnAsync("99999999999", Arg.Any<CancellationToken>())
            .Returns((AbnDetailsResponse?)null);

        var result = await sut.EnrichAsync(
            CreateContext(registrationId: "99999999999"), CancellationToken.None);

        result.Found.Should().BeFalse();
        result.Status.Should().Be(SourceStatus.NotFound);
    }

    [Fact]
    public async Task EnrichAsync_HttpError_ReturnsError()
    {
        var sut = CreateSut();
        _client.SearchByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Service unavailable"));

        var result = await sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Status.Should().Be(SourceStatus.Error);
        result.ErrorMessage.Should().Be("ABN Lookup API call failed");
    }

    [Fact]
    public void Properties_AreCorrect()
    {
        var sut = CreateSut();
        sut.ProviderId.Should().Be("abn-lookup");
        sut.Priority.Should().Be(10);
        sut.SourceQuality.Should().Be(0.90);
    }

    private static AbnDetailsResponse CreateBhpDetails() => new()
    {
        Abn = "49004028077",
        AbnStatus = "Active",
        EntityName = "BHP GROUP LIMITED",
        EntityTypeCode = "PUB",
        EntityTypeName = "Australian Public Company",
        Gst = "2000-07-01",
        AddressState = "VIC",
        AddressPostcode = "3000",
    };
}
