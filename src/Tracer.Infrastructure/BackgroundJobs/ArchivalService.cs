using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracer.Application.Services;
using Tracer.Domain.Interfaces;

namespace Tracer.Infrastructure.BackgroundJobs;

/// <summary>
/// Daily <see cref="BackgroundService"/> that archives one-shot CKB profiles:
/// profiles with <c>TraceCount ≤ MaxTraceCount</c> whose <c>LastEnrichedAt</c>
/// is older than <c>MinAgeDays</c>. Archived profiles are excluded from the
/// re-validation sweep and the fuzzy-match candidate pool but remain queryable
/// via <c>GET /api/profiles?includeArchived=true</c>.
/// </summary>
/// <remarks>
/// <para>
/// The actual SQL UPDATE is issued by
/// <see cref="ICompanyProfileRepository.ArchiveStaleAsync"/>. The service
/// repeatedly invokes it per batch until the repository reports zero rows,
/// so the transaction log stays bounded even on the first run after deployment.
/// </para>
/// <para>
/// Archival is intentionally silent — no <c>ChangeEvent</c>, no domain event,
/// no Service Bus / SignalR notification. It's CKB maintenance, not a business
/// change. Un-archival (on a new trace hitting an archived profile) is handled
/// in <see cref="CkbPersistenceService"/> and is similarly silent.
/// </para>
/// </remarks>
internal sealed partial class ArchivalService : BackgroundService
{
    private static readonly TimeSpan PerTickTimeout = TimeSpan.FromMinutes(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITracerMetrics _metrics;
    private readonly ArchivalOptions _options;
    private readonly ILogger<ArchivalService> _logger;

    public ArchivalService(
        IServiceScopeFactory scopeFactory,
        ITracerMetrics metrics,
        IOptions<ArchivalOptions> options,
        ILogger<ArchivalService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _scopeFactory = scopeFactory;
        _metrics = metrics;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Testing seam — overrides <see cref="DateTimeOffset.UtcNow"/> when
    /// computing the archival cutoff, so unit tests do not depend on real
    /// clock time.
    /// </summary>
    internal Func<DateTimeOffset> Clock { get; init; } = static () => DateTimeOffset.UtcNow;

    /// <summary>
    /// Testing seam — replaces <see cref="Task.Delay(TimeSpan, CancellationToken)"/>
    /// in the main loop so tests can drive the service deterministically
    /// without waiting 24 hours per iteration.
    /// </summary>
    internal Func<TimeSpan, CancellationToken, Task> DelayAsync { get; init; } = Task.Delay;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogServiceStarting(_options.IntervalHours, _options.MinAgeDays, _options.MaxTraceCount, _options.BatchSize);

        var interval = TimeSpan.FromHours(Math.Max(1, _options.IntervalHours));

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
#pragma warning disable CA1031 // Intentional: archival must keep running across transient errors
            catch (Exception ex)
            {
                // Use exception type name only — ex.Message can leak internal paths / connection strings (CWE-209).
                LogTickFailed(ex, ex.GetType().Name);
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

        LogServiceStopping();
    }

    /// <summary>
    /// Runs a single archival tick: computes the cutoff (<c>now − MinAgeDays</c>)
    /// and repeatedly asks the repository to archive up to <c>BatchSize</c> rows
    /// until it reports zero. Public <c>internal</c> so tests can trigger one
    /// iteration without running the outer loop.
    /// </summary>
    internal async Task RunTickAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(PerTickTimeout);

        var cutoff = Clock() - TimeSpan.FromDays(Math.Max(1, _options.MinAgeDays));
        var maxTraceCount = Math.Max(0, _options.MaxTraceCount);
        var batchSize = Math.Max(1, _options.BatchSize);

        var totalArchived = 0;
        var batches = 0;

        try
        {
            while (!timeoutCts.IsCancellationRequested)
            {
#pragma warning disable CA2007 // AsyncServiceScope does not need ConfigureAwait
                await using var scope = _scopeFactory.CreateAsyncScope();
#pragma warning restore CA2007
                var repository = scope.ServiceProvider.GetRequiredService<ICompanyProfileRepository>();

                var archived = await repository
                    .ArchiveStaleAsync(cutoff, maxTraceCount, batchSize, timeoutCts.Token)
                    .ConfigureAwait(false);

                batches++;
                totalArchived += archived;

                // ArchiveStaleAsync returns 0 when no more candidates match; done.
                if (archived < batchSize)
                    break;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested
                                                  && timeoutCts.IsCancellationRequested)
        {
            LogTickTimeout(totalArchived, batches);
        }

        stopwatch.Stop();

        if (totalArchived > 0)
            _metrics.RecordCkbArchived(totalArchived);

        LogTickCompleted(totalArchived, batches, stopwatch.Elapsed.TotalMilliseconds);
    }

    // ── LoggerMessage source generators ────────────────────────────────
    // All payloads are PII-free: counts, durations, exception type names only.
    // Never interpolate profile IDs, names, addresses, or ex.Message (CWE-209).

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Archival service starting: interval={IntervalHours}h, minAge={MinAgeDays}d, " +
                  "maxTraceCount={MaxTraceCount}, batchSize={BatchSize}")]
    private partial void LogServiceStarting(int intervalHours, int minAgeDays, int maxTraceCount, int batchSize);

    [LoggerMessage(Level = LogLevel.Information, Message = "Archival service stopping")]
    private partial void LogServiceStopping();

    [LoggerMessage(Level = LogLevel.Error, Message = "Archival tick failed ({ExceptionType})")]
    private partial void LogTickFailed(Exception ex, string exceptionType);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Archival tick exceeded per-tick timeout after {Archived} rows across {Batches} batches — partial progress retained, continuing next tick")]
    private partial void LogTickTimeout(int archived, int batches);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Archival tick completed: archived={Archived}, batches={Batches}, duration={DurationMs:F1}ms")]
    private partial void LogTickCompleted(int archived, int batches, double durationMs);
}
