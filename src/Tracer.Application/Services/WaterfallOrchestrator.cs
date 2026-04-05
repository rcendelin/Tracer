using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;

namespace Tracer.Application.Services;

/// <summary>
/// Runs enrichment providers in priority order (waterfall pattern).
/// Tier 1 (priority ≤ 100) runs in parallel via <c>Task.WhenAll</c>.
/// Each provider executes within a safe wrapper that catches exceptions.
/// Results are merged into a CKB company profile.
/// </summary>
public sealed partial class WaterfallOrchestrator : IWaterfallOrchestrator
{
    private readonly IEnumerable<IEnrichmentProvider> _providers;
    private readonly ICompanyProfileRepository _profileRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<WaterfallOrchestrator> _logger;

    private const int Tier1MaxPriority = 100;

    public WaterfallOrchestrator(
        IEnumerable<IEnrichmentProvider> providers,
        ICompanyProfileRepository profileRepository,
        IUnitOfWork unitOfWork,
        ILogger<WaterfallOrchestrator> logger)
    {
        _providers = providers;
        _profileRepository = profileRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<CompanyProfile> ExecuteAsync(TraceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. CKB lookup — try to find existing profile
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
        {
            // No providers can handle this request — return existing or new empty profile
            return profile ?? CreateNewProfile(request);
        }

        // 4. Fan-out Tier 1 providers in parallel
        var tier1 = applicable.Where(p => p.Priority <= Tier1MaxPriority).ToList();
        var tier2Plus = applicable.Where(p => p.Priority > Tier1MaxPriority).ToList();

        var accumulatedFields = ImmutableHashSet<FieldName>.Empty;
        var sourceResults = new List<(string ProviderId, ProviderResult Result)>();

        // Execute Tier 1 in parallel
        if (tier1.Count > 0)
        {
            var tier1Tasks = tier1.Select(p => ExecuteProviderSafeAsync(p, context, cancellationToken));
            var tier1Results = await Task.WhenAll(tier1Tasks).ConfigureAwait(false);

            foreach (var (providerId, result) in tier1Results)
            {
                sourceResults.Add((providerId, result));
                if (result.Found)
                    accumulatedFields = accumulatedFields.Union(result.Fields.Keys);
            }
        }

        // Execute Tier 2+ sequentially (only if depth permits)
        if (request.Depth >= TraceDepth.Standard)
        {
            foreach (var provider in tier2Plus)
            {
                var updatedContext = context with { AccumulatedFields = accumulatedFields };
                var (providerId, result) = await ExecuteProviderSafeAsync(
                    provider, updatedContext, cancellationToken).ConfigureAwait(false);

                sourceResults.Add((providerId, result));
                if (result.Found)
                    accumulatedFields = accumulatedFields.Union(result.Fields.Keys);
            }
        }

        // 5. Create or update profile
        profile ??= CreateNewProfile(request);

        // 6. Merge provider results into profile
        MergeResults(profile, sourceResults);

        // 7. Score overall confidence
        var confidence = ScoreConfidence(sourceResults);
        profile.SetOverallConfidence(confidence);
        profile.IncrementTraceCount();

        // 8. Persist to CKB
        await _profileRepository.UpsertAsync(profile, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var successCount = sourceResults.Count(r => r.Result.Found);
        LogOrchestratorComplete(profile.NormalizedKey, successCount);

        return profile;
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

    private async Task<(string ProviderId, ProviderResult Result)> ExecuteProviderSafeAsync(
        IEnrichmentProvider provider, TraceContext context, CancellationToken cancellationToken)
    {
        try
        {
            LogProviderStarting(provider.ProviderId);

            var result = await provider.EnrichAsync(context, cancellationToken).ConfigureAwait(false);

            LogProviderCompleted(provider.ProviderId, result.Status, result.Duration.TotalMilliseconds);

            return (provider.ProviderId, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // Propagate caller cancellation
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

    private static void MergeResults(
        CompanyProfile profile,
        List<(string ProviderId, ProviderResult Result)> sourceResults)
    {
        // Merge in priority order — first provider to set a field wins (highest priority)
        foreach (var (providerId, result) in sourceResults)
        {
            if (!result.Found)
                continue;

            foreach (var (fieldName, value) in result.Fields)
            {
                if (value is null)
                    continue;

                // Create a TracedField based on value type
                switch (fieldName)
                {
                    case FieldName.RegisteredAddress or FieldName.OperatingAddress when value is Address addr:
                        profile.UpdateField(fieldName, new TracedField<Address>
                        {
                            Value = addr,
                            Confidence = Confidence.Create(0.8),
                            Source = providerId,
                            EnrichedAt = DateTimeOffset.UtcNow,
                        }, providerId);
                        break;
                    case FieldName.Location when value is GeoCoordinate geo:
                        profile.UpdateField(fieldName, new TracedField<GeoCoordinate>
                        {
                            Value = geo,
                            Confidence = Confidence.Create(0.8),
                            Source = providerId,
                            EnrichedAt = DateTimeOffset.UtcNow,
                        }, providerId);
                        break;
                    case var _ when value is string strVal:
                        profile.UpdateField(fieldName, new TracedField<string>
                        {
                            Value = strVal,
                            Confidence = Confidence.Create(0.8),
                            Source = providerId,
                            EnrichedAt = DateTimeOffset.UtcNow,
                        }, providerId);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Simple confidence scoring — average of successful provider source qualities.
    /// Will be replaced by a proper ConfidenceScorer in B-18.
    /// </summary>
    private Confidence ScoreConfidence(
        List<(string ProviderId, ProviderResult Result)> sourceResults)
    {
        var successfulProviders = sourceResults
            .Where(r => r.Result.Found)
            .Select(r => _providers.FirstOrDefault(p => p.ProviderId == r.ProviderId))
            .Where(p => p is not null)
            .ToList();

        if (successfulProviders.Count == 0)
            return Confidence.Zero;

        var avgQuality = successfulProviders.Average(p => p!.SourceQuality);
        return Confidence.Create(Math.Min(avgQuality, 1.0));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Orchestrator: {ProviderCount} applicable providers for depth {Depth}")]
    private partial void LogApplicableProviders(int providerCount, TraceDepth depth);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Orchestrator: Starting provider {ProviderId}")]
    private partial void LogProviderStarting(string providerId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Orchestrator: Provider {ProviderId} completed with {Status} in {DurationMs:F0}ms")]
    private partial void LogProviderCompleted(string providerId, SourceStatus status, double durationMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "Orchestrator: Provider {ProviderId} threw an exception")]
    private partial void LogProviderException(Exception ex, string providerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Orchestrator: Complete for {NormalizedKey}, {SuccessCount} providers returned data")]
    private partial void LogOrchestratorComplete(string normalizedKey, int successCount);
}
