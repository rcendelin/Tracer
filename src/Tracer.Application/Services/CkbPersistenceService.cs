using Microsoft.Extensions.Logging;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;

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
    private readonly ILogger<CkbPersistenceService> _logger;

    public CkbPersistenceService(
        ICompanyProfileRepository profileRepository,
        IChangeEventRepository changeEventRepository,
        IUnitOfWork unitOfWork,
        IConfidenceScorer scorer,
        ILogger<CkbPersistenceService> logger)
    {
        _profileRepository = profileRepository;
        _changeEventRepository = changeEventRepository;
        _unitOfWork = unitOfWork;
        _scorer = scorer;
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

        // 1. Apply merged fields to profile, collecting change events
        var changeEvents = ApplyMergedFields(profile, mergeResult);

        // 2. Score overall confidence
        var overallConfidence = _scorer.ScoreOverall(profile);
        profile.SetOverallConfidence(overallConfidence);
        profile.IncrementTraceCount();

        // 3. Persist profile
        await _profileRepository.UpsertAsync(profile, cancellationToken).ConfigureAwait(false);

        // 4. Persist change events
        foreach (var changeEvent in changeEvents)
        {
            await _changeEventRepository.AddAsync(changeEvent, cancellationToken).ConfigureAwait(false);
        }

        // 5. Save all changes
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        LogPersistenceComplete(profile.NormalizedKey, changeEvents.Count, overallConfidence.Value);
    }

    private static List<ChangeEvent> ApplyMergedFields(
        CompanyProfile profile,
        MergeResult mergeResult)
    {
        var changeEvents = new List<ChangeEvent>();

        foreach (var (fieldName, tracedField) in mergeResult.BestFields)
        {
            ChangeEvent? changeEvent = null;

            switch (fieldName)
            {
                case FieldName.RegisteredAddress or FieldName.OperatingAddress
                    when tracedField.Value is Address addr:
                    changeEvent = profile.UpdateField(fieldName, new TracedField<Address>
                    {
                        Value = addr,
                        Confidence = tracedField.Confidence,
                        Source = tracedField.Source,
                        EnrichedAt = tracedField.EnrichedAt,
                    }, tracedField.Source);
                    break;

                case FieldName.Location when tracedField.Value is GeoCoordinate geo:
                    changeEvent = profile.UpdateField(fieldName, new TracedField<GeoCoordinate>
                    {
                        Value = geo,
                        Confidence = tracedField.Confidence,
                        Source = tracedField.Source,
                        EnrichedAt = tracedField.EnrichedAt,
                    }, tracedField.Source);
                    break;

                case var _ when tracedField.Value is string strVal:
                    changeEvent = profile.UpdateField(fieldName, new TracedField<string>
                    {
                        Value = strVal,
                        Confidence = tracedField.Confidence,
                        Source = tracedField.Source,
                        EnrichedAt = tracedField.EnrichedAt,
                    }, tracedField.Source);
                    break;
            }

            if (changeEvent is not null)
                changeEvents.Add(changeEvent);
        }

        return changeEvents;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "CKB: Persisted {NormalizedKey} with {ChangeCount} changes, confidence {Confidence:F2}")]
    private partial void LogPersistenceComplete(string normalizedKey, int changeCount, double confidence);
}
