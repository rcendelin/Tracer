using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;

namespace Tracer.Benchmarks.Fixtures;

/// <summary>
/// Fixed, deterministic sample data shared by all benchmark classes.
/// Values are representative of real-world enrichment scenarios across
/// multiple countries and character sets, but contain no PII.
/// </summary>
internal static class SampleData
{
    /// <summary>
    /// Eight company names covering the normalizer's hot code paths:
    /// ASCII legal forms, diacritics, cyrillic transliteration, umlauts,
    /// abbreviations, and multi-token legal form removal.
    /// </summary>
    public static readonly string[] CompanyNames =
    [
        "Contoso International Corp.",
        "Škoda Auto a.s.",
        "Företag AB",
        "Société Générale S.A.",
        "Deutsche Bank AG",
        "ООО Рога и Копыта",
        "BHP Group Limited",
        "Acme Intl. Holdings Ltd.",
    ];

    /// <summary>
    /// Pairs of (left, right) names for fuzzy scoring. Inputs follow the
    /// matcher's contract: pre-normalized (uppercase, legal forms removed,
    /// tokens sorted). Measures the matcher itself in isolation.
    /// </summary>
    public static readonly (string Left, string Right)[] FuzzyPairs =
    [
        ("CONTOSO INTERNATIONAL", "CONTOSO INTERNATIONAL"),
        ("CONTOSO INTERNATIONAL", "CONTOSO INTL"),
        ("BANCO SANTANDER", "SANTANDER BANCO"),
        ("MICROSOFT", "MIKROSOFT"),
        ("ACME HOLDINGS", "ACME GLOBAL HOLDINGS"),
        ("DEUTSCHE BANK", "DEUTSCHE POST"),
        ("BHP GROUP", "RIO TINTO"),
        ("FORETAG", "FORETAG INTERNATIONAL"),
        ("SKODA AUTO", "SKODA"),
        ("SOCIETE GENERALE", "GENERALE SOCIETE"),
    ];

    /// <summary>
    /// Builds four synthetic provider results that approximate the output
    /// of Tier 1 registry providers for a single trace context. Used by
    /// <c>GoldenRecordMergerBenchmarks</c>.
    /// </summary>
    public static IReadOnlyList<(string ProviderId, double SourceQuality, ProviderResult Result)> BuildProviderResults()
    {
        var aresAddress = new Address
        {
            Street = "Main 1",
            City = "Prague",
            PostalCode = "11000",
            Country = "CZ",
        };

        var azureAddress = new Address
        {
            Street = "Main 1",
            City = "Praha",
            PostalCode = "110 00",
            Country = "CZ",
        };

        var location = GeoCoordinate.Create(50.08, 14.44);

        var aresFields = new Dictionary<FieldName, object?>
        {
            [FieldName.LegalName] = "Contoso a.s.",
            [FieldName.RegistrationId] = "00177041",
            [FieldName.LegalForm] = "a.s.",
            [FieldName.RegisteredAddress] = aresAddress,
            [FieldName.EntityStatus] = "active",
        };

        var gleifFields = new Dictionary<FieldName, object?>
        {
            [FieldName.LegalName] = "Contoso a.s.",
            [FieldName.RegistrationId] = "00177041",
            [FieldName.Industry] = "6499",
            [FieldName.ParentCompany] = "Contoso Holdings Ltd",
        };

        var googleFields = new Dictionary<FieldName, object?>
        {
            [FieldName.OperatingAddress] = aresAddress,
            [FieldName.Phone] = "+420 000 000 000",
            [FieldName.Website] = "https://example.invalid",
            [FieldName.Location] = location,
        };

        var azureMapsFields = new Dictionary<FieldName, object?>
        {
            [FieldName.Location] = location,
            [FieldName.OperatingAddress] = azureAddress,
        };

        return
        [
            ("ares", 0.95, ProviderResult.Success(aresFields, TimeSpan.FromMilliseconds(120))),
            ("gleif-lei", 0.90, ProviderResult.Success(gleifFields, TimeSpan.FromMilliseconds(150))),
            ("google-maps", 0.80, ProviderResult.Success(googleFields, TimeSpan.FromMilliseconds(200))),
            ("azure-maps", 0.75, ProviderResult.Success(azureMapsFields, TimeSpan.FromMilliseconds(180))),
        ];
    }

    /// <summary>
    /// Synthetic per-field candidate set for <c>IConfidenceScorer.ScoreFields</c>.
    /// Covers eight fields and three candidates per field — representative
    /// of a post-merge state where multiple providers contributed to the same field.
    /// </summary>
    public static IReadOnlyDictionary<FieldName, IReadOnlyCollection<TracedField<object>>> BuildScoringCandidates()
    {
        var now = new DateTimeOffset(2026, 04, 22, 12, 0, 0, TimeSpan.Zero);

        static TracedField<object> Field(object value, double confidence, string source, DateTimeOffset enrichedAt) =>
            new()
            {
                Value = value,
                Confidence = Confidence.Create(confidence),
                Source = source,
                EnrichedAt = enrichedAt,
            };

        return new Dictionary<FieldName, IReadOnlyCollection<TracedField<object>>>
        {
            [FieldName.LegalName] =
            [
                Field("Contoso a.s.", 0.95, "ares", now),
                Field("Contoso A.S.", 0.85, "gleif-lei", now.AddMinutes(-5)),
                Field("Contoso", 0.60, "web-scraper", now.AddMinutes(-30)),
            ],
            [FieldName.RegistrationId] =
            [
                Field("00177041", 0.99, "ares", now),
                Field("00177041", 0.90, "gleif-lei", now),
                Field("177041", 0.50, "ai-extractor", now.AddMinutes(-60)),
            ],
            [FieldName.Phone] =
            [
                Field("+420 000 000 000", 0.80, "google-maps", now),
                Field("420000000000", 0.70, "web-scraper", now.AddMinutes(-60)),
                Field("+420 000 000 001", 0.60, "ai-extractor", now.AddMinutes(-120)),
            ],
            [FieldName.Email] =
            [
                Field("info@example.invalid", 0.70, "web-scraper", now),
                Field("info@example.invalid", 0.65, "ai-extractor", now.AddMinutes(-90)),
                Field("contact@example.invalid", 0.50, "ai-extractor", now.AddMinutes(-120)),
            ],
            [FieldName.Website] =
            [
                Field("https://example.invalid", 0.90, "google-maps", now),
                Field("example.invalid", 0.70, "web-scraper", now.AddMinutes(-10)),
                Field("https://www.example.invalid", 0.80, "ares", now.AddMinutes(-5)),
            ],
            [FieldName.Industry] =
            [
                Field("6499", 0.85, "gleif-lei", now),
                Field("Banking", 0.60, "ai-extractor", now.AddMinutes(-30)),
                Field("Finance", 0.50, "web-scraper", now.AddMinutes(-60)),
            ],
            [FieldName.EntityStatus] =
            [
                Field("active", 0.95, "ares", now),
                Field("active", 0.90, "companies-house", now),
                Field("Active", 0.60, "web-scraper", now.AddMinutes(-30)),
            ],
            [FieldName.EmployeeRange] =
            [
                Field("50-200", 0.70, "ai-extractor", now),
                Field("100-250", 0.55, "web-scraper", now.AddMinutes(-30)),
                Field("50-249", 0.65, "ai-extractor", now.AddMinutes(-60)),
            ],
        };
    }
}
