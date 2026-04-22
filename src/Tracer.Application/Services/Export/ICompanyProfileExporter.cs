namespace Tracer.Application.Services.Export;

/// <summary>
/// Exports CKB company profiles to CSV or XLSX. Writes directly to the caller's
/// <see cref="Stream"/> (response body in production) — implementations must not
/// hold the entire output in memory for CSV, and must cap XLSX at <see cref="ExportLimits.MaxRows"/>.
/// </summary>
public interface ICompanyProfileExporter
{
    /// <summary>
    /// Writes profiles matching <paramref name="filter"/> as CSV (RFC 4180, UTF-8 with BOM)
    /// to <paramref name="output"/>. The caller owns <paramref name="output"/>; the exporter
    /// flushes but does not close it.
    /// </summary>
    Task WriteCsvAsync(Stream output, CompanyProfileExportFilter filter, CancellationToken cancellationToken);

    /// <summary>
    /// Writes profiles matching <paramref name="filter"/> as XLSX (Office Open XML).
    /// Caps at <see cref="ExportLimits.MaxRows"/> rows; the caller is expected to have
    /// pre-clamped <see cref="CompanyProfileExportFilter.MaxRows"/>.
    /// </summary>
    Task WriteXlsxAsync(Stream output, CompanyProfileExportFilter filter, CancellationToken cancellationToken);
}
