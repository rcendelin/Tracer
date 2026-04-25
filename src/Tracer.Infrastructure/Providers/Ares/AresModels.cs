using System.Text.Json.Serialization;

namespace Tracer.Infrastructure.Providers.Ares;

/// <summary>
/// Response model from ARES REST API endpoint:
/// <c>GET /ekonomicke-subjekty-v-be/rest/ekonomicke-subjekty/{ico}</c>
/// </summary>
internal sealed class AresEkonomickySubjekt
{
    [JsonPropertyName("ico")]
    public string? Ico { get; init; }

    [JsonPropertyName("obchodniJmeno")]
    public string? ObchodniJmeno { get; init; }

    [JsonPropertyName("sidlo")]
    public AresSidlo? Sidlo { get; init; }

    [JsonPropertyName("pravniForma")]
    public string? PravniForma { get; init; }

    [JsonPropertyName("dic")]
    public string? Dic { get; init; }

    [JsonPropertyName("datumVzniku")]
    public string? DatumVzniku { get; init; }

    [JsonPropertyName("datumZaniku")]
    public string? DatumZaniku { get; init; }

    [JsonPropertyName("czNace")]
    public IReadOnlyCollection<string>? CzNace { get; init; }

    [JsonPropertyName("financniUrad")]
    public string? FinancniUrad { get; init; }

    [JsonPropertyName("datumAktualizace")]
    public string? DatumAktualizace { get; init; }
}

/// <summary>
/// Address (sídlo) from ARES response.
/// </summary>
internal sealed class AresSidlo
{
    [JsonPropertyName("kodStatu")]
    public string? KodStatu { get; init; }

    [JsonPropertyName("nazevObce")]
    public string? NazevObce { get; init; }

    [JsonPropertyName("nazevUlice")]
    public string? NazevUlice { get; init; }

    [JsonPropertyName("cisloDomovni")]
    public int? CisloDomovni { get; init; }

    [JsonPropertyName("cisloOrientacni")]
    public int? CisloOrientacni { get; init; }

    [JsonPropertyName("psc")]
    public int? Psc { get; init; }

    [JsonPropertyName("textovaAdresa")]
    public string? TextovaAdresa { get; init; }

    [JsonPropertyName("nazevKraje")]
    public string? NazevKraje { get; init; }

    [JsonPropertyName("nazevCastiObce")]
    public string? NazevCastiObce { get; init; }
}

/// <summary>
/// Response model from ARES search endpoint:
/// <c>GET /ekonomicke-subjekty-v-be/rest/ekonomicke-subjekty/vyhledat</c>
/// </summary>
internal sealed class AresSearchResponse
{
    [JsonPropertyName("pocetCelkem")]
    public int PocetCelkem { get; init; }

    [JsonPropertyName("ekonomickeSubjekty")]
    public IReadOnlyCollection<AresEkonomickySubjekt>? EkonomickeSubjekty { get; init; }
}
