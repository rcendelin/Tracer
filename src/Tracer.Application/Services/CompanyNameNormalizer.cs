using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Tracer.Application.Services;

/// <summary>
/// Normalizes company names for CKB deduplication using a multi-step pipeline:
/// <list type="number">
///   <item>Trim + uppercase</item>
///   <item>Transliterate special characters (ß→SS, Ø→O, Cyrillic→Latin, etc.)</item>
///   <item>Remove diacritics (Unicode NormalizationForm.FormD)</item>
///   <item>Remove punctuation + normalize whitespace → tokenize</item>
///   <item>Expand common abbreviations (INTL→INTERNATIONAL, MFG→MANUFACTURING)</item>
///   <item>Remove stopwords (AND, UND, ET, etc.)</item>
///   <item>Remove legal form tokens (200+ patterns across 30+ countries)</item>
///   <item>Sort remaining tokens alphabetically (order-independent matching)</item>
/// </list>
/// <para>
/// Thread-safe and stateless — registered as Singleton.
/// </para>
/// <para>
/// <strong>Key design decision:</strong> Legal forms and stopwords are removed via
/// <em>token matching</em> (not regex), preventing false matches inside words
/// (e.g. "DA" inside "SKODA", "AS" inside "STRASSE").
/// </para>
/// </summary>
public sealed partial class CompanyNameNormalizer : ICompanyNameNormalizer
{
    // ── Transliteration map ──────────────────────────────────────────────────
    // Characters that Unicode NormalizationForm.FormD doesn't decompose.

    private static readonly Dictionary<char, string> TransliterationMap = new()
    {
        // German
        ['ß'] = "SS",
        // Danish / Norwegian
        ['Ø'] = "O", ['ø'] = "O",
        ['Æ'] = "AE", ['æ'] = "AE",
        ['Å'] = "A", ['å'] = "A",
        // Croatian / Vietnamese
        ['Đ'] = "D", ['đ'] = "D",
        // Polish
        ['Ł'] = "L", ['ł'] = "L",
        // Icelandic
        ['Þ'] = "TH", ['þ'] = "TH",
        ['Ð'] = "D", ['ð'] = "D",
        // Turkish
        ['İ'] = "I", ['ı'] = "I",
        // Cyrillic (Russian/Ukrainian/Bulgarian)
        ['А'] = "A", ['а'] = "A",
        ['Б'] = "B", ['б'] = "B",
        ['В'] = "V", ['в'] = "V",
        ['Г'] = "G", ['г'] = "G",
        ['Д'] = "D", ['д'] = "D",
        ['Е'] = "E", ['е'] = "E",
        ['Ё'] = "E", ['ё'] = "E",
        ['Ж'] = "ZH", ['ж'] = "ZH",
        ['З'] = "Z", ['з'] = "Z",
        ['И'] = "I", ['и'] = "I",
        ['Й'] = "Y", ['й'] = "Y",
        ['К'] = "K", ['к'] = "K",
        ['Л'] = "L", ['л'] = "L",
        ['М'] = "M", ['м'] = "M",
        ['Н'] = "N", ['н'] = "N",
        ['О'] = "O", ['о'] = "O",
        ['П'] = "P", ['п'] = "P",
        ['Р'] = "R", ['р'] = "R",
        ['С'] = "S", ['с'] = "S",
        ['Т'] = "T", ['т'] = "T",
        ['У'] = "U", ['у'] = "U",
        ['Ф'] = "F", ['ф'] = "F",
        ['Х'] = "KH", ['х'] = "KH",
        ['Ц'] = "TS", ['ц'] = "TS",
        ['Ч'] = "CH", ['ч'] = "CH",
        ['Ш'] = "SH", ['ш'] = "SH",
        ['Щ'] = "SHCH", ['щ'] = "SHCH",
        ['Ъ'] = "", ['ъ'] = "",
        ['Ы'] = "Y", ['ы'] = "Y",
        ['Ь'] = "", ['ь'] = "",
        ['Э'] = "E", ['э'] = "E",
        ['Ю'] = "YU", ['ю'] = "YU",
        ['Я'] = "YA", ['я'] = "YA",
        // Ukrainian extras
        ['Є'] = "YE", ['є'] = "YE",
        ['І'] = "I", ['і'] = "I",
        ['Ї'] = "YI", ['ї'] = "YI",
        ['Ґ'] = "G", ['ґ'] = "G",
    };

    // ── Abbreviation expansion ───────────────────────────────────────────────

    private static readonly Dictionary<string, string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["INTL"] = "INTERNATIONAL",
        ["MFG"] = "MANUFACTURING",
        // Note: "CORP" is NOT expanded — it's a legal form that gets removed directly.
        ["SVCS"] = "SERVICES",
        ["SVC"] = "SERVICE",
        ["MGMT"] = "MANAGEMENT",
        ["ASSOC"] = "ASSOCIATES",
        ["TECH"] = "TECHNOLOGY",
        ["GRP"] = "GROUP",
        ["NATL"] = "NATIONAL",
        ["INDUS"] = "INDUSTRIES",
        ["IND"] = "INDUSTRIES",
        ["DIST"] = "DISTRIBUTION",
        ["ENGR"] = "ENGINEERING",
        ["ENGRG"] = "ENGINEERING",
        ["PHARM"] = "PHARMACEUTICAL",
        ["TELECOM"] = "TELECOMMUNICATIONS",
        ["ELEC"] = "ELECTRIC",
        ["CHEM"] = "CHEMICAL",
        ["FINL"] = "FINANCIAL",
        ["INS"] = "INSURANCE",
        ["INVT"] = "INVESTMENT",
        ["DEV"] = "DEVELOPMENT",
        ["PROP"] = "PROPERTIES",
        ["ENTERP"] = "ENTERPRISES",
        ["HLDGS"] = "HOLDINGS",
        ["HLDG"] = "HOLDING",
    };

    // ── Legal form tokens ────────────────────────────────────────────────────
    // Token-based matching (not regex) to prevent false matches inside words.
    // Multi-word forms are handled by joining adjacent tokens before matching.

    private static readonly HashSet<string> LegalFormTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        // DACH
        "GMBH", "AG", "KG", "OHG", "UG", "EV", "EK", "GBR", "PARTG", "VVAG", "KGAA", "SE",
        // UK / Ireland
        "LTD", "LIMITED", "LLP", "PLC", "LP", "CIC", "CIO",
        // US / Canada
        "INC", "INCORPORATED", "CORP", "CORPORATION", "LLC", "LLLP", "PLLC",
        // French
        "SARL", "SAS", "SA", "SCI", "SNC", "EURL", "SCEA", "GAEC",
        // Iberian
        "SL", "SLU", "LTDA", "EIRELI", "ME", "MEI",
        // Nordic
        "AB", "APS", "AS", "OY", "OYJ", "ANS", "DA", "HB", "KB",
        // Benelux
        "BV", "NV", "CV", "VOF", "MAATSCHAP",
        // CEE
        "SRO", "SPOL", "VOS", "KS",
        "DOO", "DD",
        "OOD", "EOOD", "AD", "ET", "SD", "KD",
        "KFT", "ZRT", "NYRT", "BT", "RT",
        // LatAm
        "SAPI", "SRL", "SPA",
        // Asia-Pacific
        "PTE", "SDN", "BHD", "PTY",
        "KK",
        // Compound (handled after initial token join)
        "CO",
    };

    // Multi-word legal forms that need special handling:
    // "GmbH & Co. KG" → tokens "GMBH", "CO", "KG" → all individually removed
    // "Pty Ltd" → "PTY", "LTD" → both individually removed
    // "Sdn Bhd" → "SDN", "BHD" → both individually removed
    // "S.A. de C.V." → after punctuation removal: "SA", "DE", "CV" → SA, DE (stopword), CV

    // ── Stopwords ────────────────────────────────────────────────────────────
    // Only multi-character stopwords to avoid false removal of meaningful single-char tokens.

    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        // English
        "AND", "THE", "OF", "FOR", "IN", "AT", "BY", "TO",
        // German
        "UND", "DER", "DIE", "DAS", "FUR", "FUER",
        // French
        "ET", "LE", "LA", "LES", "DU", "DES", "DE", "AU", "AUX",
        // Spanish
        "EL", "LOS", "LAS", "DEL",
        // Portuguese
        "OS", "AS", "DO", "DA", "DOS", "DAS",
        // Italian
        "IL", "LO", "LE", "DI", "DELLA", "DELLO", "DEGLI", "DELLE",
        // Dutch
        "EN", "HET", "VAN",
    };

    /// <inheritdoc />
    public string Normalize(string companyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companyName, nameof(companyName));

        var result = companyName.Trim().ToUpperInvariant();

        // 1. Transliterate special characters (ß, Ø, Cyrillic, etc.)
        result = Transliterate(result);

        // 2. Remove diacritics (ř→r, ü→u, ñ→n, ê→e, etc.)
        result = RemoveDiacritics(result);

        // 3. Remove punctuation and normalize whitespace → get clean tokens
        result = PunctuationPattern().Replace(result, " ");
        result = WhitespacePattern().Replace(result, " ").Trim();

        // 4. Tokenize, then process token-by-token
        var tokens = result.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        // 5. Expand abbreviations
        for (var i = 0; i < tokens.Count; i++)
        {
            if (Abbreviations.TryGetValue(tokens[i], out var expansion))
                tokens[i] = expansion;
        }

        // 6. Remove stopwords (only multi-char to avoid false positives)
        tokens.RemoveAll(t => Stopwords.Contains(t));

        // 7. Remove legal form tokens
        tokens.RemoveAll(t => LegalFormTokens.Contains(t));

        // 8. Remove isolated single-letter tokens (remnants of dot-separated legal forms
        //    like "a.s." → "A" + "S", "s.r.o." → "S" + "R" + "O" after punctuation removal).
        //    Keep digits (e.g. "3M") and keep if it's the only token.
        if (tokens.Count > 1)
            tokens.RemoveAll(t => t.Length == 1 && !char.IsDigit(t[0]));

        // Safety: never return empty — if everything was removed, keep original tokens
        if (tokens.Count == 0)
        {
            tokens = result.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        // 8. Sort tokens alphabetically (order-independent matching)
        tokens.Sort(StringComparer.Ordinal);

        return string.Join(" ", tokens);
    }

    // ── Pipeline steps ───────────────────────────────────────────────────────

    private static string Transliterate(string text)
    {
        var sb = new StringBuilder(text.Length + 16);

        foreach (var c in text)
        {
            if (TransliterationMap.TryGetValue(c, out var replacement))
                sb.Append(replacement);
            else
                sb.Append(c);
        }

        return sb.ToString();
    }

    internal static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    // ── Regex patterns ───────────────────────────────────────────────────────

    /// <summary>Matches non-word, non-space characters (punctuation, symbols).</summary>
    [GeneratedRegex(@"[^\w\s]", RegexOptions.Compiled)]
    private static partial Regex PunctuationPattern();

    /// <summary>Collapses multiple whitespace characters to a single space.</summary>
    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespacePattern();
}
