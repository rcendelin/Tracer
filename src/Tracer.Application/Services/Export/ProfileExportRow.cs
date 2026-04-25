namespace Tracer.Application.Services.Export;

/// <summary>
/// Flat row projection of a <see cref="Domain.Entities.CompanyProfile"/> for
/// CSV / XLSX export. Strings only (or primitives) — no nested records, so CsvHelper
/// and ClosedXML can write each cell directly.
/// </summary>
/// <remarks>
/// Confidence, Source and EnrichedAt of each <c>TracedField</c> are intentionally
/// dropped for legibility — the exporter is a tabular snapshot of the golden record,
/// not a full audit. Use <c>GET /api/profiles/{id}/history</c> for provenance.
/// </remarks>
public sealed record ProfileExportRow
{
    public Guid Id { get; init; }
    public string NormalizedKey { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;
    public string? RegistrationId { get; init; }

    public string? LegalName { get; init; }
    public string? TradeName { get; init; }
    public string? TaxId { get; init; }
    public string? LegalForm { get; init; }
    public string? RegisteredAddress { get; init; }
    public string? OperatingAddress { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Website { get; init; }
    public string? Industry { get; init; }
    public string? EmployeeRange { get; init; }
    public string? EntityStatus { get; init; }
    public string? ParentCompany { get; init; }

    public double? OverallConfidence { get; init; }
    public int TraceCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastEnrichedAt { get; init; }
    public DateTimeOffset? LastValidatedAt { get; init; }
    public bool IsArchived { get; init; }
}
