using Tracer.Contracts.Enums;

namespace Tracer.Contracts.Models;

/// <summary>
/// Details of a detected field-level change in a company profile.
/// </summary>
public sealed record ChangeEventContract
{
    /// <summary>Unique change event identifier. Use for idempotency on the consumer side.</summary>
    public required Guid Id { get; init; }

    /// <summary>The field that changed.</summary>
    public required FieldName Field { get; init; }

    /// <summary>Nature of the change (created, updated, or deleted).</summary>
    public required ChangeType ChangeType { get; init; }

    /// <summary>Business impact severity of this change.</summary>
    public required ChangeSeverity Severity { get; init; }

    /// <summary>
    /// JSON-serialised previous value of the field.
    /// <see langword="null"/> if <see cref="ChangeType"/> is <see cref="ChangeType.Created"/>.
    /// </summary>
    public string? PreviousValueJson { get; init; }

    /// <summary>
    /// JSON-serialised new value of the field.
    /// <see langword="null"/> if <see cref="ChangeType"/> is <see cref="ChangeType.Deleted"/>.
    /// </summary>
    public string? NewValueJson { get; init; }

    /// <summary>Identifier of the enrichment provider that detected this change.</summary>
    public required string DetectedBy { get; init; }

    /// <summary>UTC timestamp when the change was detected.</summary>
    public required DateTimeOffset DetectedAt { get; init; }
}
