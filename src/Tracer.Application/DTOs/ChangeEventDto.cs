using Tracer.Domain.Enums;

namespace Tracer.Application.DTOs;

/// <summary>
/// DTO for a field-level change event.
/// </summary>
public sealed record ChangeEventDto
{
    /// <summary>Change event identifier (primary key in <c>ChangeEvents</c>).</summary>
    public required Guid Id { get; init; }

    /// <summary>Profile whose field changed.</summary>
    public required Guid CompanyProfileId { get; init; }

    /// <summary>Which enriched field changed (<c>LegalName</c>, <c>EntityStatus</c>, ...).</summary>
    public required FieldName Field { get; init; }

    /// <summary>Nature of the change (<c>Added</c>, <c>Updated</c>, <c>Removed</c>).</summary>
    public required ChangeType ChangeType { get; init; }

    /// <summary>Severity bucket driving notification routing (Critical → Service Bus + SignalR, Major → SignalR only).</summary>
    public required ChangeSeverity Severity { get; init; }

    /// <summary>Previous value serialized as JSON (may be null for Added).</summary>
    public string? PreviousValueJson { get; init; }

    /// <summary>New value serialized as JSON (may be null for Removed).</summary>
    public string? NewValueJson { get; init; }

    /// <summary>Source / provider that detected the change (e.g. <c>"ares"</c>, <c>"companies-house"</c>, <c>"revalidation"</c>).</summary>
    public required string DetectedBy { get; init; }

    /// <summary>UTC timestamp when the change was detected.</summary>
    public required DateTimeOffset DetectedAt { get; init; }

    /// <summary>True when downstream notification (Service Bus topic / SignalR) has been dispatched.</summary>
    public bool IsNotified { get; init; }
}
