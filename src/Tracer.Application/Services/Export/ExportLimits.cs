namespace Tracer.Application.Services.Export;

/// <summary>
/// Shared numeric constants for batch export (B-81).
/// </summary>
public static class ExportLimits
{
    /// <summary>Absolute maximum number of rows exportable in a single request.</summary>
    public const int MaxRows = 10_000;

    /// <summary>Default rows when the caller does not specify <c>maxRows</c>.</summary>
    public const int DefaultRows = 1_000;

    /// <summary>
    /// Clamps <paramref name="requested"/> to <c>[1, <see cref="MaxRows"/>]</c>.
    /// Null or non-positive values return <see cref="DefaultRows"/>.
    /// </summary>
    public static int Clamp(int? requested)
    {
        if (requested is null or <= 0)
            return DefaultRows;
        return Math.Min(requested.Value, MaxRows);
    }
}
