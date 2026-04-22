using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracer.Application.Mapping;
using Tracer.Application.Services;
using Tracer.Domain.Interfaces;
using Tracer.Infrastructure.Caching;

namespace Tracer.Infrastructure.BackgroundJobs;

/// <summary>
/// One-off cache pre-population pass executed shortly after startup. Loads
/// the top <see cref="CacheWarmingOptions.MaxProfiles"/> non-archived profiles
/// (ordered by <c>TraceCount DESC</c>) via <see cref="ICompanyProfileRepository.ListTopByTraceCountAsync"/>
/// and writes each one through <see cref="IProfileCacheService"/> so the first
/// production traces hit a warm cache.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a Singleton <c>HostedService</c> only when
/// <c>Cache:Warming:Enabled = true</c>. Disabled by default — operators
/// opt-in per environment so dev / CI runs are not slowed down by an
/// unnecessary DB scan.
/// </para>
/// <para>
/// Mirrors the B-65 <c>RevalidationScheduler</c> patterns:
/// <list type="bullet">
///   <item><description>Singleton + <see cref="IServiceScopeFactory"/> for Scoped repository access.</description></item>
///   <item><description><see cref="DelayAsync"/> testing seam so unit tests stay deterministic.</description></item>
///   <item><description><c>LoggerMessage</c> source-generated logging — PII-free (counts only).</description></item>
///   <item><description>Non-throwing — warming failures must never crash startup.</description></item>
/// </list>
/// </para>
/// </remarks>
internal sealed partial class CacheWarmingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CacheOptions _cacheOptions;
    private readonly ILogger<CacheWarmingService> _logger;

    public CacheWarmingService(
        IServiceScopeFactory scopeFactory,
        IOptions<CacheOptions> cacheOptions,
        ILogger<CacheWarmingService> logger)
    {
        ArgumentNullException.ThrowIfNull(cacheOptions);
        _scopeFactory = scopeFactory;
        _cacheOptions = cacheOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Testing seam — replaces <see cref="Task.Delay(TimeSpan, CancellationToken)"/>
    /// in <see cref="ExecuteAsync"/> so unit tests can drive the service deterministically.
    /// </summary>
    internal Func<TimeSpan, CancellationToken, Task> DelayAsync { get; init; } =
        static (delay, ct) => Task.Delay(delay, ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_cacheOptions.Warming.Enabled)
        {
            // Defensive — Program.cs already gates registration on this flag,
            // but this guard makes the service safe to register unconditionally
            // in tests that do not control the IHostedService set.
            return;
        }

        try
        {
            if (_cacheOptions.Warming.DelayOnStartup > TimeSpan.Zero)
            {
                await DelayAsync(_cacheOptions.Warming.DelayOnStartup, stoppingToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutting down before we even started — that's fine.
            return;
        }

        await WarmAsync(stoppingToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs the actual warming pass. Internal so unit tests can invoke
    /// it without going through the BackgroundService loop.
    /// </summary>
    internal async Task WarmAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var loaded = 0;
        var failed = 0;

        try
        {
            #pragma warning disable CA2007 // AsyncServiceScope.DisposeAsync — ConfigureAwait would lose ServiceProvider
            await using var scope = _scopeFactory.CreateAsyncScope();
            #pragma warning restore CA2007
            var repository = scope.ServiceProvider.GetRequiredService<ICompanyProfileRepository>();
            var cache = scope.ServiceProvider.GetRequiredService<IProfileCacheService>();

            var profiles = await repository
                .ListTopByTraceCountAsync(_cacheOptions.Warming.MaxProfiles, cancellationToken)
                .ConfigureAwait(false);

            LogWarmStart(profiles.Count);

            foreach (var profile in profiles)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    var dto = profile.ToDto();
                    await cache.SetAsync(profile.NormalizedKey, dto, cancellationToken)
                        .ConfigureAwait(false);
                    loaded++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                #pragma warning disable CA1031 // Per-profile cache failures must not abort warming
                catch (Exception ex)
                {
                    failed++;
                    LogProfileWarmFailed(profile.Id, ex.GetType().Name);
                }
                #pragma warning restore CA1031
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogWarmCancelled(loaded, failed);
            return;
        }
        #pragma warning disable CA1031 // Warming must never crash startup
        catch (Exception ex)
        {
            LogWarmFailed(ex.GetType().Name, loaded, failed);
            return;
        }
        #pragma warning restore CA1031

        stopwatch.Stop();
        LogWarmComplete(loaded, failed, stopwatch.ElapsedMilliseconds);
    }

    [LoggerMessage(EventId = 9100, Level = LogLevel.Information,
        Message = "Cache warming starting: {Count} profiles selected")]
    private partial void LogWarmStart(int count);

    [LoggerMessage(EventId = 9101, Level = LogLevel.Information,
        Message = "Cache warming complete: {Loaded} loaded, {Failed} failed in {ElapsedMs} ms")]
    private partial void LogWarmComplete(int loaded, int failed, long elapsedMs);

    [LoggerMessage(EventId = 9102, Level = LogLevel.Warning,
        Message = "Cache warming cancelled: {Loaded} loaded, {Failed} failed before stop")]
    private partial void LogWarmCancelled(int loaded, int failed);

    [LoggerMessage(EventId = 9103, Level = LogLevel.Error,
        Message = "Cache warming aborted ({ExceptionType}): {Loaded} loaded, {Failed} failed")]
    private partial void LogWarmFailed(string exceptionType, int loaded, int failed);

    [LoggerMessage(EventId = 9104, Level = LogLevel.Warning,
        Message = "Cache warming: profile {ProfileId} could not be cached ({ExceptionType})")]
    private partial void LogProfileWarmFailed(Guid profileId, string exceptionType);
}
