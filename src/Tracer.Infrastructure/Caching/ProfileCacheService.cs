using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracer.Application.DTOs;
using Tracer.Application.Services;

namespace Tracer.Infrastructure.Caching;

/// <summary>
/// Distributed cache implementation for company profile DTOs.
/// Uses <see cref="IDistributedCache"/> (in-memory for MVP, Redis in Phase 4).
/// Records cache hit/miss metrics via <see cref="ITracerMetrics"/>.
/// </summary>
internal sealed partial class ProfileCacheService : IProfileCacheService
{
    private readonly IDistributedCache _cache;
    private readonly CacheOptions _options;
    private readonly ITracerMetrics _metrics;
    private readonly ILogger<ProfileCacheService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ProfileCacheService(
        IDistributedCache cache,
        IOptions<CacheOptions> options,
        ITracerMetrics metrics,
        ILogger<ProfileCacheService> logger)
    {
        _cache = cache;
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<CompanyProfileDto?> GetAsync(string normalizedKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedKey, nameof(normalizedKey));

        var cacheKey = BuildKey(normalizedKey);

        try
        {
            var bytes = await _cache.GetAsync(cacheKey, cancellationToken).ConfigureAwait(false);
            if (bytes is null)
            {
                LogCacheMiss(normalizedKey);
                _metrics.RecordCacheMiss();
                return null;
            }

            LogCacheHit(normalizedKey);
            _metrics.RecordCacheHit();
            return JsonSerializer.Deserialize<CompanyProfileDto>(bytes, JsonOptions);
        }
        #pragma warning disable CA1031 // Cache failures must not break the enrichment pipeline
        catch (Exception ex)
        {
            LogCacheError(ex, normalizedKey);
            return null;
        }
        #pragma warning restore CA1031
    }

    public async Task SetAsync(string normalizedKey, CompanyProfileDto profile, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedKey, nameof(normalizedKey));
        ArgumentNullException.ThrowIfNull(profile);

        var cacheKey = BuildKey(normalizedKey);

        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(profile, JsonOptions);
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _options.ProfileTtl,
            };

            await _cache.SetAsync(cacheKey, bytes, options, cancellationToken).ConfigureAwait(false);
            LogCacheSet(normalizedKey);
        }
        #pragma warning disable CA1031
        catch (Exception ex)
        {
            LogCacheError(ex, normalizedKey);
        }
        #pragma warning restore CA1031
    }

    public async Task RemoveAsync(string normalizedKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedKey, nameof(normalizedKey));

        var cacheKey = BuildKey(normalizedKey);

        try
        {
            await _cache.RemoveAsync(cacheKey, cancellationToken).ConfigureAwait(false);
            LogCacheInvalidated(normalizedKey);
        }
        #pragma warning disable CA1031
        catch (Exception ex)
        {
            LogCacheError(ex, normalizedKey);
        }
        #pragma warning restore CA1031
    }

    private static string BuildKey(string normalizedKey) => $"profile:{normalizedKey}";

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache: HIT for {NormalizedKey}")]
    private partial void LogCacheHit(string normalizedKey);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache: MISS for {NormalizedKey}")]
    private partial void LogCacheMiss(string normalizedKey);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache: SET for {NormalizedKey}")]
    private partial void LogCacheSet(string normalizedKey);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache: INVALIDATED {NormalizedKey}")]
    private partial void LogCacheInvalidated(string normalizedKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cache: Error for {NormalizedKey}")]
    private partial void LogCacheError(Exception ex, string normalizedKey);
}
