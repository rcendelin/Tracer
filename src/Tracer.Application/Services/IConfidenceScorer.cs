using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.ValueObjects;

namespace Tracer.Application.Services;

/// <summary>
/// Scores confidence for enriched fields using a multi-factor formula.
/// </summary>
public interface IConfidenceScorer
{
    /// <summary>
    /// Selects the best value per field from multiple provider results and scores confidence.
    /// </summary>
    /// <param name="candidates">Field name → list of candidate TracedFields from different providers.</param>
    /// <returns>Field name → best TracedField with computed confidence.</returns>
    IReadOnlyDictionary<FieldName, TracedField<object>> ScoreFields(
        IReadOnlyDictionary<FieldName, IReadOnlyCollection<TracedField<object>>> candidates);

    /// <summary>
    /// Computes the overall confidence for a company profile as a weighted average.
    /// </summary>
    Confidence ScoreOverall(CompanyProfile profile);
}
