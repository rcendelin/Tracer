using BenchmarkDotNet.Attributes;
using Tracer.Application.Services;
using Tracer.Benchmarks.Fixtures;

namespace Tracer.Benchmarks.Benchmarks;

/// <summary>
/// Measures <see cref="ICompanyNameNormalizer.Normalize"/> — a hot-path service
/// invoked at least once per trace context (entity resolution) and N times per
/// fuzzy candidate scoring pass. Sensitive to allocations because the pipeline
/// performs several string transformations.
/// </summary>
[MemoryDiagnoser]
public class NameNormalizerBenchmarks
{
    private readonly ICompanyNameNormalizer _normalizer = new CompanyNameNormalizer();

    [ParamsSource(nameof(Names))]
    public string Name { get; set; } = string.Empty;

    public static IEnumerable<string> Names => SampleData.CompanyNames;

    [Benchmark]
    public string Normalize() => _normalizer.Normalize(Name);
}
