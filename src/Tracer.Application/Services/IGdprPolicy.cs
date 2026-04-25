using Tracer.Domain.Enums;

namespace Tracer.Application.Services;

/// <summary>
/// Central authority for GDPR classification of enriched fields.
/// Stateless, thread-safe, registered as Singleton.
/// </summary>
/// <remarks>
/// Used by (current and future) components to decide:
/// <list type="bullet">
///   <item>Whether an enrichment path must be gated behind explicit opt-in (B-70).</item>
///   <item>Whether a field read must be audited via <see cref="IPersonalDataAccessAudit"/>.</item>
///   <item>What retention policy applies to the field value in the CKB.</item>
/// </list>
/// </remarks>
public interface IGdprPolicy
{
    /// <summary>
    /// Classifies the given <paramref name="field"/> as firmographic or personal data.
    /// </summary>
    FieldClassification Classify(FieldName field);

    /// <summary>
    /// Convenience predicate: <c>true</c> when the field is classified as personal data.
    /// </summary>
    bool IsPersonalData(FieldName field);

    /// <summary>
    /// Indicates whether enrichment of the given field requires explicit data-subject
    /// opt-in. Currently equivalent to <see cref="IsPersonalData"/>; the separation
    /// exists so that future legal bases (e.g. legitimate interest) can be expressed
    /// without a breaking API change.
    /// </summary>
    bool RequiresConsent(FieldName field);

    /// <summary>
    /// Maximum age of a personal-data field value before the retention job
    /// is obliged to soft-delete it.
    /// </summary>
    TimeSpan PersonalDataRetention { get; }
}
