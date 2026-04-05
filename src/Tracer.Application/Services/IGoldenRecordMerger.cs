using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;

namespace Tracer.Application.Services;

/// <summary>
/// Merges provider results into a single golden record by selecting the best value per field.
/// </summary>
public interface IGoldenRecordMerger
{
    /// <summary>
    /// Merges multiple provider results into a single set of best-value fields.
    /// </summary>
    /// <param name="providerResults">Provider results with their provider IDs and source qualities.</param>
    /// <returns>The merge result containing best fields and all candidate values for audit.</returns>
    MergeResult Merge(IReadOnlyCollection<ProviderMergeInput> providerResults);
}

/// <summary>
/// Input for the merger from a single provider.
/// </summary>
public sealed record ProviderMergeInput
{
    public required string ProviderId { get; init; }
    public required double SourceQuality { get; init; }
    public required ProviderResult Result { get; init; }
}

/// <summary>
/// Output of the merge operation.
/// </summary>
public sealed record MergeResult
{
    /// <summary>Best value per field — ready to write to CompanyProfile.</summary>
    public required IReadOnlyDictionary<FieldName, TracedField<object>> BestFields { get; init; }

    /// <summary>All candidate values per field — for audit and debugging.</summary>
    public required IReadOnlyDictionary<FieldName, IReadOnlyCollection<TracedField<object>>> CandidateValues { get; init; }
}
