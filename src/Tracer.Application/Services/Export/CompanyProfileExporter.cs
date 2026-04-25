using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Services.Export;

/// <summary>
/// Default <see cref="ICompanyProfileExporter"/> — streams rows from the repository,
/// maps to <see cref="ProfileExportRow"/>, and writes CSV / XLSX to the caller's
/// <see cref="Stream"/>.
/// </summary>
internal sealed class CompanyProfileExporter : ICompanyProfileExporter
{
    private readonly ICompanyProfileRepository _repository;

    public CompanyProfileExporter(ICompanyProfileRepository repository)
    {
        _repository = repository;
    }

    public async Task WriteCsvAsync(Stream output, CompanyProfileExportFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(filter);
        cancellationToken.ThrowIfCancellationRequested();

        var maxRows = ExportLimits.Clamp(filter.MaxRows);

        // UTF-8 with BOM so Excel on Windows auto-detects the encoding for non-ASCII
        // content (e.g. "Škoda", "Müller"). leaveOpen=true — caller owns the stream.
        await using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), bufferSize: 8192, leaveOpen: true);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            // CsvHelper quotes automatically when content has commas / quotes / newlines;
            // InvariantCulture ensures decimal points rather than locale-specific commas.
        };
        using var csv = new CsvWriter(writer, config, leaveOpen: true);

        csv.WriteHeader<ProfileExportRow>();
        await csv.NextRecordAsync().ConfigureAwait(false);

        var written = 0;
        await foreach (var profile in _repository.StreamAsync(
            maxRows,
            filter.Search, filter.Country, filter.MinConfidence, filter.MaxConfidence,
            filter.ValidatedBefore, filter.IncludeArchived,
            cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            csv.WriteRecord(profile.ToExportRow());
            await csv.NextRecordAsync().ConfigureAwait(false);
            written++;
            // Defence-in-depth: repository caps at maxRows, but re-enforce here too.
            if (written >= maxRows)
                break;
        }

        await csv.FlushAsync().ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteXlsxAsync(Stream output, CompanyProfileExportFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(filter);
        cancellationToken.ThrowIfCancellationRequested();

        var maxRows = ExportLimits.Clamp(filter.MaxRows);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Profiles");

        XlsxWriter.WriteHeaders<ProfileExportRow>(sheet);

        var rowIndex = 2; // header is row 1
        await foreach (var profile in _repository.StreamAsync(
            maxRows,
            filter.Search, filter.Country, filter.MinConfidence, filter.MaxConfidence,
            filter.ValidatedBefore, filter.IncludeArchived,
            cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            XlsxWriter.WriteRow(sheet, rowIndex++, profile.ToExportRow());
            if (rowIndex - 1 > maxRows)
                break;
        }

        sheet.Columns().AdjustToContents(1, Math.Min(rowIndex, 500));
        workbook.SaveAs(output);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
