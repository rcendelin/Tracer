using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;

namespace Tracer.Application.Services;

/// <summary>
/// Deep re-validation (B-67): runs the full waterfall enrichment pipeline at
/// <see cref="TraceDepth.Standard"/> against an existing CKB profile whenever
/// the <see cref="IFieldTtlPolicy"/> reports at least
/// <see cref="DeepRevalidationOptions.Threshold"/> expired fields. Creates a
/// synthetic <see cref="TraceRequest"/> (<c>Source = "revalidation"</c>) so the
/// normal merge / change detection / persistence path is reused.
/// </summary>
/// <remarks>
/// Trade-off vs. <see cref="IRevalidationRunner"/> doc guarantee: the runner
/// intentionally saves during its own work because <see cref="IWaterfallOrchestrator"/>
/// already persists the profile internally (via
/// <see cref="ICkbPersistenceService"/>), so the no-save rule only applies to
/// a purely in-memory runner such as lightweight mode (B-66). Deep mode owns
/// two additional save checkpoints: the InProgress <see cref="TraceRequest"/>
/// (so it has a persisted ID), and the closing
/// <see cref="ValidationRecord"/> / <see cref="CompanyProfile.MarkValidated"/> /
/// <see cref="TraceRequest.Complete"/> trio.
/// <para>
/// Runs in the <see cref="IServiceScope"/> created by
/// <c>RevalidationScheduler</c> per profile, so all scoped dependencies
/// (<see cref="ITraceRequestRepository"/>, <see cref="IWaterfallOrchestrator"/>,
/// <see cref="IUnitOfWork"/>, etc.) share the same <c>TracerDbContext</c>.
/// </para>
/// </remarks>
internal sealed partial class DeepRevalidationRunner : IRevalidationRunner
{
    /// <summary>
    /// Tag identifying the synthetic <see cref="TraceRequest"/> as originating
    /// from a re-validation run. Matches the documented values on
    /// <see cref="TraceRequest.Source"/>.
    /// </summary>
    internal const string TraceSourceTag = "revalidation";

    /// <summary>
    /// <see cref="ValidationRecord.ProviderId"/> marker for deep runs.
    /// Individual providers executed by the waterfall are visible via
    /// <see cref="ICkbPersistenceService"/> source result records.
    /// </summary>
    internal const string ValidationProviderId = "revalidation-waterfall";

    private readonly IFieldTtlPolicy _ttlPolicy;
    private readonly IWaterfallOrchestrator _orchestrator;
    private readonly ITraceRequestRepository _traceRequestRepository;
    private readonly IValidationRecordRepository _validationRecordRepository;
    private readonly IChangeEventRepository _changeEventRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly DeepRevalidationOptions _options;
    private readonly ILogger<DeepRevalidationRunner> _logger;

    public DeepRevalidationRunner(
        IFieldTtlPolicy ttlPolicy,
        IWaterfallOrchestrator orchestrator,
        ITraceRequestRepository traceRequestRepository,
        IValidationRecordRepository validationRecordRepository,
        IChangeEventRepository changeEventRepository,
        IUnitOfWork unitOfWork,
        IOptions<DeepRevalidationOptions> options,
        ILogger<DeepRevalidationRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _ttlPolicy = ttlPolicy;
        _orchestrator = orchestrator;
        _traceRequestRepository = traceRequestRepository;
        _validationRecordRepository = validationRecordRepository;
        _changeEventRepository = changeEventRepository;
        _unitOfWork = unitOfWork;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Testing seam — overrides <see cref="DateTimeOffset.UtcNow"/> when the
    /// runner asks the TTL policy for expired fields. Mirrors the
    /// <c>Clock</c> property on <c>RevalidationScheduler</c>.
    /// </summary>
    internal Func<DateTimeOffset> Clock { get; init; } = static () => DateTimeOffset.UtcNow;

    public async Task<RevalidationOutcome> RunAsync(CompanyProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();

        var now = Clock();
        var expiredFields = _ttlPolicy.GetExpiredFields(profile, now);

        if (expiredFields.Count < _options.Threshold)
        {
            LogBelowThreshold(profile.Id, expiredFields.Count, _options.Threshold);
            return RevalidationOutcome.Deferred;
        }

        // Feasibility gate: deep re-trace needs RegistrationId + Country so the
        // waterfall can target the correct registry. Without either we can't
        // reliably identify the company again; defer (neither success nor failure).
        if (string.IsNullOrWhiteSpace(profile.RegistrationId) ||
            string.IsNullOrWhiteSpace(profile.Country))
        {
            LogMissingIdentifiers(profile.Id, expiredFields.Count);
            return RevalidationOutcome.Deferred;
        }

        var stopwatch = Stopwatch.StartNew();

        var changesBefore = await _changeEventRepository
            .CountByProfileAsync(profile.Id, cancellationToken)
            .ConfigureAwait(false);

        var traceRequest = BuildSyntheticRequest(profile);
        traceRequest.MarkInProgress();

        await _traceRequestRepository.AddAsync(traceRequest, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var refreshed = await _orchestrator
                .ExecuteAsync(traceRequest, cancellationToken)
                .ConfigureAwait(false);

            var changesAfter = await _changeEventRepository
                .CountByProfileAsync(refreshed.Id, cancellationToken)
                .ConfigureAwait(false);
            var fieldsChanged = Math.Max(0, changesAfter - changesBefore);

            refreshed.MarkValidated();

            var record = new ValidationRecord(
                companyProfileId: refreshed.Id,
                validationType: ValidationType.Deep,
                fieldsChecked: expiredFields.Count,
                fieldsChanged: fieldsChanged,
                providerId: ValidationProviderId,
                durationMs: (long)stopwatch.Elapsed.TotalMilliseconds);
            await _validationRecordRepository.AddAsync(record, cancellationToken).ConfigureAwait(false);

            traceRequest.Complete(refreshed.Id, refreshed.OverallConfidence ?? Confidence.Zero);
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();
            LogDeepCompleted(profile.Id, expiredFields.Count, fieldsChanged, stopwatch.Elapsed.TotalMilliseconds);
            return RevalidationOutcome.Deep;
        }
        catch (OperationCanceledException)
        {
            // Caller (scheduler) discriminates per-profile timeout vs. host cancellation;
            // best-effort mark the synthetic request as failed so it isn't left InProgress
            // forever. Use CancellationToken.None so the persist attempt survives the same
            // cancellation that triggered us. We log and swallow secondary failures — the
            // outer OperationCanceledException is re-thrown unchanged.
            await TryMarkTraceFailedAsync(traceRequest, "Re-validation cancelled or timed out.").ConfigureAwait(false);
            throw;
        }
#pragma warning disable CA1031 // Intentional: one failing profile must not crash the scheduler
        catch (Exception ex)
        {
            // Exception type name only — ex.Message can leak internal paths / connection strings (CWE-209).
            LogDeepFailed(profile.Id, ex.GetType().Name);
            await TryMarkTraceFailedAsync(traceRequest, "Re-validation waterfall failed.").ConfigureAwait(false);
            return RevalidationOutcome.Failed;
        }
#pragma warning restore CA1031
    }

    private async Task TryMarkTraceFailedAsync(TraceRequest traceRequest, string reason)
    {
        try
        {
            if (traceRequest.Status == TraceStatus.InProgress)
            {
                traceRequest.Fail(reason);
                await _unitOfWork.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
#pragma warning disable CA1031 // Best-effort persistence of failure state; swallow secondary errors
        catch (Exception ex)
        {
            LogFailurePersistError(traceRequest.Id, ex.GetType().Name);
        }
#pragma warning restore CA1031
    }

    private static TraceRequest BuildSyntheticRequest(CompanyProfile profile)
    {
        // RegistrationId and Country are guaranteed non-empty by the feasibility gate
        // in RunAsync above. The remaining hints are pulled from the CKB profile when
        // available so the waterfall has every bit of context it had during the
        // original enrichment.
        return new TraceRequest(
            companyName: profile.LegalName?.Value ?? profile.TradeName?.Value,
            phone: profile.Phone?.Value,
            email: profile.Email?.Value,
            website: profile.Website?.Value,
            address: null,
            city: null,
            country: profile.Country,
            registrationId: profile.RegistrationId,
            taxId: profile.TaxId?.Value,
            industryHint: profile.Industry?.Value,
            depth: TraceDepth.Standard,
            callbackUrl: null,
            source: TraceSourceTag);
    }

    // ── LoggerMessage source generators (no PII: only IDs, counts, durations) ──

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Deep revalidation deferred for profile {ProfileId}: {ExpiredCount} expired field(s) < threshold {Threshold}")]
    private partial void LogBelowThreshold(Guid profileId, int expiredCount, int threshold);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Deep revalidation deferred for profile {ProfileId}: missing RegistrationId or Country ({ExpiredCount} expired)")]
    private partial void LogMissingIdentifiers(Guid profileId, int expiredCount);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Deep revalidation completed for profile {ProfileId}: expired={ExpiredCount}, changed={FieldsChanged}, duration={DurationMs:F1}ms")]
    private partial void LogDeepCompleted(Guid profileId, int expiredCount, int fieldsChanged, double durationMs);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Deep revalidation failed for profile {ProfileId} ({ExceptionType})")]
    private partial void LogDeepFailed(Guid profileId, string exceptionType);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Deep revalidation: could not persist failed state for trace {TraceId} ({ExceptionType})")]
    private partial void LogFailurePersistError(Guid traceId, string exceptionType);
}
