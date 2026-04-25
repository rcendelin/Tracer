using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracer.Infrastructure.Caching;

/// <summary>
/// Registers the <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>
/// backing store driven by <see cref="CacheOptions.Provider"/>. In-process memory is the
/// default so dev / CI setups work without configuration; <see cref="CacheProvider.Redis"/>
/// is opt-in via <c>Cache:Provider = Redis</c> and requires <c>ConnectionStrings:Redis</c>.
/// </summary>
/// <remarks>
/// Kept as an internal extension so the public <c>AddInfrastructure</c> contract stays
/// minimal and the branching logic is testable in isolation. The method is idempotent —
/// calling it twice is harmless, but the options validator only runs once.
/// </remarks>
internal static class DistributedCacheRegistration
{
    public static IServiceCollection AddTracerDistributedCache(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Bind + validate options at boot. A Redis misconfiguration must surface as
        // OptionsValidationException on startup, not as a NullReferenceException on
        // the first cache hit.
        services.AddOptions<CacheOptions>()
            .Bind(configuration.GetSection(CacheOptions.SectionName))
            .Validate(
                o => o.ProfileTtl > TimeSpan.Zero,
                "Cache:ProfileTtl must be a strictly positive TimeSpan.")
            .Validate(
                o => o.Warming.MaxProfiles is >= 1 and <= 10_000,
                "Cache:Warming:MaxProfiles must be between 1 and 10000.")
            .Validate(
                o => o.Warming.DelayOnStartup >= TimeSpan.Zero,
                "Cache:Warming:DelayOnStartup must be non-negative.")
            .Validate(
                o => o.Provider != CacheProvider.Redis ||
                     !string.IsNullOrWhiteSpace(configuration.GetConnectionString("Redis")),
                "ConnectionStrings:Redis is required when Cache:Provider = Redis.")
            .ValidateOnStart();

        // Resolve the effective provider once at registration time. We deliberately
        // do NOT resolve IOptions<CacheOptions> here — options are not yet bound —
        // so we read raw configuration and trust ValidateOnStart to catch drift.
        var provider = CacheOptions.ResolveProvider(configuration);

        if (provider == CacheProvider.Redis)
        {
            var connectionString = configuration.GetConnectionString("Redis")
                ?? throw new InvalidOperationException(
                    "ConnectionStrings:Redis is required when Cache:Provider = Redis.");
            var instanceName = configuration[$"{CacheOptions.SectionName}:RedisInstanceName"]
                ?? "tracer:";

            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = connectionString;
                options.InstanceName = instanceName;
            });
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        return services;
    }
}
