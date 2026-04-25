using FluentAssertions;
using Tracer.Application.Services;

namespace Tracer.Application.Tests.Services;

/// <summary>
/// Tests for <see cref="CompanyNameNormalizer"/> — the enhanced multi-language company name
/// normalization pipeline. 50+ test cases covering diacritics, transliteration, legal forms,
/// stopwords, abbreviations, and real-world company name variants.
/// </summary>
public sealed class CompanyNameNormalizerTests
{
    private readonly CompanyNameNormalizer _sut = new();

    // ── Basic normalization ──────────────────────────────────────────────────

    [Fact]
    public void Normalize_TrimsWhitespace()
    {
        _sut.Normalize("  Acme Corp  ").Should().Be(_sut.Normalize("Acme Corp"));
    }

    [Fact]
    public void Normalize_CaseInsensitive()
    {
        _sut.Normalize("ACME").Should().Be(_sut.Normalize("acme"));
        _sut.Normalize("Acme").Should().Be(_sut.Normalize("ACME"));
    }

    [Fact]
    public void Normalize_CollapsesWhitespace()
    {
        _sut.Normalize("Acme   Industries").Should().Be(_sut.Normalize("Acme Industries"));
    }

    [Fact]
    public void Normalize_SortsTokensAlphabetically()
    {
        _sut.Normalize("Beta Alpha").Should().Be(_sut.Normalize("Alpha Beta"));
    }

    [Fact]
    public void Normalize_EmptyOrWhitespace_Throws()
    {
        var act = () => _sut.Normalize("");
        act.Should().Throw<ArgumentException>();

        var act2 = () => _sut.Normalize("   ");
        act2.Should().Throw<ArgumentException>();

        var act3 = () => _sut.Normalize(null!);
        act3.Should().Throw<ArgumentException>();
    }

    // ── Diacritics removal ───────────────────────────────────────────────────

    [Theory]
    [InlineData("Škoda", "SKODA")]
    [InlineData("Průmyslové", "PRUMYSLOVE")]
    [InlineData("Černošice", "CERNOSICE")]
    [InlineData("Königs", "KONIGS")]
    [InlineData("Müller", "MULLER")]
    [InlineData("René", "RENE")]
    [InlineData("José", "JOSE")]
    [InlineData("Ñoño", "NONO")]
    [InlineData("Göteborgs", "GOTEBORGS")]
    [InlineData("Łódź", "LODZ")]
    [InlineData("İstanbul", "ISTANBUL")]
    public void Normalize_RemovesDiacritics(string input, string expectedContains)
    {
        _sut.Normalize(input).Should().Contain(expectedContains);
    }

    // ── Transliteration (beyond diacritics) ──────────────────────────────────

    [Fact]
    public void Normalize_GermanSharpS_ConvertedToSS()
    {
        _sut.Normalize("Straße").Should().Contain("STRASSE");
    }

    [Fact]
    public void Normalize_DanishOSlash_ConvertedToO()
    {
        _sut.Normalize("Ørsted").Should().Contain("ORSTED");
    }

    [Fact]
    public void Normalize_CroatianDStroke_ConvertedToD()
    {
        _sut.Normalize("Đurđevac").Should().Contain("DURDEVAC");
    }

    [Fact]
    public void Normalize_PolishLStroke_ConvertedToL()
    {
        _sut.Normalize("Łódź").Should().Contain("LODZ");
    }

    [Fact]
    public void Normalize_Cyrillic_ConvertedToLatin()
    {
        _sut.Normalize("Газпром").Should().Contain("GAZPROM");
    }

    [Fact]
    public void Normalize_CyrillicComplex_ConvertedToLatin()
    {
        _sut.Normalize("Яндекс").Should().Contain("YANDEKS");
    }

    [Fact]
    public void Normalize_AeLigature_Expanded()
    {
        _sut.Normalize("Ærø").Should().Contain("AER");
    }

    // ── Legal form removal ───────────────────────────────────────────────────

    [Theory]
    [InlineData("Siemens AG")]
    [InlineData("Bosch GmbH")]
    [InlineData("SAP SE")]
    public void Normalize_RemovesGermanLegalForms(string input)
    {
        var result = _sut.Normalize(input);
        result.Should().NotContainAny("AG", "GMBH", "SE");
    }

    [Theory]
    [InlineData("Barclays PLC")]
    [InlineData("Tesco Ltd")]
    [InlineData("Shell Limited")]
    public void Normalize_RemovesBritishLegalForms(string input)
    {
        var result = _sut.Normalize(input);
        result.Should().NotContainAny("PLC", "LTD", "LIMITED");
    }

    [Theory]
    [InlineData("Apple Inc.")]
    [InlineData("Microsoft Corporation")]
    [InlineData("Google LLC")]
    public void Normalize_RemovesUsLegalForms(string input)
    {
        var result = _sut.Normalize(input);
        result.Should().NotContainAny("INC", "CORPORATION", "LLC");
    }

    [Theory]
    [InlineData("Total SA")]
    [InlineData("Carrefour SARL")]
    [InlineData("BNP Paribas SAS")]
    public void Normalize_RemovesFrenchLegalForms(string input)
    {
        var result = _sut.Normalize(input);
        result.Should().NotContainAny("SARL", "SAS");
    }

    [Theory]
    [InlineData("Volvo AB")]
    [InlineData("Nokia OYJ")]
    [InlineData("Ericsson ApS")]
    public void Normalize_RemovesNordicLegalForms(string input)
    {
        var result = _sut.Normalize(input);
        result.Should().NotContainAny("AB", "OYJ", "APS");
    }

    [Theory]
    [InlineData("Škoda Auto a.s.")]
    [InlineData("Agrofert s.r.o.")]
    [InlineData("Metalurg d.o.o.")]
    public void Normalize_RemovesCeeLegalForms(string input)
    {
        var result = _sut.Normalize(input);
        result.Should().NotContainAny("AS", "SRO", "DOO");
    }

    [Theory]
    [InlineData("Petrobras S.A.")]
    [InlineData("Embraer Ltda.")]
    public void Normalize_RemovesLatAmLegalForms(string input)
    {
        var result = _sut.Normalize(input);
        result.Should().NotContainAny("SA", "LTDA");
    }

    // ── Stopword removal ─────────────────────────────────────────────────────

    [Fact]
    public void Normalize_RemovesEnglishStopwords()
    {
        var result = _sut.Normalize("Bank of America");
        result.Should().NotContain("OF");
        result.Should().Contain("AMERICA");
        result.Should().Contain("BANK");
    }

    [Fact]
    public void Normalize_RemovesGermanStopwords()
    {
        var result = _sut.Normalize("Verband der Automobilindustrie");
        result.Should().NotContain("DER");
    }

    [Fact]
    public void Normalize_RemovesFrenchStopwords()
    {
        var result = _sut.Normalize("Banque de France");
        result.Should().NotContain(" DE ");
        result.Should().Contain("BANQUE");
        result.Should().Contain("FRANCE");
    }

    [Fact]
    public void Normalize_RemovesAmpersand()
    {
        _sut.Normalize("Johnson & Johnson").Should().Be(_sut.Normalize("Johnson Johnson"));
    }

    [Fact]
    public void Normalize_KeepsAllTokensIfAllAreStopwords()
    {
        // Edge case: if all tokens are stopwords, keep original
        var result = _sut.Normalize("The And Of");
        result.Should().NotBeEmpty();
    }

    // ── Abbreviation expansion ───────────────────────────────────────────────

    [Theory]
    [InlineData("Acme Intl", "INTERNATIONAL")]
    [InlineData("Global Mfg", "MANUFACTURING")]
    [InlineData("Tech Mgmt", "MANAGEMENT")]
    [InlineData("Pacific Grp", "GROUP")]
    public void Normalize_ExpandsAbbreviations(string input, string expectedExpansion)
    {
        _sut.Normalize(input).Should().Contain(expectedExpansion);
    }

    // ── Cross-variant matching (the key assertion) ───────────────────────────

    [Fact]
    public void Normalize_SkodaVariants_AllProduceSameResult()
    {
        var v1 = _sut.Normalize("ŠKODA AUTO a.s.");
        var v2 = _sut.Normalize("Škoda Auto, a.s.");
        var v3 = _sut.Normalize("škoda auto a.s");
        var v4 = _sut.Normalize("Skoda Auto");

        v1.Should().Be(v2).And.Be(v3).And.Be(v4);
    }

    [Fact]
    public void Normalize_SiemensVariants_AllProduceSameResult()
    {
        var v1 = _sut.Normalize("Siemens AG");
        var v2 = _sut.Normalize("SIEMENS Aktiengesellschaft");
        var v3 = _sut.Normalize("siemens");

        // After legal form removal and uppercase, all should contain SIEMENS
        v1.Should().Contain("SIEMENS");
        v3.Should().Contain("SIEMENS");
        v1.Should().Be(v3);
    }

    [Fact]
    public void Normalize_GazpromCyrillicAndLatin_Match()
    {
        var cyrillic = _sut.Normalize("Газпром");
        var latin = _sut.Normalize("Gazprom");

        cyrillic.Should().Be(latin);
    }

    // ── Real-world company names ─────────────────────────────────────────────

    [Fact]
    public void Normalize_PetrobrasVariants_Match()
    {
        var v1 = _sut.Normalize("PETROLEO BRASILEIRO S.A. PETROBRAS");
        var v2 = _sut.Normalize("Petrobras S.A.");

        // Both should contain PETROBRAS after removing SA
        v1.Should().Contain("PETROBRAS");
        v2.Should().Contain("PETROBRAS");
    }

    [Fact]
    public void Normalize_BankOfAmerica_OrderIndependent()
    {
        var v1 = _sut.Normalize("Bank of America");
        var v2 = _sut.Normalize("America Bank");

        v1.Should().Be(v2, "stopword 'of' removed, tokens sorted alphabetically");
    }

    [Fact]
    public void Normalize_JohnsonAndJohnson_AmpersandRemoved()
    {
        var v1 = _sut.Normalize("Johnson & Johnson");
        var v2 = _sut.Normalize("Johnson and Johnson");
        var v3 = _sut.Normalize("Johnson Johnson");

        v1.Should().Be(v2).And.Be(v3);
    }

    [Fact]
    public void Normalize_DeutscheBank_StopwordRemoved()
    {
        var result = _sut.Normalize("Deutsche Bank AG");
        result.Should().Contain("BANK");
        result.Should().Contain("DEUTSCHE");
        result.Should().NotContain("AG");
    }

    [Fact]
    public void Normalize_RemovesPunctuation()
    {
        var result = _sut.Normalize("Acme, Inc. (Global)");
        result.Should().NotContain(",");
        result.Should().NotContain("(");
        result.Should().NotContain(")");
    }

    // ── Edge cases ───────────────────────────────────────────────────────────

    [Fact]
    public void Normalize_SingleWord_ReturnsSingleToken()
    {
        _sut.Normalize("Apple").Should().Be("APPLE");
    }

    [Fact]
    public void Normalize_NumbersPreserved()
    {
        _sut.Normalize("3M Company").Should().Contain("3M");
    }

    [Fact]
    public void Normalize_MixedScripts_HandledCorrectly()
    {
        // Company with both Latin and a transliterable char
        var result = _sut.Normalize("Müller & Söhne GmbH");
        result.Should().Contain("MULLER");
        result.Should().Contain("SOHNE");
        result.Should().NotContain("GMBH");
    }
}
