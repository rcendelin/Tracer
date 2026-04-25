using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Tracer.Infrastructure.Persistence;

namespace Tracer.Infrastructure.HealthChecks;

/// <summary>
/// Verifies that the database is reachable by running a minimal query via EF Core.
/// Kept in Infrastructure so TracerDbContext (internal) stays encapsulated.
/// Uses IServiceScopeFactory to avoid captive dependency — health checks are
/// registered as Singleton but TracerDbContext is Scoped.
/// </summary>
internal sealed class DatabaseHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            #pragma warning disable CA2007 // AsyncServiceScope.DisposeAsync — ConfigureAwait wraps the disposable and loses ServiceProvider
            await using var scope = scopeFactory.CreateAsyncScope();
            #pragma warning restore CA2007
            var db = scope.ServiceProvider.GetRequiredService<TracerDbContext>();

            var canConnect = await db.Database.CanConnectAsync(cancellationToken)
                .ConfigureAwait(false);

            return canConnect
                ? HealthCheckResult.Healthy("Database connection OK")
                : HealthCheckResult.Unhealthy("Cannot connect to database");
        }
        #pragma warning disable CA1031 // Health check must not throw — returns HealthCheckResult on error
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database health check failed", ex);
        }
        #pragma warning restore CA1031
    }
}
