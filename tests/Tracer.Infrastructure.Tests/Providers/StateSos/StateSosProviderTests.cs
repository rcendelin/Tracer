using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Infrastructure.Providers.StateSos;

namespace Tracer.Infrastructure.Tests.Providers.StateSos;

/// <summary>
/// Unit tests for <see cref="StateSosProvider"/>.
/// Uses NSubstitute for <see cref="IStateSosClient"/> and NullLogger.
/// </summary>
public sealed class StateSosProviderTests
{
    private readonly IStateSosClient _client = Substitute.For<IStateSosClient>();
    private readonly StateSosProvider _sut;

    public StateSosProviderTests()
    {
        _sut = new StateSosProvider(_client, NullLogger<StateSosProvider>.Instance);
    }

    // ── Context helpers ──────────────────────────────────────────────────────

    private static TraceContext CreateContext(
        string? country = "US",
        string? companyName = "Apple Inc",
        TraceDepth depth = TraceDepth.Standard,
        IReadOnlySet<FieldName>? accumulatedFields = null) =>
        new()
        {
            Request = new Domain.Entities.TraceRequest(
                companyName: companyName,
                phone: null, email: null, website: null, address: null,
                city: null, country: country,
                registrationId: null, taxId: null, industryHint: null,
                depth: depth,
                callbackUrl: null,
                source: "test"),
            AccumulatedFields = accumulatedFields ?? ImmutableHashSet<FieldName>.Empty,
        };

    private static IReadOnlyList<StateSosSearchResult> AppleSearchResults() =>
    [
        new()
        {
            EntityName = "APPLE INC.",
            FilingNumber = "C0806592",
            StateCode = "CA",
            Status = "Active",
            EntityType = "Corporation",
            FormationDate = "01/03/1977",
        },
    ];

    // ── Provider metadata ────────────────────────────────────────────────────

    [Fact]
    public void Properties_AreCorrect()
    {
        _sut.ProviderId.Should().Be("state-sos");
        _sut.Priority.Should().Be(200);
        _sut.SourceQuality.Should().Be(0.85);
    }

    // ── CanHandle ────────────────────────────────────────────────────────────

    [Fact]
    public void CanHandle_UsWithName_Standard_ReturnsTrue()
    {
        _sut.CanHandle(CreateContext(country: "US", companyName: "Apple Inc"))
            .Should().BeTrue();
    }

    [Fact]
    public void CanHandle_Deep_ReturnsTrue()
    {
        _sut.CanHandle(CreateContext(depth: TraceDepth.Deep))
            .Should().BeTrue();
    }

    [Fact]
    public void CanHandle_QuickDepth_ReturnsFalse()
    {
        _sut.CanHandle(CreateContext(depth: TraceDepth.Quick))
            .Should().BeFalse("Quick traces skip Tier 2 providers");
    }

    [Fact]
    public void CanHandle_NonUsCountry_ReturnsFalse()
    {
        _sut.CanHandle(CreateContext(country: "DE", companyName: "Test GmbH"))
            .Should().BeFalse();
    }

    [Fact]
    public void CanHandle_EmptyCompanyName_ReturnsFalse()
    {
        // State registries require name search — empty name is not searchable
        // Note: TraceRequest requires at least one identifying field, so we pass a phone
        // to satisfy the constructor but leave companyName empty.
        var ctx = new TraceContext
        {
            Request = new Domain.Entities.TraceRequest(
                companyName: null, phone: "+1234567890", email: null, website: null,
                address: null, city: null, country: "US",
                registrationId: null, taxId: null, industryHint: null,
                depth: TraceDepth.Standard, callbackUrl: null, source: "test"),
        };
        _sut.CanHandle(ctx).Should().BeFalse("state registries are name-search based");
    }

    [Fact]
    public void CanHandle_SecEdgarAlreadyEnrichedRegistrationId_ReturnsFalse()
    {
        // SEC EDGAR (Tier 1) already found a CIK — no need for state-level search
        var accumulated = ImmutableHashSet.Create(FieldName.RegistrationId);
        _sut.CanHandle(CreateContext(accumulatedFields: accumulated))
            .Should().BeFalse("SEC EDGAR already enriched RegistrationId");
    }

    [Fact]
    public void CanHandle_OtherFieldsAccumulated_ButNotRegistrationId_ReturnsTrue()
    {
        // Other fields accumulated but not RegistrationId — still run
        var accumulated = ImmutableHashSet.Create(FieldName.LegalName, FieldName.Industry);
        _sut.CanHandle(CreateContext(accumulatedFields: accumulated))
            .Should().BeTrue();
    }

    [Fact]
    public void CanHandle_NullContext_Throws()
    {
        var act = () => _sut.CanHandle(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── EnrichAsync — happy path ─────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_Found_MapsAllFields()
    {
        _client.SearchAsync("Apple Inc", null, Arg.Any<CancellationToken>())
            .Returns(AppleSearchResults());

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Found.Should().BeTrue();
        result.Status.Should().Be(SourceStatus.Success);
        result.Fields.Should().ContainKey(FieldName.LegalName);
        result.Fields[FieldName.LegalName].Should().Be("APPLE INC.");
        result.Fields.Should().ContainKey(FieldName.RegistrationId);
        result.Fields[FieldName.RegistrationId].Should().Be("CA:C0806592");
        result.Fields.Should().ContainKey(FieldName.EntityStatus);
        result.Fields[FieldName.EntityStatus].Should().Be("active");
        result.Fields.Should().ContainKey(FieldName.LegalForm);
        result.Fields[FieldName.LegalForm].Should().Be("Corporation");
    }

    // ── EnrichAsync — status normalization ────────────────────────────────────

    [Theory]
    [InlineData("Active", "active")]
    [InlineData("Good Standing", "active")]
    [InlineData("Dissolved", "dissolved")]
    [InlineData("Cancelled", "dissolved")]
    [InlineData("Revoked", "dissolved")]
    [InlineData("Suspended", "suspended")]
    [InlineData("Forfeited", "suspended")]
    [InlineData("Merged", "merged")]
    [InlineData("UNKNOWN_STATUS", "UNKNOWN_STATUS")]
    public void NormalizeStatus_UsTerminologyToCanonical(string input, string expected)
    {
        StateSosProvider.NormalizeStatus(input).Should().Be(expected);
    }

    // ── EnrichAsync — not found ──────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_NothingFound_ReturnsNotFound()
    {
        _client.SearchAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<StateSosSearchResult>?)null);

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Found.Should().BeFalse();
        result.Status.Should().Be(SourceStatus.NotFound);
    }

    // ── EnrichAsync — error cases ────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_HttpRequestException_ReturnsError()
    {
        _client.SearchAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Status.Should().Be(SourceStatus.Error);
        result.ErrorMessage.Should().Be("State SoS search failed");
    }

    [Fact]
    public async Task EnrichAsync_PollyTimeout_ReturnsTimeout()
    {
        using var pollyTokenSource = new CancellationTokenSource();
        _client.SearchAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(pollyTokenSource.Token));

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Status.Should().Be(SourceStatus.Timeout);
    }

    [Fact]
    public async Task EnrichAsync_CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _client.SearchAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var act = () => _sut.EnrichAsync(CreateContext(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task EnrichAsync_NullContext_Throws()
    {
        var act = () => _sut.EnrichAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
