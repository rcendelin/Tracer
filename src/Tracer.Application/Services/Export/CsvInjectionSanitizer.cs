namespace Tracer.Application.Services.Export;

/// <summary>
/// Mitigates CSV / XLSX formula injection (CWE-1236 / OWASP "Formula Injection").
/// A cell whose content starts with <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, TAB or CR
/// would be interpreted as a formula by Excel / LibreOffice. The sanitizer prefixes
/// such cells with an apostrophe so the spreadsheet treats them as literal text.
/// </summary>
/// <remarks>
/// The apostrophe is stripped by Excel / LibreOffice on display, but remains in
/// the on-disk / on-wire representation. Downstream consumers reading the file
/// programmatically should be aware — we deliberately prefer a small visible
/// artefact over exposing users to arbitrary formula execution.
/// </remarks>
public static class CsvInjectionSanitizer
{
    // Characters that trigger formula interpretation at the start of a cell.
    private static readonly char[] DangerousLeadingChars = ['=', '+', '-', '@', '\t', '\r'];

    /// <summary>
    /// Returns the input unchanged if safe, otherwise prepends a single apostrophe.
    /// <see langword="null"/> passes through unchanged.
    /// </summary>
    public static string? Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return Array.IndexOf(DangerousLeadingChars, value[0]) >= 0
            ? "'" + value
            : value;
    }
}
