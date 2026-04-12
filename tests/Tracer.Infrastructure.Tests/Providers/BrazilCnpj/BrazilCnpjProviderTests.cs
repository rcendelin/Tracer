using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;
using Tracer.Infrastructure.Providers.BrazilCnpj;

namespace Tracer.Infrastructure.Tests.Providers.BrazilCnpj;

/// <summary>
/// Unit tests for <see cref="BrazilCnpjProvider"/>.
/// Uses NSubstitute for <see cref="IBrazilCnpjClient"/> and NullLogger
/// (required because the provider is internal sealed).
/// </summary>
public sealed class BrazilCnpjProviderTests
{
    private readonly IBrazilCnpjClient _client = Substitute.For<IBrazilCnpjClient>();
    private readonly BrazilCnpjProvider _sut;

    public BrazilCnpjProviderTests()
    {
        _sut = new BrazilCnpjProvider(_client, NullLogger<BrazilCnpjProvider>.Instance);
    }

    // ── Context helpers ──────────────────────────────────────────────────────

    private static TraceContext CreateContext(
        string? country = "BR",
        string? companyName = "Petrobras",
        string? registrationId = "33000167000101",
        TraceDepth depth = TraceDepth.Standard) =>
        new()
        {
            Request = new Domain.Entities.TraceRequest(
                companyName: companyName,
                phone: null, email: null, website: null, address: null,
                city: null, country: country,
                registrationId: registrationId, taxId: null, industryHint: null,
                depth: depth,
                callbackUrl: null,
                source: "test"),
        };

    private static BrazilCnpjResponse PetrobrasResponse() =>
        new()
        {
            Cnpj = "33000167000101",
            RazaoSocial = "PETROLEO BRASILEIRO S.A. PETROBRAS",
            NomeFantasia = "PETROBRAS",
            DescricaoSituacaoCadastral = "ATIVA",
            NaturezaJuridica = "Sociedade de Economia Mista",
            CnaeFiscalDescricao = "Extração de petróleo e gás natural",
            CnaeFiscal = 600001,
            Porte = "DEMAIS",
            Logradouro = "AVENIDA REPUBLICA DO CHILE",
            Numero = "65",
            Complemento = null,
            Bairro = "CENTRO",
            Municipio = "RIO DE JANEIRO",
            Uf = "RJ",
            Cep = "20031912",
            DddTelefone1 = "2132242164",
            Email = "INVESTIDORES@PETROBRAS.COM.BR",
        };

    // ── Provider metadata ────────────────────────────────────────────────────

    [Fact]
    public void Properties_AreCorrect()
    {
        _sut.ProviderId.Should().Be("brazil-cnpj");
        _sut.Priority.Should().Be(200);
        _sut.SourceQuality.Should().Be(0.90);
    }

    // ── CanHandle ────────────────────────────────────────────────────────────

    [Fact]
    public void CanHandle_BrazilWithCnpj_Standard_ReturnsTrue()
    {
        _sut.CanHandle(CreateContext(country: "BR", registrationId: "33000167000101"))
            .Should().BeTrue();
    }

    [Fact]
    public void CanHandle_BrazilWithFormattedCnpj_ReturnsTrue()
    {
        _sut.CanHandle(CreateContext(country: "BR", registrationId: "33.000.167/0001-01"))
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
    public void CanHandle_NonBrazilCountry_WithCnpjFormat_ReturnsTrue()
    {
        // CNPJ pattern match overrides country code
        _sut.CanHandle(CreateContext(country: "US", registrationId: "33000167000101"))
            .Should().BeTrue();
    }

    [Fact]
    public void CanHandle_NonBrazilCountry_WithNonCnpjId_ReturnsFalse()
    {
        _sut.CanHandle(CreateContext(country: "US", registrationId: "12345678"))
            .Should().BeFalse();
    }

    [Fact]
    public void CanHandle_NoCnpj_ReturnsFalse()
    {
        // BrasilAPI requires CNPJ — no name search support
        _sut.CanHandle(CreateContext(country: "BR", registrationId: null, companyName: "Petrobras"))
            .Should().BeFalse("BrasilAPI doesn't support name search");
    }

    [Fact]
    public void CanHandle_NullContext_Throws()
    {
        var act = () => _sut.CanHandle(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── EnrichAsync — happy path ─────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_FullResult_MapsAllExpectedFields()
    {
        _client.GetByCnpjAsync("33000167000101", Arg.Any<CancellationToken>())
            .Returns(PetrobrasResponse());

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Found.Should().BeTrue();
        result.Status.Should().Be(SourceStatus.Success);
        result.Fields.Should().ContainKey(FieldName.LegalName);
        result.Fields[FieldName.LegalName].Should().Be("PETROLEO BRASILEIRO S.A. PETROBRAS");
        result.Fields.Should().ContainKey(FieldName.TradeName);
        result.Fields[FieldName.TradeName].Should().Be("PETROBRAS");
        result.Fields.Should().ContainKey(FieldName.RegistrationId);
        result.Fields.Should().ContainKey(FieldName.LegalForm);
        result.Fields.Should().ContainKey(FieldName.EntityStatus);
        result.Fields[FieldName.EntityStatus].Should().Be("active");
        result.Fields.Should().ContainKey(FieldName.Industry);
        result.Fields.Should().ContainKey(FieldName.RegisteredAddress);
        result.Fields.Should().ContainKey(FieldName.Phone);
        result.Fields.Should().ContainKey(FieldName.Email);
    }

    [Fact]
    public async Task EnrichAsync_CnpjFormatting_StoresFormattedCnpj()
    {
        _client.GetByCnpjAsync("33000167000101", Arg.Any<CancellationToken>())
            .Returns(PetrobrasResponse());

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Fields[FieldName.RegistrationId].Should().Be("33.000.167/0001-01");
    }

    [Fact]
    public async Task EnrichAsync_Address_MappedCorrectly()
    {
        _client.GetByCnpjAsync("33000167000101", Arg.Any<CancellationToken>())
            .Returns(PetrobrasResponse());

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        var addr = result.Fields[FieldName.RegisteredAddress].Should().BeOfType<Address>().Subject;
        addr.Street.Should().Contain("AVENIDA REPUBLICA DO CHILE");
        addr.Street.Should().Contain("65");
        addr.City.Should().Be("RIO DE JANEIRO");
        addr.PostalCode.Should().Be("20031-912");
        addr.Region.Should().Be("RJ");
        addr.Country.Should().Be("BR");
    }

    [Fact]
    public async Task EnrichAsync_Phone_FormattedAsBrazilian()
    {
        _client.GetByCnpjAsync("33000167000101", Arg.Any<CancellationToken>())
            .Returns(PetrobrasResponse());

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        var phone = (string)result.Fields[FieldName.Phone]!;
        phone.Should().StartWith("+55");
        phone.Should().Contain("(21)");
    }

    // ── EnrichAsync — status normalization ────────────────────────────────────

    [Theory]
    [InlineData("ATIVA", "active")]
    [InlineData("INAPTA", "inactive")]
    [InlineData("SUSPENSA", "suspended")]
    [InlineData("BAIXADA", "dissolved")]
    [InlineData("NULA", "annulled")]
    [InlineData("UNKNOWN_STATUS", "UNKNOWN_STATUS")]
    public void NormalizeStatus_PortugueseToEnglish(string input, string expected)
    {
        BrazilCnpjProvider.NormalizeStatus(input).Should().Be(expected);
    }

    // ── EnrichAsync — not found ──────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_CnpjNotFound_ReturnsNotFound()
    {
        _client.GetByCnpjAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((BrazilCnpjResponse?)null);

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Found.Should().BeFalse();
        result.Status.Should().Be(SourceStatus.NotFound);
    }

    [Fact]
    public async Task EnrichAsync_NoCnpjAvailable_ReturnsNotFound()
    {
        var ctx = CreateContext(registrationId: null, companyName: "Petrobras");

        var result = await _sut.EnrichAsync(ctx, CancellationToken.None);

        result.Found.Should().BeFalse();
        result.Status.Should().Be(SourceStatus.NotFound);
        await _client.DidNotReceive().GetByCnpjAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── EnrichAsync — error cases ────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_HttpRequestException_ReturnsError()
    {
        _client.GetByCnpjAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Status.Should().Be(SourceStatus.Error);
        result.ErrorMessage.Should().Be("BrasilAPI CNPJ call failed");
        result.Found.Should().BeFalse();
    }

    [Fact]
    public async Task EnrichAsync_PollyTimeout_ReturnsTimeout()
    {
        using var pollyTokenSource = new CancellationTokenSource();
        _client.GetByCnpjAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(pollyTokenSource.Token));

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Status.Should().Be(SourceStatus.Timeout);
    }

    [Fact]
    public async Task EnrichAsync_CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _client.GetByCnpjAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
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

    // ── EnrichAsync — address edge cases ─────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_NoAddress_OmitsAddressField()
    {
        var noAddr = new BrazilCnpjResponse
        {
            Cnpj = "33000167000101",
            RazaoSocial = "PETROBRAS",
            DescricaoSituacaoCadastral = "ATIVA",
            Logradouro = null,
            Municipio = null,
        };
        _client.GetByCnpjAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(noAddr);

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Found.Should().BeTrue();
        result.Fields.Should().NotContainKey(FieldName.RegisteredAddress);
    }

    [Fact]
    public async Task EnrichAsync_CityOnly_IncludesAddress()
    {
        var cityOnly = new BrazilCnpjResponse
        {
            Cnpj = "33000167000101",
            RazaoSocial = "PETROBRAS",
            DescricaoSituacaoCadastral = "ATIVA",
            Logradouro = null,
            Numero = null,
            Municipio = "SAO PAULO",
        };
        _client.GetByCnpjAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(cityOnly);

        var result = await _sut.EnrichAsync(CreateContext(), CancellationToken.None);

        result.Fields.Should().ContainKey(FieldName.RegisteredAddress);
        var addr = (Address)result.Fields[FieldName.RegisteredAddress]!;
        addr.City.Should().Be("SAO PAULO");
    }
}
