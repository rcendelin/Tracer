using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;

namespace Tracer.Application.Services;

/// <summary>
/// Merges provider results into a golden record.
/// Conflict resolution: highest confidence → highest source quality → most recent enrichment.
/// </summary>
public sealed class GoldenRecordMerger : IGoldenRecordMerger
{
    private readonly IConfidenceScorer _scorer;

    public GoldenRecordMerger(IConfidenceScorer scorer)
    {
        _scorer = scorer;
    }

    public MergeResult Merge(IReadOnlyCollection<ProviderMergeInput> providerResults)
    {
        ArgumentNullException.ThrowIfNull(providerResults);

        // 1. Collect all candidate values per field from all providers
        var candidatesByField = CollectCandidates(providerResults);

        // 2. Use ConfidenceScorer to select best and score
        var scoredFields = _scorer.ScoreFields(candidatesByField);

        return new MergeResult
        {
            BestFields = scoredFields,
            CandidateValues = candidatesByField,
        };
    }

    private static Dictionary<FieldName, IReadOnlyCollection<TracedField<object>>> CollectCandidates(
        IReadOnlyCollection<ProviderMergeInput> providerResults)
    {
        var candidates = new Dictionary<FieldName, List<TracedField<object>>>();

        foreach (var input in providerResults)
        {
            if (!input.Result.Found)
                continue;

            foreach (var (fieldName, value) in input.Result.Fields)
            {
                if (value is null)
                    continue;

                if (!candidates.TryGetValue(fieldName, out var list))
                {
                    list = [];
                    candidates[fieldName] = list;
                }

                list.Add(new TracedField<object>
                {
                    Value = value,
                    Confidence = Confidence.Create(input.SourceQuality),
                    Source = input.ProviderId,
                    EnrichedAt = DateTimeOffset.UtcNow,
                });
            }
        }

        // Convert to IReadOnlyCollection
        return candidates.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyCollection<TracedField<object>>)kvp.Value.AsReadOnly());
    }
}
