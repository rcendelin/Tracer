namespace Tracer.Contracts.Enums;

/// <summary>
/// Describes the nature of a field-level change detected during enrichment or re-validation.
/// </summary>
public enum ChangeType
{
    /// <summary>Field had no value before; a value was populated for the first time.</summary>
    Created = 0,

    /// <summary>Field value changed from a previously known value.</summary>
    Updated = 1,

    /// <summary>
    /// Field value was cleared (e.g. website taken down, phone number removed from registry).
    /// </summary>
    Deleted = 2,

    /// <summary>
    /// Field value was set or changed by an authenticated operator (B-85).
    /// Carries the caller fingerprint as the source identifier rather than a
    /// provider id. Mirrors <c>Tracer.Domain.Enums.ChangeType.ManualOverride</c>.
    /// </summary>
    ManualOverride = 3,
}
