using System.Collections.Immutable;
using MediatR;
using Microsoft.Extensions.Logging;
using Tracer.Application.EventHandlers;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Services;

/// <summary>
/// Runs enrichment providers in waterfall pattern with parallel fan-out.
/// Tier 1 (priority ≤ 100): parallel via Task.WhenAll with per-provider timeout.
/// Tier 2+ (priority > 100): sequential, with accumulated fields from Tier 1.
/// Publishes SignalR notifications after each provider completes.
/// Uses GoldenRecordMerger for result merging and CkbPersistenceService for persistence.
/// </summary>
public sealed partial class WaterfallOrchestrator : IWaterfallOrchestrator
{
    private readonly IEnumerable<IEnrichmentProvider> _providers;
    private readonly ICompanyProfileRepository _profileRepository;
    private readonly IGoldenRecordMerger _merger;
    private readonly ICkbPersistenceService _persistenceService;
    private readonly IMediator _mediator;
    private readonly ILogger<WaterfallOrchestrator> _logger;

    private const int Tier1MaxPriority = 100;
    private static readonly TimeSpan PerProviderTimeout = TimeSpan.FromSeconds(15);

    public WaterfallOrchestrator(
        IEnumerable<IEnrichmentProvider> providers,
        ICompanyProfileRepository profileRepository,
        IGoldenRecordMerger merger,
        ICkbPersistenceService persistenceService,
        IMediator mediator,
        ILogger<WaterfallOrchestrator> logger)
    {
        _providers = providers;
        _profileRepository = profileRepository;
        _merger = merger;
        _persistenceService = persistenceService;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<CompanyProfile> ExecuteAsync(TraceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. CKB lookup
        var profile = await FindExistingProfileAsync(request, cancellationToken).ConfigureAwait(false);

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
        var tier2Plus = applicable.Where(p => p.Priority > Tier1MaxPriority).ToList();

        var accumulatedFields = ImmutableHashSet<FieldName>.Empty;
        var sourceResults = new List<(string ProviderId, ProviderResult Result)>();

        // 5. Fan-out Tier 1 in parallel with per-provider timeout
        if (tier1.Count > 0)
        {
            var tier1Tasks = tier1.Select(p =>
                ExecuteProviderWithTimeoutAsync(p, context, cancellationToken));
            var tier1Results = await Task.WhenAll(tier1Tasks).ConfigureAwait(false);

            foreach (var (providerId, result) in tier1Results)
            {
                sourceResults.Add((providerId, result));
                if (result.Found)
                    accumulatedFields = accumulatedFields.Union(result.Fields.Keys);

                // Push SignalR notification
                await PublishSourceCompletedAsync(request.Id, providerId, result, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        // 6. Sequential Tier 2+ with accumulated fields (only for Standard/Deep)
        if (request.Depth >= TraceDepth.Standard)
        {
            foreach (var provider in tier2Plus)
            {
                var updatedContext = context with { AccumulatedFields = accumulatedFields };
                var (providerId, result) = await ExecuteProviderWithTimeoutAsync(
                    provider, updatedContext, cancellationToken).ConfigureAwait(false);

                sourceResults.Add((providerId, result));
                if (result.Found)
                    accumulatedFields = accumulatedFields.Union(result.Fields.Keys);

                await PublishSourceCompletedAsync(request.Id, providerId, result, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        // 7. Merge via GoldenRecordMerger
        var mergeInputs = sourceResults
            .Select(r => new ProviderMergeInput
            {
                ProviderId = r.ProviderId,
                SourceQuality = _providers.FirstOrDefault(p => p.ProviderId == r.ProviderId)?.SourceQuality ?? 0.5,
                Result = r.Result,
            })
            .ToList();

        var mergeResult = _merger.Merge(mergeInputs);

        // 8. Persist via CkbPersistenceService
        profile ??= CreateNewProfile(request);

        await _persistenceService.PersistEnrichmentAsync(
            profile, sourceResults, mergeResult, request.Id, cancellationToken).ConfigureAwait(false);

        var successCount = sourceResults.Count(r => r.Result.Found);
        LogOrchestratorComplete(profile.NormalizedKey, successCount);

        return profile;
    }

    private async Task<(string ProviderId, ProviderResult Result)> ExecuteProviderWithTimeoutAsync(
        IEnrichmentProvider provider, TraceContext context, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(PerProviderTimeout);

        try
        {
            LogProviderStarting(provider.ProviderId);

            var result = await provider.EnrichAsync(context, timeoutCts.Token).ConfigureAwait(false);

            LogProviderCompleted(provider.ProviderId, result.Status, result.Duration.TotalMilliseconds);

            return (provider.ProviderId, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // Propagate caller cancellation
        }
        catch (OperationCanceledException)
        {
            // Per-provider timeout
            LogProviderTimeout(provider.ProviderId);
            return (provider.ProviderId, ProviderResult.Timeout(PerProviderTimeout));
        }
        #pragma warning disable CA1031 // Intentional: safe wrapper must catch all provider exceptions
        catch (Exception ex)
        {
            LogProviderException(ex, provider.ProviderId);
        #pragma warning restore CA1031
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
}
