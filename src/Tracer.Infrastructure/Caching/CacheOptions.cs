using System.ComponentModel.DataAnnotations;

namespace Tracer.Infrastructure.Caching;

/// <summary>
/// Bindable cache configuration — supersedes the inline <c>CacheOptions</c>
/// that shipped with B-40. Adds B-79 knobs for the distributed backing store
/// (<see cref="Provider"/>, <see cref="RedisInstanceName"/>) and the startup
/// <see cref="Warming"/> pre-population pass.
/// </summary>
/// <remarks>
/// Registered via <c>services.AddOptions&lt;CacheOptions&gt;().BindConfiguration("Cache")</c>
/// plus <c>ValidateOnStart()</c> so mis-configuration fails at boot, not at first
/// cache resolve. <see cref="CacheProvider.InMemory"/> is the default so existing
/// dev / CI setups continue to work without any configuration.
/// </remarks>
public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    /// <summary>
    /// Backing store for <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>.
    /// </summary>
    public CacheProvider Provider { get; set; } = CacheProvider.InMemory;

    /// <summary>Profile cache TTL. Default: 7 days.</summary>
    public TimeSpan ProfileTtl { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Key prefix applied by <c>Microsoft.Extensions.Caching.StackExchangeRedis</c>.
    /// Prevents collisions when multiple environments share one Redis instance.
    /// Ignored when <see cref="Provider"/> is <see cref="CacheProvider.InMemory"/>.
    /// </summary>
    public string RedisInstanceName { get; set; } = "tracer:";

    /// <summary>
    /// Startup cache-warming configuration.
    /// </summary>
    public CacheWarmingOptions Warming { get; set; } = new();
}

/// <summary>
/// Supported distributed cache backing stores.
/// </summary>
public enum CacheProvider
{
    /// <summary>In-process <see cref="Microsoft.Extensions.Caching.Memory.MemoryDistributedCache"/>. Default.</summary>
    InMemory = 0,

    /// <summary>Azure Cache for Redis via <c>StackExchange.Redis</c>.</summary>
    Redis = 1,
}

/// <summary>
/// Controls the one-off startup cache-warming pass performed by
/// <c>CacheWarmingService</c>. Disabled by default — operators opt in
/// per environment.
/// </summary>
public sealed class CacheWarmingOptions
{
    /// <summary>Enable the startup warming pass. Default: disabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Maximum number of profiles to preload (ordered by <c>TraceCount DESC</c>).
    /// Capped at 10_000 to bound memory / DB load.
    /// </summary>
    [Range(1, 10_000)]
    public int MaxProfiles { get; set; } = 1000;

    /// <summary>
    /// Delay applied before the first load batch so the API can start
    /// accepting traffic before a large DB scan begins.
    /// </summary>
    public TimeSpan DelayOnStartup { get; set; } = TimeSpan.FromSeconds(5);
}
