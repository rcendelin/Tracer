using BenchmarkDotNet.Attributes;
using Tracer.Application.Services;
using Tracer.Benchmarks.Fixtures;

namespace Tracer.Benchmarks.Benchmarks;

/// <summary>
/// Measures <see cref="IGoldenRecordMerger.Merge"/> end-to-end: candidate
/// collection, per-field scoring delegation to <see cref="IConfidenceScorer"/>,
/// and result materialisation. Matches the real Tier 1 fan-in shape
/// (four providers, 13 distinct fields across them).
/// </summary>
[MemoryDiagnoser]
public class GoldenRecordMergerBenchmarks
{
    private readonly IGoldenRecordMerger _merger;
    private readonly IReadOnlyList<ProviderMergeInput> _inputs;

    public GoldenRecordMergerBenchmarks()
    {
        _merger = new GoldenRecordMerger(new ConfidenceScorer());
        _inputs = SampleData.BuildProviderResults()
            .Select(x => new ProviderMergeInput
            {
                ProviderId = x.ProviderId,
                SourceQuality = x.SourceQuality,
                Result = x.Result,
            })
            .ToList();
    }

    [Benchmark]
    public MergeResult Merge() => _merger.Merge(_inputs);
}
