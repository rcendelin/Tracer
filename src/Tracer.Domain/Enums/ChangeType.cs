namespace Tracer.Domain.Enums;

/// <summary>
/// Describes the nature of a field-level change detected during enrichment or re-validation.
/// </summary>
public enum ChangeType
{
    /// <summary>Field had no value before; a value was set for the first time.</summary>
    Created = 0,

    /// <summary>Field value changed from a previously known value.</summary>
    Updated = 1,

    /// <summary>Field value was cleared (e.g. website taken down, phone removed).</summary>
    Deleted = 2,

    /// <summary>
    /// Field value was set or changed by an authenticated operator via the
    /// <c>PUT /api/profiles/{id}/fields/{field}</c> endpoint (B-85). Carries the
    /// caller fingerprint in <see cref="Tracer.Domain.Entities.ChangeEvent.DetectedBy"/>
    /// (server-derived from the API key — never from the request body).
    /// </summary>
    ManualOverride = 3,
}
