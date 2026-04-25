using System.Collections.Immutable;
using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using Tracer.Application.EventHandlers;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Services;

/// <summary>
/// Runs enrichment providers in a three-tier waterfall pattern with parallel fan-out.
/// Tier 1 (priority ≤ 100): parallel via Task.WhenAll with per-provider timeout. Runs for all depths.
/// Tier 2 (priority 101–200): sequential, with accumulated fields from Tier 1. Requires Standard or Deep.
/// Tier 3 (priority > 200): sequential, with accumulated fields from Tier 1+2. Requires Deep only.
/// A total depth budget timeout wraps all tiers; if the budget expires, partial results are used.
/// Publishes SignalR notifications after each provider completes.
/// Uses GoldenRecordMerger for result merging and CkbPersistenceService for persistence.
/// Records observability metrics via ITracerMetrics.
/// </summary>
public sealed partial class WaterfallOrchestrator : IWaterfallOrchestrator
{
    private readonly IEnumerable<IEnrichmentProvider> _providers;
    private readonly ICompanyProfileRepository _profileRepository;
    private readonly IGoldenRecordMerger _merger;
    private readonly ICkbPersistenceService _persistenceService;
    private readonly IMediator _mediator;
    private readonly ITracerMetrics _metrics;
    private readonly ILogger<WaterfallOrchestrator> _logger;

    private const int Tier1MaxPriority = 100;
    private const int Tier2MaxPriority = 200;

    // Per-provider timeouts: must be less than the smallest relevant depth budget (Standard=15s)
    // so that the depth budget can fire before per-provider timeouts for short-budget depths.
    // Tier1: 8s — leaves headroom for Tier 2 within the 15s Standard budget.
    // Tier2: 12s — scraping; longer than registry APIs.
    // Tier3: 20s — AI extraction; longest operation, only runs under Deep=30s budget.
    private static readonly TimeSpan Tier1ProviderTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan Tier2ProviderTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan Tier3ProviderTimeout = TimeSpan.FromSeconds(20);

    public WaterfallOrchestrator(
        IEnumerable<IEnrichmentProvider> providers,
        ICompanyProfileRepository profileRepository,
        IGoldenRecordMerger merger,
        ICkbPersistenceService persistenceService,
        IMediator mediator,
        ITracerMetrics metrics,
        ILogger<WaterfallOrchestrator> logger)
    {
        _providers = providers;
        _profileRepository = profileRepository;
        _merger = merger;
        _persistenceService = persistenceService;
        _mediator = mediator;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<CompanyProfile> ExecuteAsync(TraceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var traceStopwatch = Stopwatch.StartNew();

        // 1. CKB lookup
        var profile = await FindExistingProfileAsync(request, cancellationToken).ConfigureAwait(false);
        var isNewProfile = profile is null;

        // 2. Build trace context
        var context = new TraceContext
        {
            Request = request,
            ExistingProfile = profile,
        };

        // 3. Filter applicable providers
        var applicable = _providers
            .Where(p => p.CanHandle(context))
            .OrderBy(p => p.Priority)
            .ToList();

        LogApplicableProviders(applicable.Count, request.Depth);

        if (applicable.Count == 0)
            return profile ?? CreateNewProfile(request);

        // 4. Split into tiers
        var tier1 = applicable.Where(p => p.Priority <= Tier1MaxPriority).ToList();
        var tier2 = applicable.Where(p => p.Priority > Tier1MaxPriority && p.Priority <= Tier2MaxPriority).ToList();
        var tier3 = applicable.Where(p => p.Priority > Tier2MaxPriority).ToList();

        var accumulatedFields = ImmutableHashSet<FieldName>.Empty;
        var sourceResults = new List<(string ProviderId, ProviderResult Result)>();

        // 5. Total depth budget — wraps all tiers; partial results are used if budget expires.
        using var depthTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        depthTimeoutCts.CancelAfter(GetDepthTimeout(request.Depth));
        var effectiveCt = depthTimeoutCts.Token;

        try
        {
            // 6. Fan-out Tier 1 in parallel with per-provider timeout
            if (tier1.Count > 0)
            {
                var tier1Tasks = tier1.Select(p =>
                    ExecuteProviderWithTimeoutAsync(p, context, Tier1ProviderTimeout, effectiveCt));
                var tier1Results = await Task.WhenAll(tier1Tasks).ConfigureAwait(false);

                foreach (var (providerId, result) in tier1Results)
                {
                    sourceResults.Add((providerId, result));
                    if (result.Found)
                        accumulatedFields = accumulatedFields.Union(result.Fields.Keys);

                    await PublishSourceCompletedAsync(request.Id, providerId, result, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            // 7. Sequential Tier 2 with accumulated fields from Tier 1 (Standard or Deep)
            if (request.Depth >= TraceDepth.Standard)
            {
                foreach (var provider in tier2)
                {
                    var updatedContext = context with { AccumulatedFields = accumulatedFields };
                    var (providerId, result) = await ExecuteProviderWithTimeoutAsync(
                        provider, updatedContext, Tier2ProviderTimeout, effectiveCt).ConfigureAwait(false);

                    sourceResults.Add((providerId, result));
                    if (result.Found)
                        accumulatedFields = accumulatedFields.Union(result.Fields.Keys);

                    await PublishSourceCompletedAsync(request.Id, providerId, result, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            // 8. Sequential Tier 3 with accumulated fields from Tier 1+2 (Deep only)
            if (request.Depth == TraceDepth.Deep)
            {
                foreach (var provider in tier3)
                {
                    var updatedContext = context with { AccumulatedFields = accumulatedFields };
                    var (providerId, result) = await ExecuteProviderWithTimeoutAsync(
                        provider, updatedContext, Tier3ProviderTimeout, effectiveCt).ConfigureAwait(false);

                    sourceResults.Add((providerId, result));
                    if (result.Found)
                        accumulatedFields = accumulatedFields.Union(result.Fields.Keys);

                    await PublishSourceCompletedAsync(request.Id, providerId, result, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested
                                                  && effectiveCt.IsCancellationRequested)
        {
            // Depth budget expired — use whatever partial results were collected so far.
            LogDepthBudgetExceeded(request.Depth, traceStopwatch.Elapsed);
        }

        // 9. Merge via GoldenRecordMerger
        var mergeInputs = sourceResults
            .Select(r => new ProviderMergeInput
            {
                ProviderId = r.ProviderId,
                SourceQuality = _providers.FirstOrDefault(p => p.ProviderId == r.ProviderId)?.SourceQuality ?? 0.5,
                Result = r.Result,
            })
            .ToList();

        var mergeResult = _merger.Merge(mergeInputs);

        // 10. Persist via CkbPersistenceService
        profile ??= CreateNewProfile(request);

        await _persistenceService.PersistEnrichmentAsync(
            profile, sourceResults, mergeResult, request.Id, cancellationToken).ConfigureAwait(false);

        var successCount = sourceResults.Count(r => r.Result.Found);
        LogOrchestratorComplete(profile.NormalizedKey, successCount);

        // 11. Record trace-level metrics
        _metrics.RecordTraceDuration(request.Depth.ToString(), traceStopwatch.Elapsed.TotalMilliseconds);

        // isNewProfile is accurate for RegistrationId-based lookups (CKB key is deterministic).
        // For name-only requests (no RegistrationId/Country), FindExistingProfileAsync always returns
        // null because name-based CKB lookup is not implemented — so every name-only trace
        // increments this counter. A precise fix requires CkbPersistenceService to return
        // an insert/update discriminator; deferred to a future task.
        if (isNewProfile)
            _metrics.RecordCkbProfileCreated();

        return profile;
    }

    private async Task<(string ProviderId, ProviderResult Result)> ExecuteProviderWithTimeoutAsync(
        IEnrichmentProvider provider, TraceContext context, TimeSpan providerTimeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(providerTimeout);

        // Add ProviderId to the logging scope so all log messages within this call
        // include the provider identifier as a structured property.
        using var logScope = _logger.BeginScope(
            new Dictionary<string, object?> { ["ProviderId"] = provider.ProviderId });

        try
        {
            LogProviderStarting(provider.ProviderId);

            var result = await provider.EnrichAsync(context, timeoutCts.Token).ConfigureAwait(false);

            LogProviderCompleted(provider.ProviderId, result.Status, result.Duration.TotalMilliseconds);

            _metrics.RecordProviderDuration(provider.ProviderId, result.Duration.TotalMilliseconds, success: result.Found);
            if (result.Found)
                _metrics.RecordProviderFieldsEnriched(provider.ProviderId, result.Fields.Count);

            return (provider.ProviderId, result);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested
                                                  && timeoutCts.Token.IsCancellationRequested)
        {
            // Per-provider timeout fired before the depth budget — graceful per-provider degradation.
            // Guard: per-provider timer (timeoutCts) cancelled AND effectiveCt (depth budget/caller)
            // has not yet fired. When effectiveCt fires, cancellationToken.IsCancellationRequested
            // becomes true (timeoutCts is linked), so this guard does not match and the exception
            // propagates to the outer ExecuteAsync budget catch.
            //
            // Note: when the depth budget fires during Task.WhenAll (Tier 1), results from providers
            // that already completed before the budget fired are not recoverable from WhenAll and are
            // lost. This is an accepted tradeoff — in practice the budget fires only when Tier 1
            // providers are all unusually slow (beyond the depth budget ceiling).
            LogProviderTimeout(provider.ProviderId);
            _metrics.RecordProviderDuration(provider.ProviderId, providerTimeout.TotalMilliseconds, success: false);
            return (provider.ProviderId, ProviderResult.Timeout(providerTimeout));
        }
        catch (OperationCanceledException)
        {
            // Depth budget (effectiveCt) or true caller cancellation — propagate.
            // ExecuteAsync outer catch discriminates between the two via !cancellationToken.IsCancellationRequested.
            throw;
        }
        #pragma warning disable CA1031 // Intentional: safe wrapper must catch all provider exceptions
        catch (Exception ex)
        {
            LogProviderException(ex, provider.ProviderId);
        #pragma warning restore CA1031
            _metrics.RecordProviderDuration(provider.ProviderId, 0, success: false);
            return (provider.ProviderId, ProviderResult.Error(
                "Provider execution failed", TimeSpan.Zero));
        }
    }

    private async Task PublishSourceCompletedAsync(
        Guid traceId, string providerId, ProviderResult result, CancellationToken cancellationToken)
    {
        try
        {
            await _mediator.Publish(new SourceCompletedNotification(
                traceId, providerId, result.Status,
                result.Fields.Count, (long)result.Duration.TotalMilliseconds),
                cancellationToken).ConfigureAwait(false);
        }
        #pragma warning disable CA1031 // SignalR notification failure must not break the pipeline
        catch (Exception ex)
        {
            LogNotificationError(ex, providerId);
        }
        #pragma warning restore CA1031
    }

    private async Task<CompanyProfile?> FindExistingProfileAsync(
        TraceRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.RegistrationId) &&
            !string.IsNullOrWhiteSpace(request.Country))
        {
            var key = $"{request.Country}:{request.RegistrationId}";
            var profile = await _profileRepository.FindByKeyAsync(key, cancellationToken)
                .ConfigureAwait(false);
            if (profile is not null)
                return profile;

            return await _profileRepository.FindByRegistrationIdAsync(
                request.RegistrationId, request.Country, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private static CompanyProfile CreateNewProfile(TraceRequest request)
    {
        var key = !string.IsNullOrWhiteSpace(request.RegistrationId) &&
                  !string.IsNullOrWhiteSpace(request.Country)
            ? $"{request.Country}:{request.RegistrationId}"
            : $"NAME:{request.CompanyName?.ToUpperInvariant()}";

        return new CompanyProfile(
            key,
            request.Country ?? "XX",
            request.RegistrationId);
    }

    /// <summary>
    /// Allows tests to override depth budget timeouts without waiting for real timers.
    /// Follows the same injectable-delegate pattern as DnsResolve in WebScraperClient.
    /// Production code leaves this null.
    /// </summary>
    internal Func<TraceDepth, TimeSpan>? DepthTimeoutOverride { get; init; }

    private TimeSpan GetDepthTimeout(TraceDepth depth) =>
        DepthTimeoutOverride?.Invoke(depth) ?? depth switch
        {
            TraceDepth.Quick => TimeSpan.FromSeconds(5),
            TraceDepth.Standard => TimeSpan.FromSeconds(15),
            TraceDepth.Deep => TimeSpan.FromSeconds(30),
            _ => TimeSpan.FromSeconds(15),
        };

    [LoggerMessage(Level = LogLevel.Information, Message = "Orchestrator: {ProviderCount} applicable providers for depth {Depth}")]
    private partial void LogApplicableProviders(int providerCount, TraceDepth depth);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Orchestrator: Starting provider {ProviderId}")]
    private partial void LogProviderStarting(string providerId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Orchestrator: Provider {ProviderId} completed with {Status} in {DurationMs:F0}ms")]
    private partial void LogProviderCompleted(string providerId, SourceStatus status, double durationMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Orchestrator: Provider {ProviderId} timed out")]
    private partial void LogProviderTimeout(string providerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Orchestrator: Provider {ProviderId} threw an exception")]
    private partial void LogProviderException(Exception ex, string providerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Orchestrator: SignalR notification failed for provider {ProviderId}")]
    private partial void LogNotificationError(Exception ex, string providerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Orchestrator: Complete for {NormalizedKey}, {SuccessCount} providers returned data")]
    private partial void LogOrchestratorComplete(string normalizedKey, int successCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Orchestrator: Depth budget for {Depth} expired after {Elapsed}; using partial results")]
    private partial void LogDepthBudgetExceeded(TraceDepth depth, TimeSpan elapsed);
}
