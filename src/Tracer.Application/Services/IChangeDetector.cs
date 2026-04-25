using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.ValueObjects;

namespace Tracer.Application.Services;

/// <summary>
/// Detects field-level changes on a <see cref="CompanyProfile"/> by comparing
/// its current state against a set of newly enriched fields.
/// Returns a structured result with all detected <see cref="ChangeEvent"/>s.
/// </summary>
public interface IChangeDetector
{
    /// <summary>
    /// Compares <paramref name="newFields"/> against the current state of
    /// <paramref name="profile"/>, applies changes, and returns the result.
    /// </summary>
    /// <param name="profile">The existing CKB profile to update. Modified in-place.</param>
    /// <param name="newFields">Merged best-field values keyed by field name.</param>
    /// <returns>A <see cref="ChangeDetectionResult"/> containing all detected changes.</returns>
    ChangeDetectionResult DetectChanges(
        CompanyProfile profile,
        IReadOnlyDictionary<FieldName, TracedField<object>> newFields);
}
