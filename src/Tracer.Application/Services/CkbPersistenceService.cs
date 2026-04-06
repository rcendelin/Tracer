using Microsoft.Extensions.Logging;
using Tracer.Domain.Entities;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Services;

/// <summary>
/// Coordinates CKB persistence: profile upsert with change tracking,
/// source result recording, and change event creation.
/// </summary>
public sealed partial class CkbPersistenceService : ICkbPersistenceService
{
    private readonly ICompanyProfileRepository _profileRepository;
    private readonly IChangeEventRepository _changeEventRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConfidenceScorer _scorer;
    private readonly IChangeDetector _changeDetector;
    private readonly IProfileCacheService _cache;
    private readonly ILogger<CkbPersistenceService> _logger;

    public CkbPersistenceService(
        ICompanyProfileRepository profileRepository,
        IChangeEventRepository changeEventRepository,
        IUnitOfWork unitOfWork,
        IConfidenceScorer scorer,
        IChangeDetector changeDetector,
        IProfileCacheService cache,
        ILogger<CkbPersistenceService> logger)
    {
        _profileRepository = profileRepository;
        _changeEventRepository = changeEventRepository;
        _unitOfWork = unitOfWork;
        _scorer = scorer;
        _changeDetector = changeDetector;
        _cache = cache;
        _logger = logger;
    }

    public async Task PersistEnrichmentAsync(
        CompanyProfile profile,
        IReadOnlyCollection<(string ProviderId, ProviderResult Result)> sourceResults,
        MergeResult mergeResult,
        Guid traceRequestId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(sourceResults);
        ArgumentNullException.ThrowIfNull(mergeResult);

        // 1. Detect changes and apply merged fields to profile
        var detectionResult = _changeDetector.DetectChanges(profile, mergeResult.BestFields);

        // 2. Score overall confidence
        var overallConfidence = _scorer.ScoreOverall(profile);
        profile.SetOverallConfidence(overallConfidence);
        profile.IncrementTraceCount();

        // 3. Persist profile
        await _profileRepository.UpsertAsync(profile, cancellationToken).ConfigureAwait(false);

        // 4. Persist change events
        foreach (var changeEvent in detectionResult.Changes)
        {
            await _changeEventRepository.AddAsync(changeEvent, cancellationToken).ConfigureAwait(false);
        }

        // 5. Save all changes
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // 6. Invalidate cache (so next Quick depth fetch gets fresh data)
        await _cache.RemoveAsync(profile.NormalizedKey, cancellationToken).ConfigureAwait(false);

        LogPersistenceComplete(profile.NormalizedKey, detectionResult.TotalChanges, overallConfidence.Value);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "CKB: Persisted {NormalizedKey} with {ChangeCount} changes, confidence {Confidence:F2}")]
    private partial void LogPersistenceComplete(string normalizedKey, int changeCount, double confidence);
}
