using Tracer.Domain.Enums;

namespace Tracer.Application.Services.Export;

/// <summary>
/// Filters for <see cref="IChangeEventExporter"/>. Mirrors
/// <see cref="Queries.ListChanges.ListChangesQuery"/> plus a date range and
/// absolute row cap.
/// </summary>
public sealed record ChangeEventExportFilter
{
    /// <summary>Maximum rows to export. Caller is expected to pre-clamp to [1, 10000].</summary>
    public int MaxRows { get; init; } = 1_000;

    public ChangeSeverity? Severity { get; init; }
    public Guid? ProfileId { get; init; }

    /// <summary>Inclusive lower bound on <see cref="Domain.Entities.ChangeEvent.DetectedAt"/>.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Exclusive upper bound on <see cref="Domain.Entities.ChangeEvent.DetectedAt"/>.</summary>
    public DateTimeOffset? To { get; init; }
}
