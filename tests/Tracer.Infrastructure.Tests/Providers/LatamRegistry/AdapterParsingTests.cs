using FluentAssertions;
using Tracer.Infrastructure.Providers.LatamRegistry.Adapters;

namespace Tracer.Infrastructure.Tests.Providers.LatamRegistry;

/// <summary>
/// Exercise the AngleSharp-based HTML label extractors of each LATAM adapter.
/// Uses minimal hand-crafted HTML fixtures representative of each registry's
/// layout — enough to validate the parser recognises the labels we rely on.
/// </summary>
public sealed class AdapterParsingTests
{
    // ── Argentina (AFIP) ─────────────────────────────────────────────────────

    [Fact]
    public void ArgentinaAfip_Parse_LabelledTable_ReturnsResult()
    {
        const string html = """
            <html><body>
            <table>
              <tr><th>Razón Social</th><td>ACME SA</td></tr>
              <tr><th>Estado</th><td>ACTIVO</td></tr>
              <tr><th>Forma Jurídica</th><td>SOCIEDAD ANONIMA</td></tr>
              <tr><th>Domicilio Fiscal</th><td>Av. Corrientes 1234</td></tr>
            </table>
            </body></html>
            """;

        var result = new ArgentinaAfipAdapter().Parse(html, "30500010912");

        result.Should().NotBeNull();
        result!.EntityName.Should().Be("ACME SA");
        result.RegistrationId.Should().Be("30500010912");
        result.Status.Should().Be("ACTIVO");
        result.EntityType.Should().Be("SOCIEDAD ANONIMA");
        result.Address.Should().Be("Av. Corrientes 1234");
    }

    [Fact]
    public void ArgentinaAfip_Parse_NoMatch_ReturnsNull()
    {
        const string html = "<html><body><p>No se encontraron resultados</p></body></html>";

        new ArgentinaAfipAdapter().Parse(html, "30500010912").Should().BeNull();
    }

    [Theory]
    [InlineData("30-50001091-2", "30500010912")]
    [InlineData("30500010912", "30500010912")]
    [InlineData("30.50001091.2", "30500010912")]
    public void ArgentinaAfip_Normalize_AcceptsFormattedCuit(string input, string expected)
    {
        new ArgentinaAfipAdapter().NormalizeIdentifier(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("1234567890")]       // too short
    [InlineData("123456789012")]     // too long
    [InlineData("")]
    public void ArgentinaAfip_Normalize_RejectsInvalid(string input)
    {
        new ArgentinaAfipAdapter().NormalizeIdentifier(input).Should().BeNull();
    }

    // ── Chile (SII) ──────────────────────────────────────────────────────────

    [Fact]
    public void ChileSii_Parse_BoldLabels_ReturnsResult()
    {
        const string html = """
            <html><body>
            <p><b>Razón Social:</b> COPEC S.A.</p>
            <p><b>Situación Tributaria:</b> Contribuyente Vigente</p>
            <p><b>Actividades Económicas:</b> Venta de combustibles</p>
            </body></html>
            """;

        var result = new ChileSiiAdapter().Parse(html, "99520000-7");

        result.Should().NotBeNull();
        result!.EntityName.Should().Be("COPEC S.A.");
        result.Status.Should().Be("Contribuyente Vigente");
        result.EntityType.Should().Be("Venta de combustibles");
    }

    [Theory]
    [InlineData("96.790.240-3", "96790240-3")]
    [InlineData("96790240-3", "96790240-3")]
    [InlineData("1234567-K", "1234567-K")]
    [InlineData("1234567-k", "1234567-K")] // uppercase verifier
    public void ChileSii_Normalize_AcceptsVariants(string input, string expected)
    {
        new ChileSiiAdapter().NormalizeIdentifier(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("123-4")]             // too short
    [InlineData("abcdefgh-X")]        // X is not K
    [InlineData("")]
    public void ChileSii_Normalize_RejectsInvalid(string input)
    {
        new ChileSiiAdapter().NormalizeIdentifier(input).Should().BeNull();
    }

    [Fact]
    public void ChileSii_BuildLookupRequest_DefensiveGuardRejectsUnnormalizedInput()
    {
        // Guard catches callers that bypass NormalizeIdentifier (contract breach).
        var act = () => new ChileSiiAdapter().BuildLookupRequest("no-dash-but-chars");
        // "no-dash-but-chars".Split('-', 2) yields 2 parts → still OK
        act.Should().NotThrow();

        var act2 = () => new ChileSiiAdapter().BuildLookupRequest("nodashatall");
        act2.Should().Throw<ArgumentException>()
            .WithParameterName("normalizedIdentifier");
    }

    // ── Colombia (RUES) ──────────────────────────────────────────────────────

    [Fact]
    public void ColombiaRues_Parse_Table_ReturnsResult()
    {
        const string html = """
            <html><body>
            <table>
              <tr><td>Razón Social</td><td>BANCOLOMBIA S.A.</td></tr>
              <tr><td>Estado</td><td>ACTIVA</td></tr>
              <tr><td>Organización Jurídica</td><td>SOCIEDAD ANONIMA</td></tr>
              <tr><td>Dirección Comercial</td><td>Carrera 48 #26-85</td></tr>
            </table>
            </body></html>
            """;

        var result = new ColombiaRuesAdapter().Parse(html, "890903938");

        result.Should().NotBeNull();
        result!.EntityName.Should().Be("BANCOLOMBIA S.A.");
        result.Status.Should().Be("ACTIVA");
        result.EntityType.Should().Be("SOCIEDAD ANONIMA");
        result.Address.Should().Be("Carrera 48 #26-85");
    }

    [Theory]
    [InlineData("890903938", "890903938")]
    [InlineData("890.903.938", "890903938")]
    [InlineData("890903938-8", "8909039388")]
    public void ColombiaRues_Normalize_StripsFormatting(string input, string expected)
    {
        new ColombiaRuesAdapter().NormalizeIdentifier(input).Should().Be(expected);
    }

    // ── Mexico (SAT) ─────────────────────────────────────────────────────────

    [Fact]
    public void MexicoSat_Parse_Table_ReturnsResult()
    {
        const string html = """
            <html><body>
            <table>
              <tr><th>Denominación o Razón Social</th><td>WAL-MART DE MEXICO SAB DE CV</td></tr>
              <tr><th>Estatus del Contribuyente</th><td>ACTIVO</td></tr>
              <tr><th>Régimen</th><td>PERSONA MORAL</td></tr>
              <tr><th>Domicilio Fiscal</th><td>Av. Ejército Nacional</td></tr>
            </table>
            </body></html>
            """;

        var result = new MexicoSatAdapter().Parse(html, "WMT970714R10");

        result.Should().NotBeNull();
        result!.EntityName.Should().Be("WAL-MART DE MEXICO SAB DE CV");
        result.Status.Should().Be("ACTIVO");
        result.EntityType.Should().Be("PERSONA MORAL");
        result.Address.Should().Be("Av. Ejército Nacional");
    }

    [Fact]
    public void MexicoSat_Parse_CaptchaWall_ReturnsNull()
    {
        const string html = """
            <html><body>
            <form><label>Captcha:</label><input name="captcha"/></form>
            <p>Por favor valide que no es un robot.</p>
            </body></html>
            """;

        new MexicoSatAdapter().Parse(html, "WMT970714R10").Should().BeNull();
    }

    [Theory]
    [InlineData("wmt970714r10", "WMT970714R10")]
    [InlineData("WMT970714R10", "WMT970714R10")]
    [InlineData("JUAP850514H14", "JUAP850514H14")]
    public void MexicoSat_Normalize_UpcasesAndValidates(string input, string expected)
    {
        new MexicoSatAdapter().NormalizeIdentifier(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("1234567")]
    [InlineData("XY-GARBAGE")]
    [InlineData("")]
    public void MexicoSat_Normalize_RejectsInvalid(string input)
    {
        new MexicoSatAdapter().NormalizeIdentifier(input).Should().BeNull();
    }
}
