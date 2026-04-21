using Tracer.Domain.Entities;

namespace Tracer.Application.Services;

/// <summary>
/// Placeholder implementation of <see cref="IRevalidationRunner"/> used
/// until the lightweight (B-66) and deep (B-67) pipelines are wired up.
/// Always returns <see cref="RevalidationOutcome.Deferred"/>.
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
