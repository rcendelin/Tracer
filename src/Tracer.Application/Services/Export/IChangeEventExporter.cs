namespace Tracer.Application.Services.Export;

/// <summary>
/// Exports change events to CSV or XLSX. See <see cref="ICompanyProfileExporter"/>
/// for streaming / ownership semantics.
/// </summary>
public interface IChangeEventExporter
{
    /// <summary>
    /// Writes change events matching <paramref name="filter"/> as CSV (RFC 4180, UTF-8 with BOM).
    /// </summary>
    Task WriteCsvAsync(Stream output, ChangeEventExportFilter filter, CancellationToken cancellationToken);

    /// <summary>
    /// Writes change events matching <paramref name="filter"/> as XLSX.
    /// Caps at <see cref="ExportLimits.MaxRows"/> rows.
    /// </summary>
    Task WriteXlsxAsync(Stream output, ChangeEventExportFilter filter, CancellationToken cancellationToken);
}
