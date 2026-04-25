using BenchmarkDotNet.Attributes;
using Tracer.Application.Services;
using Tracer.Benchmarks.Fixtures;

namespace Tracer.Benchmarks.Benchmarks;

/// <summary>
/// Measures <see cref="IFuzzyNameMatcher.Score"/> against representative
/// normalized name pairs. Each invocation executes both Jaro-Winkler and
/// Token Jaccard passes; throughput and alloc per call are the primary SLOs.
/// </summary>
[MemoryDiagnoser]
public class FuzzyMatcherBenchmarks
{
    private readonly IFuzzyNameMatcher _matcher = new FuzzyNameMatcher();
    private readonly (string Left, string Right)[] _pairs = SampleData.FuzzyPairs;

    [Benchmark]
    public double ScoreAllPairs()
    {
        double sum = 0;
        foreach (var (left, right) in _pairs)
            sum += _matcher.Score(left, right);
        return sum;
    }
}
