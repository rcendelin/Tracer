using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Tracer.Infrastructure.HealthChecks;

/// <summary>
/// Probes the distributed cache by writing and reading back a transient key.
/// Returns <see cref="HealthCheckResult.Degraded(string,Exception?,IReadOnlyDictionary{string,object}?)"/>
/// on failure (never <c>Unhealthy</c>) — the cache is an optimisation, its
/// absence must not take the API down or trigger Azure auto-restarts.
/// </summary>
/// <remarks>
/// Registered conditionally only when <see cref="Caching.CacheProvider.Redis"/>
/// is selected. The probe key uses a short TTL so it is harmless if a
/// failover leaves a stale entry. The probe is intentionally I/O-only —
/// no <c>StackExchange.Redis</c>-specific APIs are used so the implementation
/// stays decoupled from the underlying client.
/// </remarks>
internal sealed class RedisHealthCheck : IHealthCheck
{
    private const string ProbeKeyPrefix = "health:probe:";
    private static readonly DistributedCacheEntryOptions ProbeEntry = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(5),
    };
    private static readonly byte[] ProbePayload = Encoding.UTF8.GetBytes("ok");

    private readonly IDistributedCache _cache;

    public RedisHealthCheck(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var probeKey = ProbeKeyPrefix + Guid.NewGuid().ToString("N");

        try
        {
            await _cache.SetAsync(probeKey, ProbePayload, ProbeEntry, cancellationToken)
                .ConfigureAwait(false);

            var roundTrip = await _cache.GetAsync(probeKey, cancellationToken)
                .ConfigureAwait(false);

            if (roundTrip is null || roundTrip.Length == 0)
            {
                return HealthCheckResult.Degraded("Redis probe write succeeded but read returned no data");
            }

            // Best-effort cleanup; ignore failures because the entry expires anyway.
            try
            {
                await _cache.RemoveAsync(probeKey, cancellationToken).ConfigureAwait(false);
            }
            #pragma warning disable CA1031 // Cleanup must not affect health verdict
            catch
            {
                // Intentionally swallowed — TTL handles eventual cleanup.
            }
            #pragma warning restore CA1031

            return HealthCheckResult.Healthy("Redis probe round-trip OK");
        }
        #pragma warning disable CA1031 // Health check must not throw — exceptions are mapped to Degraded
        catch (Exception ex)
        {
            // CWE-209: surface the exception type only, never ex.Message — Redis
            // connection strings can include passwords that StackExchange.Redis
            // sometimes echoes in inner-exception messages.
            return HealthCheckResult.Degraded(
                description: $"Redis probe failed ({ex.GetType().Name})");
        }
        #pragma warning restore CA1031
    }
}
