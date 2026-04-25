using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Infrastructure.Providers.BrazilCnpj;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Tracer.Infrastructure.Tests.Providers.BrazilCnpj;

/// <summary>
/// Integration tests for <see cref="BrazilCnpjClient"/> using WireMock to simulate
/// BrasilAPI JSON responses without making real network calls.
/// </summary>
public sealed class BrazilCnpjClientWireMockTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly HttpClient _httpClient;
    private readonly BrazilCnpjClient _client;

    public BrazilCnpjClientWireMockTests()
    {
        _server = WireMockServer.Start();
        _httpClient = new HttpClient { BaseAddress = new Uri(_server.Url!) };
        _client = new BrazilCnpjClient(_httpClient, NullLogger<BrazilCnpjClient>.Instance);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _server.Dispose();
    }

    // ── GetByCnpjAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetByCnpjAsync_Found_ReturnsFullResponse()
    {
        _server.Given(
            Request.Create()
                .WithPath("/cnpj/v1/33000167000101")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(PetrobrasJson));

        var result = await _client.GetByCnpjAsync("33000167000101", CancellationToken.None);

        result.Should().NotBeNull();
        result!.RazaoSocial.Should().Be("PETROLEO BRASILEIRO S.A. PETROBRAS");
        result.NomeFantasia.Should().Be("PETROBRAS");
        result.Cnpj.Should().Be("33000167000101");
        result.DescricaoSituacaoCadastral.Should().Be("ATIVA");
        result.NaturezaJuridica.Should().Be("Sociedade de Economia Mista");
        result.CnaeFiscalDescricao.Should().Be("Extração de petróleo e gás natural");
        result.Municipio.Should().Be("RIO DE JANEIRO");
        result.Uf.Should().Be("RJ");
        result.DddTelefone1.Should().Be("2132242164");
    }

    [Fact]
    public async Task GetByCnpjAsync_FormattedCnpj_NormalizesBeforeRequest()
    {
        _server.Given(
            Request.Create()
                .WithPath("/cnpj/v1/33000167000101")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(PetrobrasJson));

        // Pass formatted CNPJ — client should normalize to 14 digits
        var result = await _client.GetByCnpjAsync("33.000.167/0001-01", CancellationToken.None);

        result.Should().NotBeNull();
        result!.RazaoSocial.Should().Contain("PETROBRAS");
    }

    [Fact]
    public async Task GetByCnpjAsync_NotFound_ReturnsNull()
    {
        _server.Given(
            Request.Create()
                .WithPath("/cnpj/v1/00000000000000")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(404));

        var result = await _client.GetByCnpjAsync("00000000000000", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByCnpjAsync_ServerError_ThrowsHttpRequestException()
    {
        _server.Given(
            Request.Create()
                .WithPath("/cnpj/v1/33000167000101")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(500));

        var act = () => _client.GetByCnpjAsync("33000167000101", CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GetByCnpjAsync_NullOrEmpty_ThrowsArgumentException()
    {
        var act = () => _client.GetByCnpjAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();

        var act2 = () => _client.GetByCnpjAsync("  ", CancellationToken.None);
        await act2.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetByCnpjAsync_InvalidCnpjFormat_ThrowsArgumentException()
    {
        var act = () => _client.GetByCnpjAsync("1234", CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*14 digits*");
    }

    // ── NormalizeCnpj ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("33000167000101", "33000167000101")]
    [InlineData("33.000.167/0001-01", "33000167000101")]
    [InlineData(" 33.000.167/0001-01 ", "33000167000101")]
    public void NormalizeCnpj_StripsFormattingCharacters(string input, string expected)
    {
        BrazilCnpjClient.NormalizeCnpj(input).Should().Be(expected);
    }

    // ── JSON fixture ─────────────────────────────────────────────────────────

    private const string PetrobrasJson = """
        {
            "cnpj": "33000167000101",
            "razao_social": "PETROLEO BRASILEIRO S.A. PETROBRAS",
            "nome_fantasia": "PETROBRAS",
            "descricao_situacao_cadastral": "ATIVA",
            "natureza_juridica": "Sociedade de Economia Mista",
            "cnae_fiscal_descricao": "Extração de petróleo e gás natural",
            "cnae_fiscal": 600001,
            "porte": "DEMAIS",
            "logradouro": "AVENIDA REPUBLICA DO CHILE",
            "numero": "65",
            "complemento": "",
            "bairro": "CENTRO",
            "municipio": "RIO DE JANEIRO",
            "uf": "RJ",
            "cep": "20031912",
            "ddd_telefone_1": "2132242164",
            "ddd_telefone_2": "",
            "ddd_fax": "",
            "email": "INVESTIDORES@PETROBRAS.COM.BR",
            "data_inicio_atividade": "1966-09-28",
            "data_situacao_cadastral": "2005-11-03"
        }
        """;
}
