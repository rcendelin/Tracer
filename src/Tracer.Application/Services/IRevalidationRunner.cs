using Tracer.Domain.Entities;

namespace Tracer.Application.Services;

/// <summary>
/// Executes the actual re-validation logic against a single
/// <see cref="CompanyProfile"/>. Implementations are responsible for
/// deciding between lightweight (single-provider TTL refresh, B-66)
/// and deep (full waterfall re-enrichment, B-67) strategies.
/// </summary>
/// <remarks>
/// Production wiring (B-67) uses <see cref="DeepRevalidationRunner"/>.
/// <see cref="NoOpRevalidationRunner"/> is retained as a test double.
/// The <see cref="Tracer.Infrastructure.BackgroundJobs.RevalidationScheduler"/>
/// depends only on this abstraction so the lightweight / composite runners
/// can be introduced in later blocks without changing the scheduler.
/// </remarks>
public interface IRevalidationRunner
{
    /// <summary>
    /// Runs a re-validation pass against <paramref name="profile"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lightweight implementations (B-66) should only mutate the profile
    /// in memory and leave persistence to the caller — the scheduler
    /// saves changes after this method returns.
    /// </para>
    /// <para>
    /// Deep implementations (B-67) reuse <see cref="IWaterfallOrchestrator"/>
    /// which persists the profile internally; such runners therefore own
    /// their own save boundaries for audit entities
    /// (<see cref="Tracer.Domain.Entities.ValidationRecord"/>,
    /// <see cref="Tracer.Domain.Entities.TraceRequest"/>).
    /// </para>
    /// </remarks>
    Task<RevalidationOutcome> RunAsync(CompanyProfile profile, CancellationToken cancellationToken);
}
