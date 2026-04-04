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
}
