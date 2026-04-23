using Tracer.Domain.Entities;

namespace Tracer.Application.Services;

/// <summary>
/// No-op implementation of <see cref="IRevalidationRunner"/>. Always returns
/// <see cref="RevalidationOutcome.Deferred"/>. Retained as a placeholder
/// double for unit tests that exercise <c>RevalidationScheduler</c> without
/// standing up the full deep-mode dependency graph. Production wiring uses
/// <see cref="DeepRevalidationRunner"/> (B-67).
/// </summary>
internal sealed class NoOpRevalidationRunner : IRevalidationRunner
{
    public Task<RevalidationOutcome> RunAsync(CompanyProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(RevalidationOutcome.Deferred);
    }
}
