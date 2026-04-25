using BenchmarkDotNet.Attributes;
using Tracer.Application.Services;
using Tracer.Benchmarks.Fixtures;
using Tracer.Domain.Enums;
using Tracer.Domain.ValueObjects;

namespace Tracer.Benchmarks.Benchmarks;

/// <summary>
/// Measures <see cref="IConfidenceScorer.ScoreFields"/> — the multi-factor
/// scoring engine that runs exactly once per trace context, folding every
/// provider candidate into the chosen value per field. Input size:
/// 8 fields × 3 candidates.
/// </summary>
[MemoryDiagnoser]
public class ConfidenceScorerBenchmarks
{
    private readonly IConfidenceScorer _scorer = new ConfidenceScorer();
    private readonly IReadOnlyDictionary<FieldName, IReadOnlyCollection<TracedField<object>>> _candidates =
        SampleData.BuildScoringCandidates();

    [Benchmark]
    public IReadOnlyDictionary<FieldName, TracedField<object>> ScoreFields()
        => _scorer.ScoreFields(_candidates);
}
