using MediatR;
using Microsoft.Extensions.Logging;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;

namespace Tracer.Application.Commands.OverrideField;

/// <summary>
/// Handles <see cref="OverrideFieldCommand"/> by upserting a single
/// string-typed <see cref="TracedField{T}"/> on the target CKB profile and
/// re-classifying the resulting <c>ChangeEvent</c> as a manual override.
/// </summary>
/// <remarks>
/// Source string convention is <c>"manual-override:&lt;callerFingerprint&gt;"</c>;
/// downstream consumers (FieldForce, monitoring) can therefore filter the
/// audit feed by prefix without parsing the enum.
/// </remarks>
public sealed class OverrideFieldHandler : IRequestHandler<OverrideFieldCommand, OverrideFieldResult>
{
    private readonly ICompanyProfileRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<OverrideFieldHandler> _logger;

    public OverrideFieldHandler(
        ICompanyProfileRepository repository,
        IUnitOfWork unitOfWork,
        ILogger<OverrideFieldHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(logger);
        _repository = repository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>
    /// Whitelist of fields eligible for manual override. Address / Location
    /// types are excluded — they require a typed body which is out of scope
    /// for the v1 endpoint. Officers is excluded because it is GDPR-gated.
    /// </summary>
    internal static readonly HashSet<FieldName> OverridableFields = new()
    {
        FieldName.LegalName,
        FieldName.TradeName,
        FieldName.TaxId,
        FieldName.LegalForm,
        FieldName.Phone,
        FieldName.Email,
        FieldName.Website,
        FieldName.Industry,
        FieldName.EmployeeRange,
        FieldName.EntityStatus,
        FieldName.ParentCompany,
    };

    public async Task<OverrideFieldResult> Handle(
        OverrideFieldCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!OverridableFields.Contains(request.Field))
        {
            _logger.LogWarning(
                "Manual override rejected — field {Field} is not in the override whitelist (caller {Caller}).",
                request.Field, request.CallerFingerprint);
            return OverrideFieldResult.FieldNotOverridable;
        }

        var profile = await _repository.GetByIdAsync(request.ProfileId, cancellationToken)
            .ConfigureAwait(false);
        if (profile is null)
            return OverrideFieldResult.ProfileNotFound;

        var sourceTag = $"manual-override:{request.CallerFingerprint}";
        var traced = new TracedField<string>
        {
            Value = request.NewValue.Trim(),
            Confidence = Confidence.Create(1.0),
            Source = sourceTag,
            EnrichedAt = DateTimeOffset.UtcNow,
        };

        var change = profile.UpdateField(request.Field, traced, sourceTag);
        if (change is null)
        {
            _logger.LogInformation(
                "Manual override no-op for profile {ProfileId} field {Field} (value unchanged) by {Caller}.",
                profile.Id, request.Field, request.CallerFingerprint);
            return OverrideFieldResult.NoChange;
        }

        // Reclassify so downstream consumers (FieldForce, monitoring) and
        // GET /api/profiles/{id}/history surface the entry as ManualOverride
        // rather than a regular Updated. Severity classification is unchanged
        // — a manual override of EntityStatus still goes through the Critical
        // notification handler.
        change.MarkAsManualOverride();

        _logger.LogInformation(
            "Manual override applied: profile {ProfileId} field {Field} by {Caller} (label {Label}). Reason: {Reason}",
            profile.Id, request.Field, request.CallerFingerprint,
            request.CallerLabel ?? "unlabelled", request.Reason);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OverrideFieldResult.Overridden;
    }
}
