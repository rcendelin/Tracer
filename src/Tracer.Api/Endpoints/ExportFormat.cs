namespace Tracer.Api.Endpoints;

/// <summary>
/// Supported batch export formats (B-81).
/// </summary>
internal enum ExportFormat
{
    Csv,
    Xlsx,
}

internal static class ExportFormatParser
{
    public const string CsvContentType = "text/csv; charset=utf-8";
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>
    /// Parses the <c>format</c> query string value (case-insensitive). Defaults to CSV
    /// when absent. Returns <see langword="false"/> for unknown values so the endpoint
    /// can return <c>400 Bad Request</c>.
    /// </summary>
    public static bool TryParse(string? raw, out ExportFormat format)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            format = ExportFormat.Csv;
            return true;
        }

        switch (raw.Trim().ToLowerInvariant())
        {
            case "csv":
                format = ExportFormat.Csv;
                return true;
            case "xlsx":
            case "excel":
                format = ExportFormat.Xlsx;
                return true;
            default:
                format = ExportFormat.Csv;
                return false;
        }
    }

    public static string ContentType(ExportFormat format) => format switch
    {
        ExportFormat.Xlsx => XlsxContentType,
        _ => CsvContentType,
    };

    public static string FileExtension(ExportFormat format) => format switch
    {
        ExportFormat.Xlsx => "xlsx",
        _ => "csv",
    };
}
