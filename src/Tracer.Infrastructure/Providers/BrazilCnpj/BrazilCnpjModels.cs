using System.Text.Json.Serialization;

namespace Tracer.Infrastructure.Providers.BrazilCnpj;

/// <summary>
/// Response model from BrasilAPI CNPJ endpoint:
/// <c>GET /api/cnpj/v1/{cnpj}</c>
/// <para>
/// Source: <see href="https://brasilapi.com.br/docs#tag/CNPJ"/>
/// Data originates from the Brazilian Federal Revenue Service (Receita Federal do Brasil).
/// </para>
/// </summary>
internal sealed class BrazilCnpjResponse
{
    [JsonPropertyName("cnpj")]
    public string? Cnpj { get; init; }

    /// <summary>Legal name (razão social).</summary>
    [JsonPropertyName("razao_social")]
    public string? RazaoSocial { get; init; }

    /// <summary>Trade name / brand name (nome fantasia).</summary>
    [JsonPropertyName("nome_fantasia")]
    public string? NomeFantasia { get; init; }

    /// <summary>Registration status code (e.g. 2 = ATIVA, 3 = SUSPENSA, 4 = INAPTA, 8 = BAIXADA).</summary>
    [JsonPropertyName("descricao_situacao_cadastral")]
    public string? DescricaoSituacaoCadastral { get; init; }

    /// <summary>Legal nature description (e.g. "Sociedade Anônima Aberta").</summary>
    [JsonPropertyName("natureza_juridica")]
    public string? NaturezaJuridica { get; init; }

    /// <summary>Primary economic activity description (CNAE).</summary>
    [JsonPropertyName("cnae_fiscal_descricao")]
    public string? CnaeFiscalDescricao { get; init; }

    /// <summary>Primary economic activity code.</summary>
    [JsonPropertyName("cnae_fiscal")]
    public long? CnaeFiscal { get; init; }

    /// <summary>Company size (e.g. "DEMAIS", "ME", "EPP").</summary>
    [JsonPropertyName("porte")]
    public string? Porte { get; init; }

    // ── Address fields ─────────────────────────────────────────────────

    [JsonPropertyName("logradouro")]
    public string? Logradouro { get; init; }

    [JsonPropertyName("numero")]
    public string? Numero { get; init; }

    [JsonPropertyName("complemento")]
    public string? Complemento { get; init; }

    [JsonPropertyName("bairro")]
    public string? Bairro { get; init; }

    [JsonPropertyName("municipio")]
    public string? Municipio { get; init; }

    [JsonPropertyName("uf")]
    public string? Uf { get; init; }

    [JsonPropertyName("cep")]
    public string? Cep { get; init; }

    // ── Contact fields ─────────────────────────────────────────────────

    /// <summary>Phone number including area code (e.g. "2132242164").</summary>
    [JsonPropertyName("ddd_telefone_1")]
    public string? DddTelefone1 { get; init; }

    /// <summary>Secondary phone number.</summary>
    [JsonPropertyName("ddd_telefone_2")]
    public string? DddTelefone2 { get; init; }

    /// <summary>Fax number.</summary>
    [JsonPropertyName("ddd_fax")]
    public string? DddFax { get; init; }

    /// <summary>Company email address.</summary>
    [JsonPropertyName("email")]
    public string? Email { get; init; }

    // ── Date fields ────────────────────────────────────────────────────

    [JsonPropertyName("data_inicio_atividade")]
    public string? DataInicioAtividade { get; init; }

    [JsonPropertyName("data_situacao_cadastral")]
    public string? DataSituacaoCadastral { get; init; }
}
