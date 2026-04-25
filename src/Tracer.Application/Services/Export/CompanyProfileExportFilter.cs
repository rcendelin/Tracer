namespace Tracer.Application.Services.Export;

/// <summary>
/// Filters for <see cref="ICompanyProfileExporter"/>. Mirrors
/// <see cref="Queries.ListProfiles.ListProfilesQuery"/> plus an absolute row cap.
/// </summary>
public sealed record CompanyProfileExportFilter
{
    /// <summary>Maximum rows to export. Caller is expected to pre-clamp to [1, 10000].</summary>
    public int MaxRows { get; init; } = 1_000;

    public string? Search { get; init; }
    public string? Country { get; init; }
    public double? MinConfidence { get; init; }
    public double? MaxConfidence { get; init; }
    public DateTimeOffset? ValidatedBefore { get; init; }
    public bool IncludeArchived { get; init; }
}
