using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;
using Tracer.Infrastructure.Providers.GleifLei;

namespace Tracer.Infrastructure.Tests.Providers.GleifLei;

public sealed class GleifProviderTests
{
    private readonly IGleifClient _client = Substitute.For<IGleifClient>();
    private readonly ILogger<GleifProvider> _logger = Substitute.For<ILogger<GleifProvider>>();

    private GleifProvider CreateSut() => new(_client, _logger);

    private static TraceContext CreateContext(
        string? companyName = "Skoda Auto",
        string? country = "CZ",
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

    // ── CanHandle ───────────────────────────────────────────────────

    [Fact]
    public void CanHandle_WithCompanyName_ReturnsTrue()
    {
        CreateSut().CanHandle(CreateContext()).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_WithRegistrationIdOnly_ReturnsFalse()
    {
        // GLEIF requires company name for search — registrationId alone is not sufficient
        CreateSut().CanHandle(CreateContext(companyName: null, registrationId: "12345678")).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_NoNameNoId_ReturnsFalse()
    {
        CreateSut().CanHandle(CreateContext(companyName: null, registrationId: null)).Should().BeFalse();
    }

    // ── EnrichAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_NameMatch_ReturnsFields()
    {
        var sut = CreateSut();
        _client.SearchByNameAsync("Skoda Auto", "CZ", Arg.Any<CancellationToken>())
            .Returns(new[] { CreateSkodaRecord() });
        _client.GetDirectParentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GleifRelationshipNode { Id = "PARENT_LEI", Name = "Volkswagen AG" });

        var result = await sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Found.Should().BeTrue();
        result.Status.Should().Be(SourceStatus.Success);
        result.Fields.Should().ContainKey(FieldName.LegalName);
        result.Fields[FieldName.LegalName].Should().Be("SKODA AUTO a.s.");
        result.Fields.Should().ContainKey(FieldName.RegisteredAddress);
        result.Fields[FieldName.RegisteredAddress].Should().BeOfType<Address>();
        result.Fields.Should().ContainKey(FieldName.ParentCompany);
        result.Fields[FieldName.ParentCompany].Should().Be("Volkswagen AG");
        result.Fields.Should().ContainKey(FieldName.EntityStatus);
        result.RawResponseJson.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task EnrichAsync_NoMatch_ReturnsNotFound()
    {
        var sut = CreateSut();
        _client.SearchByNameAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<GleifLeiRecord>());

        var result = await sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Found.Should().BeFalse();
        result.Status.Should().Be(SourceStatus.NotFound);
    }

    [Fact]
    public async Task EnrichAsync_HttpError_ReturnsError()
    {
        var sut = CreateSut();
        _client.SearchByNameAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Service down"));

        var result = await sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Found.Should().BeFalse();
        result.Status.Should().Be(SourceStatus.Error);
        result.ErrorMessage.Should().Be("GLEIF API call failed");
    }

    [Fact]
    public async Task EnrichAsync_Timeout_ReturnsTimeout()
    {
        var sut = CreateSut();
        _client.SearchByNameAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var result = await sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Found.Should().BeFalse();
        result.Status.Should().Be(SourceStatus.Timeout);
    }

    [Fact]
    public async Task EnrichAsync_InactiveEntity_MapsToDissolvedStatus()
    {
        var sut = CreateSut();
        var record = new GleifLeiRecord
        {
            Id = "TEST",
            Attributes = new GleifAttributes
            {
                Lei = "TEST",
                Entity = new GleifEntity
                {
                    LegalName = new GleifLocalizedName { Name = "Inactive Corp" },
                    Status = "INACTIVE",
                },
            },
        };
        _client.SearchByNameAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new[] { record });
        _client.GetDirectParentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((GleifRelationshipNode?)null);

        var result = await sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Fields[FieldName.EntityStatus].Should().Be("dissolved");
    }

    [Fact]
    public void Properties_AreCorrect()
    {
        var sut = CreateSut();
        sut.ProviderId.Should().Be("gleif-lei");
        sut.Priority.Should().Be(30);
        sut.SourceQuality.Should().Be(0.85);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static GleifLeiRecord CreateSkodaRecord() => new()
    {
        Id = "529900I8XOZDM4ZQSQ21",
        Attributes = new GleifAttributes
        {
            Lei = "529900I8XOZDM4ZQSQ21",
            Entity = new GleifEntity
            {
                LegalName = new GleifLocalizedName { Name = "SKODA AUTO a.s.", Language = "cs" },
                RegisteredAs = "00177041",
                Jurisdiction = "CZ",
                Status = "ACTIVE",
                LegalForm = new GleifLegalForm { Id = "8888", Other = "akciová společnost" },
                LegalAddress = new GleifAddress
                {
                    AddressLines = ["tř. Václava Klementa 869"],
                    City = "Mladá Boleslav",
                    PostalCode = "29301",
                    Country = "CZ",
                    Region = "CZ-ST",
                },
                HeadquartersAddress = new GleifAddress
                {
                    AddressLines = ["tř. Václava Klementa 869"],
                    City = "Mladá Boleslav",
                    PostalCode = "29301",
                    Country = "CZ",
                },
            },
            Registration = new GleifRegistration
            {
                ManagingLou = "5299000J2N45DDNE4Y28",
                CorroborationLevel = "FULLY_CORROBORATED",
                Status = "ISSUED",
            },
        },
    };
}
