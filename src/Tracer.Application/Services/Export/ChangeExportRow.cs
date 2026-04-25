using Tracer.Domain.Enums;

namespace Tracer.Application.Services.Export;

/// <summary>
/// Flat row projection of a <see cref="Domain.Entities.ChangeEvent"/> for
/// CSV / XLSX export. Enums are serialised as strings by CsvHelper / ClosedXML.
/// </summary>
public sealed record ChangeExportRow
{
    public Guid Id { get; init; }
    public Guid CompanyProfileId { get; init; }
    public FieldName Field { get; init; }
    public ChangeType ChangeType { get; init; }
    public ChangeSeverity Severity { get; init; }
    public string? PreviousValueJson { get; init; }
    public string? NewValueJson { get; init; }
    public string DetectedBy { get; init; } = string.Empty;
    public DateTimeOffset DetectedAt { get; init; }
    public bool IsNotified { get; init; }
}
