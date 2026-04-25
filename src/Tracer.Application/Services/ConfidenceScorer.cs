using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.ValueObjects;

namespace Tracer.Application.Services;

/// <summary>
/// Multi-factor confidence scoring engine.
/// Scores each field based on: source quality (30%), freshness (20%),
/// cross-validation (25%), and verification level (25%).
/// </summary>
public sealed class ConfidenceScorer : IConfidenceScorer
{
    // Scoring weights
    private const double SourceQualityWeight = 0.30;
    private const double FreshnessWeight = 0.20;
    private const double CrossValidationWeight = 0.25;
    private const double VerificationWeight = 0.25;

    // Continuous-freshness parameters (B-96).
    // Linear decay: full score at 0 days, hits the floor at FreshnessFullDecayDays.
    private const double FreshnessFullDecayDays = 90.0;
    private const double FreshnessFloor = 0.3;

    // Provider verification tiers (B-96 expanded — between B-96 only registry vs geo).
    // Authoritative national / global registries — highest verification weight.
    private static readonly HashSet<string> Tier1RegistrySources = new(StringComparer.OrdinalIgnoreCase)
    {
        "ares", "companies-house", "abn-lookup", "sec-edgar",
    };

    // Global registry — slightly less granular than per-country registries.
    private static readonly HashSet<string> Tier1GlobalSources = new(StringComparer.OrdinalIgnoreCase)
    {
        "gleif-lei",
    };

    // First-class commercial geo APIs.
    private static readonly HashSet<string> GeoSources = new(StringComparer.OrdinalIgnoreCase)
    {
        "google-maps", "azure-maps",
    };

    // Registry HTML scrapers — public-record data but more fragile than APIs.
    private static readonly HashSet<string> RegistryScraperSources = new(StringComparer.OrdinalIgnoreCase)
    {
        "handelsregister", "state-sos", "cnpj-brazil",
        "latam-afip", "latam-sii", "latam-rues", "latam-sat",
    };

    public IReadOnlyDictionary<FieldName, TracedField<object>> ScoreFields(
        IReadOnlyDictionary<FieldName, IReadOnlyCollection<TracedField<object>>> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var result = new Dictionary<FieldName, TracedField<object>>();

        foreach (var (fieldName, fieldCandidates) in candidates)
        {
            if (fieldCandidates.Count == 0)
                continue;

            var best = SelectBestCandidate(fieldCandidates);
            var confidence = ComputeFieldConfidence(best, fieldCandidates);

            result[fieldName] = best with { Confidence = confidence };
        }

        return result;
    }

    public Confidence ScoreOverall(CompanyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var fieldConfidences = GetFieldConfidences(profile);

        if (fieldConfidences.Count == 0)
            return Confidence.Zero;

        // Weighted average — key fields have higher weight
        var totalWeight = 0.0;
        var weightedSum = 0.0;

        foreach (var (fieldName, confidence) in fieldConfidences)
        {
            var weight = GetFieldWeight(fieldName);
            weightedSum += confidence * weight;
            totalWeight += weight;
        }

        var overall = totalWeight > 0 ? weightedSum / totalWeight : 0.0;
        return Confidence.Create(Math.Clamp(overall, 0.0, 1.0));
    }

    private static TracedField<object> SelectBestCandidate(IReadOnlyCollection<TracedField<object>> candidates)
    {
        // Primary: verification level (official registry > geo > AI).
        // Secondary: provider-reported confidence as tie-breaker.
        return candidates
            .OrderByDescending(c => ComputeVerificationLevel(c.Source))
            .ThenByDescending(c => c.Confidence.Value)
            .First();
    }

    private static Confidence ComputeFieldConfidence(
        TracedField<object> field,
        IReadOnlyCollection<TracedField<object>> allCandidates)
    {
        var sourceQuality = field.Confidence.Value;
        var freshness = ComputeFreshness(field.EnrichedAt);
        var crossValidation = ComputeCrossValidation(allCandidates);
        var verification = ComputeVerificationLevel(field.Source);

        var score =
            sourceQuality * SourceQualityWeight +
            freshness * FreshnessWeight +
            crossValidation * CrossValidationWeight +
            verification * VerificationWeight;

        return Confidence.Create(Math.Clamp(score, 0.0, 1.0));
    }

    /// <summary>
    /// Freshness score based on age of the data, B-96 continuous variant.
    /// Linear decay from <c>1.0</c> at 0 days down to <see cref="FreshnessFloor"/>
    /// at <see cref="FreshnessFullDecayDays"/> days, then flat at the floor.
    /// </summary>
    /// <remarks>
    /// Pre-B-96 implementation was a 4-tier step function (`<7d=1.0`, `<30d=0.8`,
    /// `<90d=0.5`, else `0.3`) — it produced visible jumps in the score across
    /// the boundary days. The continuous formula keeps the same endpoints but
    /// removes the step. See `docs/adr/0006-confidence-scoring.md` for the
    /// derivation and trade-offs.
    /// </remarks>
    private static double ComputeFreshness(DateTimeOffset enrichedAt)
    {
        var ageDays = (DateTimeOffset.UtcNow - enrichedAt).TotalDays;
        if (ageDays <= 0)
            return 1.0;

        var linear = 1.0 - (ageDays / FreshnessFullDecayDays);
        return Math.Max(FreshnessFloor, linear);
    }

    /// <summary>
    /// Cross-validation score based on how many sources agree.
    /// </summary>
    private static double ComputeCrossValidation(IReadOnlyCollection<TracedField<object>> candidates)
    {
        var distinctSources = candidates.Select(c => c.Source).Distinct().Count();

        return distinctSources switch
        {
            >= 3 => 1.0,
            2 => 0.7,
            _ => 0.4,
        };
    }

    /// <summary>
    /// Verification level based on the source type — B-96 N-tier expansion of
    /// the original 3-tier registry / geo / other split. Higher means more
    /// authoritative.
    /// </summary>
    /// <remarks>
    /// Pre-B-96 implementation collapsed Tier 2 scrapers (Handelsregister,
    /// LATAM, State SoS, CNPJ) and the AI extractor into the same `0.4`
    /// bucket. Registry scrapers are public-record data and deserve a
    /// distinct tier above scraping miscellany. See
    /// `docs/adr/0006-confidence-scoring.md` for the rationale; the buckets
    /// match `IEnrichmentProvider.Priority` tiers (B-86 docs).
    /// </remarks>
    private static double ComputeVerificationLevel(string source)
    {
        if (Tier1RegistrySources.Contains(source))
            return 1.0;

        // Manual override is the most authoritative source — operator-set.
        // Source string format is `manual-override:<callerFingerprint>` (B-85).
        if (!string.IsNullOrEmpty(source) &&
            source.StartsWith("manual-override:", StringComparison.OrdinalIgnoreCase))
        {
            return 1.0;
        }

        if (Tier1GlobalSources.Contains(source))
            return 0.95;

        if (RegistryScraperSources.Contains(source))
            return 0.85;

        if (GeoSources.Contains(source))
            return 0.7;

        // AI extraction (`ai-extractor`), web scraper, or any unknown source.
        return 0.4;
    }

    private static List<(FieldName Name, double Confidence)> GetFieldConfidences(
        CompanyProfile profile)
    {
        var results = new List<(FieldName, double)>();

        AddIfPresent(results, FieldName.LegalName, profile.LegalName);
        AddIfPresent(results, FieldName.TradeName, profile.TradeName);
        AddIfPresent(results, FieldName.TaxId, profile.TaxId);
        AddIfPresent(results, FieldName.LegalForm, profile.LegalForm);
        AddIfPresent(results, FieldName.RegisteredAddress, profile.RegisteredAddress);
        AddIfPresent(results, FieldName.OperatingAddress, profile.OperatingAddress);
        AddIfPresent(results, FieldName.Phone, profile.Phone);
        AddIfPresent(results, FieldName.Email, profile.Email);
        AddIfPresent(results, FieldName.Website, profile.Website);
        AddIfPresent(results, FieldName.Industry, profile.Industry);
        AddIfPresent(results, FieldName.EmployeeRange, profile.EmployeeRange);
        AddIfPresent(results, FieldName.EntityStatus, profile.EntityStatus);
        AddIfPresent(results, FieldName.ParentCompany, profile.ParentCompany);
        AddIfPresent(results, FieldName.Location, profile.Location);

        return results;
    }

    private static void AddIfPresent<T>(List<(FieldName, double)> list, FieldName name, TracedField<T>? field)
    {
        if (field is not null)
            list.Add((name, field.Confidence.Value));
    }

    /// <summary>
    /// Returns the weight of a field for overall confidence calculation.
    /// Key business fields have higher weight.
    /// </summary>
    private static double GetFieldWeight(FieldName fieldName) => fieldName switch
    {
        FieldName.LegalName => 3.0,
        // RegistrationId is identity-only (plain string), not a TracedField — no confidence to weight
        FieldName.EntityStatus => 2.5,
        FieldName.RegisteredAddress => 2.0,
        FieldName.TaxId => 2.0,
        FieldName.Phone => 1.5,
        FieldName.Email => 1.5,
        FieldName.Website => 1.0,
        FieldName.Location => 1.0,
        _ => 1.0,
    };
}
