using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracer.Application.Services;
using Tracer.Domain.Entities;
using Tracer.Domain.Interfaces;

namespace Tracer.Infrastructure.BackgroundJobs;

/// <summary>
/// Periodic <see cref="BackgroundService"/> that walks the CKB and
/// re-validates <see cref="CompanyProfile"/> records whose field TTLs
/// have expired. Manual revalidation requests from
/// <c>POST /api/profiles/{id}/revalidate</c> are drained first every tick,
/// then (optionally gated by an off-peak window) the scheduler pulls a
/// bounded batch from <see cref="ICompanyProfileRepository.GetRevalidationQueueAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// The actual re-validation logic lives in <see cref="IRevalidationRunner"/>.
/// In B-65 the only implementation is <see cref="NoOpRevalidationRunner"/>
/// which returns <see cref="RevalidationOutcome.Deferred"/>. B-66 adds the
/// lightweight mode and B-67 adds the deep mode.
/// </para>
/// <para>
/// Design notes:
/// <list type="bullet">
///   <item><description>
///     Scheduler is registered as a Singleton via <c>AddHostedService</c>.
///     Because <c>TracerDbContext</c> is Scoped, we create one
///     <see cref="IServiceScope"/> per processed profile via
///     <see cref="IServiceScopeFactory"/> (avoids captive dependency).
///   </description></item>
///   <item><description>
///     Per-profile work is bounded by a 5-minute cancellation token so a
///     hanging runner cannot starve the tick or block shutdown.
///   </description></item>
///   <item><description>
///     Off-peak gating only applies to the automatic sweep — manual queue
///     items are drained on every tick regardless of the hour.
///   </description></item>
/// </list>
/// </para>
/// </remarks>
internal sealed partial class RevalidationScheduler : BackgroundService
{
    private static readonly TimeSpan PerProfileTimeout = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRevalidationQueue _manualQueue;
    private readonly ITracerMetrics _metrics;
    private readonly RevalidationOptions _options;
    private readonly ILogger<RevalidationScheduler> _logger;

    public RevalidationScheduler(
        IServiceScopeFactory scopeFactory,
        IRevalidationQueue manualQueue,
        ITracerMetrics metrics,
        IOptions<RevalidationOptions> options,
        ILogger<RevalidationScheduler> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _scopeFactory = scopeFactory;
        _manualQueue = manualQueue;
        _metrics = metrics;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Testing seam — overrides <see cref="DateTimeOffset.UtcNow"/> in
    /// off-peak evaluation so unit tests do not depend on real clock time.
    /// </summary>
    internal Func<DateTimeOffset> Clock { get; init; } = static () => DateTimeOffset.UtcNow;

    /// <summary>
    /// Testing seam — replaces <see cref="Task.Delay(TimeSpan, CancellationToken)"/>
    /// in the main loop so tests can drive the scheduler deterministically
    /// without waiting an hour per iteration.
    /// </summary>
    internal Func<TimeSpan, CancellationToken, Task> DelayAsync { get; init; } = Task.Delay;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogSchedulerStarting(_options.IntervalMinutes, _options.MaxProfilesPerRun,
            _options.OffPeak.Enabled, _options.OffPeak.StartHourUtc, _options.OffPeak.EndHourUtc);

        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunTickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // Intentional: scheduler must keep running across transient errors
            catch (Exception ex)
            {
                LogTickFailed(ex);
            }
#pragma warning restore CA1031

            try
            {
                await DelayAsync(interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        LogSchedulerStopping();
    }

    /// <summary>
    /// Performs a single scheduler tick: drains the manual queue, then (if
    /// off-peak allows) runs the automatic sweep. Public <c>internal</c>
    /// so tests can trigger one iteration without running the outer loop.
    /// </summary>
    internal async Task RunTickAsync(CancellationToken cancellationToken)
    {
        var manualStopwatch = Stopwatch.StartNew();
        var manualStats = await DrainManualQueueAsync(cancellationToken).ConfigureAwait(false);
        manualStopwatch.Stop();

        if (manualStats.Candidates > 0)
        {
            _metrics.RecordRevalidationRun("manual",
                manualStats.Processed, manualStats.Skipped, manualStats.Failed,
                manualStopwatch.Elapsed.TotalMilliseconds);
            LogManualRunCompleted(manualStats.Processed, manualStats.Skipped, manualStats.Failed,
                manualStopwatch.Elapsed.TotalMilliseconds);
        }

        var now = Clock();
        if (!_options.OffPeak.IsWithin(now))
        {
            LogOutsideOffPeak(now.UtcDateTime.Hour, _options.OffPeak.StartHourUtc, _options.OffPeak.EndHourUtc);
            return;
        }

        var autoStopwatch = Stopwatch.StartNew();
        var autoStats = await RunAutomaticSweepAsync(cancellationToken).ConfigureAwait(false);
        autoStopwatch.Stop();

        _metrics.RecordRevalidationRun("auto",
            autoStats.Processed, autoStats.Skipped, autoStats.Failed,
            autoStopwatch.Elapsed.TotalMilliseconds);
        LogAutoRunCompleted(autoStats.Candidates, autoStats.Processed, autoStats.Skipped, autoStats.Failed,
            autoStopwatch.Elapsed.TotalMilliseconds);
    }

    private async Task<TickStats> DrainManualQueueAsync(CancellationToken cancellationToken)
    {
        var stats = new TickStats();

        while (!cancellationToken.IsCancellationRequested && _manualQueue.TryDequeue(out var profileId))
        {
            stats.Candidates++;

            var outcome = await ProcessProfileByIdAsync(profileId, cancellationToken).ConfigureAwait(false);
            stats.Record(outcome);
        }

        return stats;
    }

    private async Task<TickStats> RunAutomaticSweepAsync(CancellationToken cancellationToken)
    {
        var stats = new TickStats();
        var budget = Math.Max(1, _options.MaxProfilesPerRun);

#pragma warning disable CA2007 // AsyncServiceScope + ServiceProvider access requires no ConfigureAwait
        await using var scope = _scopeFactory.CreateAsyncScope();
#pragma warning restore CA2007
        var repository = scope.ServiceProvider.GetRequiredService<ICompanyProfileRepository>();

        var candidates = await repository
            .GetRevalidationQueueAsync(budget, cancellationToken)
            .ConfigureAwait(false);

        foreach (var profile in candidates)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            stats.Candidates++;

            if (!profile.NeedsRevalidation())
            {
                stats.Skipped++;
                continue;
            }

            var outcome = await ProcessProfileAsync(profile, cancellationToken).ConfigureAwait(false);
            stats.Record(outcome);
        }

        return stats;
    }

    private async Task<RevalidationOutcome> ProcessProfileByIdAsync(Guid profileId, CancellationToken cancellationToken)
    {
#pragma warning disable CA2007
        await using var scope = _scopeFactory.CreateAsyncScope();
#pragma warning restore CA2007
        var repository = scope.ServiceProvider.GetRequiredService<ICompanyProfileRepository>();

        var profile = await repository.GetByIdAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            LogManualProfileMissing(profileId);
            return RevalidationOutcome.Deferred;
        }

        return await InvokeRunnerAsync(scope.ServiceProvider, profile, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RevalidationOutcome> ProcessProfileAsync(CompanyProfile profile, CancellationToken cancellationToken)
    {
#pragma warning disable CA2007
        await using var scope = _scopeFactory.CreateAsyncScope();
#pragma warning restore CA2007

        return await InvokeRunnerAsync(scope.ServiceProvider, profile, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RevalidationOutcome> InvokeRunnerAsync(
        IServiceProvider scopedProvider, CompanyProfile profile, CancellationToken cancellationToken)
    {
        var runner = scopedProvider.GetRequiredService<IRevalidationRunner>();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(PerProfileTimeout);

        try
        {
            return await runner.RunAsync(profile, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            LogProfileTimeout(profile.Id);
            return RevalidationOutcome.Failed;
        }
#pragma warning disable CA1031 // Intentional: one bad profile must not crash the scheduler
        catch (Exception ex)
        {
            // Use exception type name only — ex.Message can leak internal paths / connection strings (CWE-209)
            LogProfileFailed(ex, profile.Id, ex.GetType().Name);
            return RevalidationOutcome.Failed;
        }
#pragma warning restore CA1031
    }

    // ── telemetry helpers ──────────────────────────────────────────────

    private sealed class TickStats
    {
        public int Candidates { get; set; }
        public int Processed { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }

        public void Record(RevalidationOutcome outcome)
        {
            switch (outcome)
            {
                case RevalidationOutcome.Lightweight:
                case RevalidationOutcome.Deep:
                    Processed++;
                    break;
                case RevalidationOutcome.Failed:
                    Failed++;
                    break;
                default:
                    Skipped++;
                    break;
            }
        }
    }

    // ── LoggerMessage source generators ────────────────────────────────

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Revalidation scheduler starting: interval={IntervalMinutes}m, budget={MaxProfilesPerRun}, " +
                  "offPeakEnabled={OffPeakEnabled}, window={OffPeakStart}-{OffPeakEnd} UTC")]
    private partial void LogSchedulerStarting(int intervalMinutes, int maxProfilesPerRun,
        bool offPeakEnabled, int offPeakStart, int offPeakEnd);

    [LoggerMessage(Level = LogLevel.Information, Message = "Revalidation scheduler stopping")]
    private partial void LogSchedulerStopping();

    [LoggerMessage(Level = LogLevel.Error, Message = "Revalidation scheduler tick failed")]
    private partial void LogTickFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Revalidation: outside off-peak window (hour={Hour}, window={Start}-{End})")]
    private partial void LogOutsideOffPeak(int hour, int start, int end);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Revalidation manual run: processed={Processed}, skipped={Skipped}, failed={Failed}, duration={DurationMs:F1}ms")]
    private partial void LogManualRunCompleted(int processed, int skipped, int failed, double durationMs);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Revalidation auto run: candidates={Candidates}, processed={Processed}, skipped={Skipped}, failed={Failed}, duration={DurationMs:F1}ms")]
    private partial void LogAutoRunCompleted(int candidates, int processed, int skipped, int failed, double durationMs);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Revalidation manual: profile {ProfileId} not found, dropping request")]
    private partial void LogManualProfileMissing(Guid profileId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Revalidation: profile {ProfileId} exceeded per-profile timeout")]
    private partial void LogProfileTimeout(Guid profileId);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Revalidation: profile {ProfileId} failed ({ExceptionType})")]
    private partial void LogProfileFailed(Exception ex, Guid profileId, string exceptionType);
}
