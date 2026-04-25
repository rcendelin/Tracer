using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Services.Export;

/// <summary>
/// Default <see cref="IChangeEventExporter"/> — streams rows from the repository,
/// maps to <see cref="ChangeExportRow"/>, and writes CSV / XLSX to the caller's
/// <see cref="Stream"/>.
/// </summary>
internal sealed class ChangeEventExporter : IChangeEventExporter
{
    private readonly IChangeEventRepository _repository;

    public ChangeEventExporter(IChangeEventRepository repository)
    {
        _repository = repository;
    }

    public async Task WriteCsvAsync(Stream output, ChangeEventExportFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(filter);
        cancellationToken.ThrowIfCancellationRequested();

        var maxRows = ExportLimits.Clamp(filter.MaxRows);

        await using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), bufferSize: 8192, leaveOpen: true);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture);
        using var csv = new CsvWriter(writer, config, leaveOpen: true);

        csv.WriteHeader<ChangeExportRow>();
        await csv.NextRecordAsync().ConfigureAwait(false);

        var written = 0;
        await foreach (var evt in _repository.StreamAsync(
            maxRows, filter.Severity, filter.ProfileId, filter.From, filter.To,
            cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            csv.WriteRecord(evt.ToExportRow());
            await csv.NextRecordAsync().ConfigureAwait(false);
            written++;
            if (written >= maxRows)
                break;
        }

        await csv.FlushAsync().ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteXlsxAsync(Stream output, ChangeEventExportFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(filter);
        cancellationToken.ThrowIfCancellationRequested();

        var maxRows = ExportLimits.Clamp(filter.MaxRows);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Changes");

        XlsxWriter.WriteHeaders<ChangeExportRow>(sheet);

        var rowIndex = 2;
        await foreach (var evt in _repository.StreamAsync(
            maxRows, filter.Severity, filter.ProfileId, filter.From, filter.To,
            cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            XlsxWriter.WriteRow(sheet, rowIndex++, evt.ToExportRow());
            if (rowIndex - 1 > maxRows)
                break;
        }

        sheet.Columns().AdjustToContents(1, Math.Min(rowIndex, 500));
        workbook.SaveAs(output);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
