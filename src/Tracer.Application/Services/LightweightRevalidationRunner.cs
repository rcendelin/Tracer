using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Services;

/// <summary>
/// Lightweight re-validation (B-66): for profiles with a small number of
/// expired fields, this runner refreshes their <c>EnrichedAt</c> timestamp
/// in-memory without calling any provider. The semantic is "the value is
/// still correct as of now" — the operator pays the daily off-peak sweep
/// cost without paying for an external API round-trip.
/// </summary>
/// <remarks>
/// <para>
/// **Save boundary.** This runner does NOT call <see cref="IUnitOfWork.SaveChangesAsync"/>
/// — the caller (<c>RevalidationScheduler</c>) is responsible for flushing the
/// scope's DbContext at the end of the per-profile work. This matches the
/// <see cref="IRevalidationRunner"/> contract for lightweight implementations.
/// </para>
/// <para>
/// **No provider call.** Lightweight does not validate against any registry —
/// if the user wants a true single-provider re-check they should use
/// <c>POST /api/profiles/{id}/revalidate</c>, which queues a manual run and
/// then enters the deep waterfall via <see cref="CompositeRevalidationRunner"/>.
/// </para>
/// </remarks>
internal sealed class LightweightRevalidationRunner
{
    /// <summary>
    /// <see cref="ValidationRecord.ProviderId"/> marker for lightweight runs.
    /// </summary>
    internal const string ValidationProviderId = "revalidation-lightweight";

    private readonly IFieldTtlPolicy _ttlPolicy;
    private readonly IValidationRecordRepository _validationRecordRepository;
    private readonly ILogger<LightweightRevalidationRunner> _logger;

    public LightweightRevalidationRunner(
        IFieldTtlPolicy ttlPolicy,
        IValidationRecordRepository validationRecordRepository,
        ILogger<LightweightRevalidationRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(ttlPolicy);
        ArgumentNullException.ThrowIfNull(validationRecordRepository);
        ArgumentNullException.ThrowIfNull(logger);
        _ttlPolicy = ttlPolicy;
        _validationRecordRepository = validationRecordRepository;
        _logger = logger;
    }

    public async Task<RevalidationOutcome> RunAsync(
        CompanyProfile profile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
        var now = DateTimeOffset.UtcNow;
        var expiredFields = _ttlPolicy.GetExpiredFields(profile, now);

        if (expiredFields.Count == 0)
        {
            // Nothing to do — TTLs all green.
            _logger.LogDebug(
                "Lightweight re-validation for profile {ProfileId} skipped (no expired fields).",
                profile.Id);
            return RevalidationOutcome.Succeeded;
        }

        // In-memory only: refresh timestamps. CompanyProfile's internal switch
        // ignores fields that are GDPR-gated (Officers) and the non-TracedField
        // RegistrationId, so the call is safe for the full FieldName surface.
        foreach (var field in expiredFields)
            profile.RefreshFieldEnrichedAt(field, now);

        // MarkValidated is the aggregate-level "this profile was looked at"
        // signal. Same semantics whether deep or lightweight refreshed it.
        profile.MarkValidated();

        // Audit record. AddAsync registers the entity with the scoped DbContext;
        // the scheduler flushes it together with the profile state on exit.
        // Lightweight always reports FieldsChanged = 0 — by definition the
        // value did not change; we only refreshed the timestamp.
        stopwatch.Stop();
        var record = new ValidationRecord(
            companyProfileId: profile.Id,
            validationType: ValidationType.Lightweight,
            fieldsChecked: expiredFields.Count,
            fieldsChanged: 0,
            providerId: ValidationProviderId,
            durationMs: stopwatch.ElapsedMilliseconds);
        await _validationRecordRepository.AddAsync(record, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Lightweight re-validation refreshed {FieldCount} fields on profile {ProfileId}.",
            expiredFields.Count, profile.Id);

        return RevalidationOutcome.Succeeded;
    }
}
