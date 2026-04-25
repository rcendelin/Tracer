using System.Collections.Concurrent;
using System.Reflection;
using ClosedXML.Excel;

namespace Tracer.Application.Services.Export;

/// <summary>
/// Internal helper for writing records into a <see cref="IXLWorksheet"/>. Caches
/// the ordered property list per row type so we don't reflect on every row.
/// </summary>
internal static class XlsxWriter
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new();

    public static void WriteHeaders<T>(IXLWorksheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var props = GetProperties(typeof(T));
        for (var i = 0; i < props.Length; i++)
        {
            var headerCell = sheet.Cell(1, i + 1);
            headerCell.Value = props[i].Name;
            headerCell.Style.Font.Bold = true;
        }
    }

    public static void WriteRow<T>(IXLWorksheet sheet, int rowIndex, T record)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(record);

        var props = GetProperties(typeof(T));
        for (var i = 0; i < props.Length; i++)
        {
            var cell = sheet.Cell(rowIndex, i + 1);
            SetCellValue(cell, props[i].GetValue(record));
        }
    }

    private static void SetCellValue(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                cell.Clear();
                return;
            case string s:
                cell.SetValue(s);
                return;
            case bool b:
                cell.SetValue(b);
                return;
            case Guid g:
                cell.SetValue(g.ToString());
                return;
            case DateTimeOffset dto:
                cell.SetValue(dto.UtcDateTime);
                cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
                return;
            case DateTime dt:
                cell.SetValue(dt);
                cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
                return;
            case double d:
                cell.SetValue(d);
                return;
            case int i:
                cell.SetValue(i);
                return;
            case long l:
                cell.SetValue(l);
                return;
            case Enum e:
                cell.SetValue(e.ToString());
                return;
            default:
                cell.SetValue(value.ToString());
                return;
        }
    }

    private static PropertyInfo[] GetProperties(Type type) =>
        PropertyCache.GetOrAdd(type, static t => t
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead)
            .ToArray());
}
