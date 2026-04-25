using System.Diagnostics;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;

namespace Tracer.Infrastructure.Tests.Integration.Fakes;

/// <summary>
/// Configurable <see cref="IEnrichmentProvider"/> used to drive the waterfall in E2E tests
/// without touching real HTTP / Azure OpenAI / databases.
/// </summary>
/// <remarks>
/// Priority determines tier placement:
/// <list type="bullet">
/// <item>≤ 100 → Tier 1 (parallel fan-out, per-provider 8 s timeout).</item>
/// <item>101–200 → Tier 2 (sequential, 12 s timeout, Standard+ depth).</item>
/// <item>&gt; 200 → Tier 3 (sequential, 20 s timeout, Deep depth only).</item>
/// </list>
/// An optional <see cref="Delay"/> lets tests verify depth budget / per-provider timeout behaviour.
/// </remarks>
internal sealed class FakeEnrichmentProvider : IEnrichmentProvider
{
    /// <summary>Outcome mode for <see cref="EnrichAsync"/>.</summary>
    public enum Outcome
    {
        /// <summary>Return <see cref="ProviderResult.Success"/> with the configured fields.</summary>
        Success,
        /// <summary>Return <see cref="ProviderResult.NotFound"/>.</summary>
        NotFound,
        /// <summary>Return <see cref="ProviderResult.Error"/> with a generic message.</summary>
        Error,
        /// <summary>Throw an unexpected exception (simulates provider bug).</summary>
        Throw,
    }

    private int _invocations;

    public required string ProviderId { get; init; }

    public required int Priority { get; init; }

    public double SourceQuality { get; init; } = 0.9;

    public Outcome Mode { get; init; } = Outcome.Success;

    public IReadOnlyDictionary<FieldName, object?> Fields { get; init; }
        = new Dictionary<FieldName, object?>();

    /// <summary>
    /// Optional artificial delay before returning. Use to exercise per-provider timeouts
    /// (Tier1=8 s, Tier2=12 s, Tier3=20 s) or depth budgets (Quick=5 s, Standard=15 s, Deep=30 s).
    /// </summary>
    public TimeSpan Delay { get; init; } = TimeSpan.Zero;

    /// <summary>Predicate for <see cref="CanHandle"/>. Defaults to always-true.</summary>
    public Func<TraceContext, bool> CanHandlePredicate { get; init; } = _ => true;

    /// <summary>Number of times <see cref="EnrichAsync"/> was invoked. Safe for cross-thread read.</summary>
    public int Invocations => Volatile.Read(ref _invocations);

    public bool CanHandle(TraceContext context) => CanHandlePredicate(context);

    public async Task<ProviderResult> EnrichAsync(TraceContext context, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _invocations);

        var stopwatch = Stopwatch.StartNew();

        if (Delay > TimeSpan.Zero)
            await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);

        stopwatch.Stop();

        return Mode switch
        {
            Outcome.Success => ProviderResult.Success(Fields, stopwatch.Elapsed),
            Outcome.NotFound => ProviderResult.NotFound(stopwatch.Elapsed),
            Outcome.Error => ProviderResult.Error("fake-provider-error", stopwatch.Elapsed),
            Outcome.Throw => throw new InvalidOperationException("fake-provider-threw"),
            _ => throw new InvalidOperationException($"Unknown outcome: {Mode}"),
        };
    }
}
