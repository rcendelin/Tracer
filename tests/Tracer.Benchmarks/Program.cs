using BenchmarkDotNet.Running;

namespace Tracer.Benchmarks;

// Console entrypoint for BenchmarkDotNet.
// Usage:
//   dotnet run -c Release --project tests/Tracer.Benchmarks -- --filter "*NameNormalizer*"
//   dotnet run -c Release --project tests/Tracer.Benchmarks -- --filter "*" --join
//
// BenchmarkSwitcher auto-discovers all [Benchmark] classes in this assembly.
public static class Program
{
    public static void Main(string[] args)
    {
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
