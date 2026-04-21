using Tracer.Domain.Entities;

namespace Tracer.Application.Services;

/// <summary>
/// Executes the actual re-validation logic against a single
/// <see cref="CompanyProfile"/>. Implementations are responsible for
/// deciding between lightweight (single-provider TTL refresh, B-66)
/// and deep (full waterfall re-enrichment, B-67) strategies.
/// </summary>
/// <remarks>
/// In B-65 this interface is registered as <see cref="NoOpRevalidationRunner"/>
/// which simply returns <see cref="RevalidationOutcome.Deferred"/>. The
/// <see cref="Tracer.Infrastructure.BackgroundJobs.RevalidationScheduler"/>
/// depends only on this abstraction so the lightweight / deep pipelines
/// can be introduced in later blocks without changing the scheduler.
/// </remarks>
public interface IRevalidationRunner
{
    /// <summary>
    /// Runs a re-validation pass against <paramref name="profile"/>.
    /// Implementations must NOT call <c>SaveChangesAsync</c> themselves;
    /// persistence is coordinated by the caller (scheduler) via
    /// <see cref="Tracer.Domain.Interfaces.IUnitOfWork"/>.
    /// </summary>
    Task<RevalidationOutcome> RunAsync(CompanyProfile profile, CancellationToken cancellationToken);
}
