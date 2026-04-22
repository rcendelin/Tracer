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

public sealed class ArgentinaAfipProviderTests
{
    private readonly ILatamRegistryClient _client = Substitute.For<ILatamRegistryClient>();
    private readonly ArgentinaAfipProvider _sut;

    public ArgentinaAfipProviderTests()
    {
        _sut = new ArgentinaAfipProvider(_client, NullLogger<ArgentinaAfipProvider>.Instance);
    }

    private static TraceContext CreateContext(
        string? country = "AR",
        string? registrationId = "30-50001091-2",
        string? taxId = null,
        TraceDepth depth = TraceDepth.Standard) =>
        LatamProviderTestContext.Create(country, registrationId, taxId,
            companyName: "Some SA", depth: depth);

    private static LatamRegistrySearchResult AcmeResult() => new()
    {
        EntityName = "ACME SOCIEDAD ANÓNIMA",
        RegistrationId = "30500010912",
        CountryCode = "AR",
        Status = "ACTIVO",
        EntityType = "SOCIEDAD ANONIMA",
        Address = "Av. Corrientes 1234, CABA",
    };

    // ── Provider metadata ────────────────────────────────────────────────────

    [Fact]
    public void Properties_AreCorrect()
    {
        _sut.ProviderId.Should().Be("latam-afip");
        _sut.Priority.Should().Be(200);
        _sut.SourceQuality.Should().Be(0.80);
    }

    // ── CanHandle ────────────────────────────────────────────────────────────

    [Fact]
    public void CanHandle_CountryAr_Standard_ReturnsTrue()
    {
        _sut.CanHandle(CreateContext()).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_Deep_ReturnsTrue()
    {
        _sut.CanHandle(CreateContext(depth: TraceDepth.Deep)).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_Quick_ReturnsFalse()
    {
        _sut.CanHandle(CreateContext(depth: TraceDepth.Quick)).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_OtherCountryWithCuitFormat_ReturnsTrue()
    {
        // 11-digit CUIT even without AR country hint
        _sut.CanHandle(CreateContext(country: "US", registrationId: "30500010912"))
            .Should().BeTrue();
    }

    [Fact]
    public void CanHandle_OtherCountryNoIdentifier_ReturnsFalse()
    {
        _sut.CanHandle(CreateContext(country: "BR", registrationId: null, taxId: null))
            .Should().BeFalse();
    }

    [Fact]
    public void CanHandle_UsesTaxIdFallback_WhenRegistrationIdMissing()
    {
        _sut.CanHandle(CreateContext(country: "AR", registrationId: null, taxId: "30-50001091-2"))
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
    public async Task EnrichAsync_Found_MapsFields()
    {
        _client.LookupAsync("AR", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AcmeResult());

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Found.Should().BeTrue();
        result.Status.Should().Be(SourceStatus.Success);
        result.Fields[FieldName.LegalName].Should().Be("ACME SOCIEDAD ANÓNIMA");
        result.Fields[FieldName.RegistrationId].Should().Be("AR:30500010912");
        result.Fields[FieldName.EntityStatus].Should().Be("active");
        result.Fields[FieldName.LegalForm].Should().Be("SOCIEDAD ANONIMA");
    }

    // ── Status normalization ─────────────────────────────────────────────────

    [Theory]
    [InlineData("ACTIVO", "active")]
    [InlineData("Activo", "active")]
    [InlineData("INACTIVO", "inactive")]
    [InlineData("SUSPENDIDO", "suspended")]
    [InlineData("BAJA", "dissolved")]
    [InlineData("CANCELADO", "dissolved")]
    [InlineData("UNKNOWN", "UNKNOWN")]
    public void NormalizeStatus_SpanishToCanonical(string input, string expected)
    {
        ArgentinaAfipAdapter.NormalizeStatus(input).Should().Be(expected);
    }

    // ── NotFound / error cases ───────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_NoMatch_ReturnsNotFound()
    {
        _client.LookupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((LatamRegistrySearchResult?)null);

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Found.Should().BeFalse();
        result.Status.Should().Be(SourceStatus.NotFound);
    }

    [Fact]
    public async Task EnrichAsync_NoIdentifier_ReturnsNotFound()
    {
        var ctx = CreateContext(registrationId: null, taxId: null);

        var result = await _sut.EnrichAsync(ctx, CancellationToken.None);

        result.Status.Should().Be(SourceStatus.NotFound);
    }

    [Fact]
    public async Task EnrichAsync_HttpRequestException_ReturnsError()
    {
        _client.LookupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Status.Should().Be(SourceStatus.Error);
        result.ErrorMessage.Should().Be("AFIP Constancia lookup failed");
        result.ErrorMessage.Should().NotContain("Connection", "error message must be sanitized (CWE-209)");
    }

    [Fact]
    public async Task EnrichAsync_PollyTimeout_ReturnsTimeout()
    {
        using var pollyTokenSource = new CancellationTokenSource();
        _client.LookupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(pollyTokenSource.Token));

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

    [Fact]
    public async Task EnrichAsync_NullContext_Throws()
    {
        var act = () => _sut.EnrichAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ── Defensive mapping ───────────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_WhitespaceRegistrationId_SkipsPrefixedField()
    {
        // A mis-parsed adapter could surface a whitespace-only identifier; the
        // provider must not emit "AR: " as a CKB key — drop it instead.
        _client.LookupAsync("AR", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new LatamRegistrySearchResult
            {
                EntityName = "ACME",
                RegistrationId = "   ",
                CountryCode = "AR",
            });

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Status.Should().Be(SourceStatus.Success);
        result.Fields.Should().ContainKey(FieldName.LegalName);
        result.Fields.Should().NotContainKey(FieldName.RegistrationId);
    }
}
