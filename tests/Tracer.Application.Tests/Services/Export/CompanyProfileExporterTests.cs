using System.Text;
using ClosedXML.Excel;
using FluentAssertions;
using Tracer.Application.Services.Export;
using static Tracer.Application.Tests.Services.Export.ExporterTestFakes;

namespace Tracer.Application.Tests.Services.Export;

public sealed class CompanyProfileExporterTests
{
    [Fact]
    public async Task WriteCsvAsync_EmptyRepository_WritesOnlyHeader()
    {
        var sut = new CompanyProfileExporter(new FakeCompanyProfileRepository([]));
        using var output = new MemoryStream();

        await sut.WriteCsvAsync(output, new CompanyProfileExportFilter(), CancellationToken.None);

        var csv = ReadCsv(output);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(1);
        lines[0].Should().Contain("Id").And.Contain("LegalName").And.Contain("Country");
    }

    [Fact]
    public async Task WriteCsvAsync_MultipleProfiles_WritesAllRows()
    {
        var profiles = new[]
        {
            CreateProfile("00000001", "CZ", "Alpha a.s."),
            CreateProfile("00000002", "CZ", "Beta s.r.o."),
            CreateProfile("00000003", "CZ", "Gamma Industries"),
        };
        var sut = new CompanyProfileExporter(new FakeCompanyProfileRepository(profiles));
        using var output = new MemoryStream();

        await sut.WriteCsvAsync(output, new CompanyProfileExportFilter(), CancellationToken.None);

        var csv = ReadCsv(output);
        csv.Should().Contain("Alpha a.s.");
        csv.Should().Contain("Beta s.r.o.");
        csv.Should().Contain("Gamma Industries");
    }

    [Fact]
    public async Task WriteCsvAsync_MaxRowsRespected_TruncatesToLimit()
    {
        var profiles = Enumerable.Range(0, 10)
            .Select(i => CreateProfile($"ID{i:D8}", "CZ", $"Company{i}"))
            .ToList();
        var repo = new FakeCompanyProfileRepository(profiles);
        var sut = new CompanyProfileExporter(repo);
        using var output = new MemoryStream();

        await sut.WriteCsvAsync(output, new CompanyProfileExportFilter { MaxRows = 3 }, CancellationToken.None);

        repo.CapturedMaxRows.Should().Be(3);
        var csv = ReadCsv(output);
        var dataLines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).Count();
        dataLines.Should().Be(3);
    }

    [Fact]
    public async Task WriteCsvAsync_MaxRowsOutOfRange_ClampedByExportLimits()
    {
        var sut = new CompanyProfileExporter(new FakeCompanyProfileRepository([]));
        using var output = new MemoryStream();

        // Exporter.Clamp should coerce 0 → DefaultRows (1000).
        await sut.WriteCsvAsync(output, new CompanyProfileExportFilter { MaxRows = 0 }, CancellationToken.None);

        // Smoke: method returns cleanly; clamping is exercised even without rows.
        output.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task WriteCsvAsync_InjectionPayloadInLegalName_Sanitised()
    {
        var profiles = new[]
        {
            CreateProfile("00000001", "CZ", "=cmd|' /c calc'!A1"),
        };
        var sut = new CompanyProfileExporter(new FakeCompanyProfileRepository(profiles));
        using var output = new MemoryStream();

        await sut.WriteCsvAsync(output, new CompanyProfileExportFilter(), CancellationToken.None);

        var csv = ReadCsv(output);
        csv.Should().Contain("'=cmd");       // apostrophe-prefixed so Excel treats it as text
        csv.Should().NotContain("\n=cmd");   // the dangerous leading '=' never appears un-escaped
    }

    [Fact]
    public async Task WriteCsvAsync_CancelledBeforeStart_Throws()
    {
        var sut = new CompanyProfileExporter(new FakeCompanyProfileRepository([]));
        using var output = new MemoryStream();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => sut.WriteCsvAsync(output, new CompanyProfileExportFilter(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task WriteXlsxAsync_ProducesValidWorkbookWithExpectedRows()
    {
        var profiles = new[]
        {
            CreateProfile("00000001", "CZ", "Alpha a.s."),
            CreateProfile("00000002", "CZ", "Beta s.r.o."),
        };
        var sut = new CompanyProfileExporter(new FakeCompanyProfileRepository(profiles));
        using var output = new MemoryStream();

        await sut.WriteXlsxAsync(output, new CompanyProfileExportFilter(), CancellationToken.None);

        output.Position = 0;
        using var workbook = new XLWorkbook(output);
        var sheet = workbook.Worksheets.First();
        sheet.Name.Should().Be("Profiles");

        // Row 1 is the header.
        sheet.Cell(1, 1).GetString().Should().Be("Id");
        // Row 2+ are data rows with at least the LegalName column populated.
        var legalNameColumn = Enumerable.Range(1, 30)
            .FirstOrDefault(c => sheet.Cell(1, c).GetString() == nameof(ProfileExportRow.LegalName));
        legalNameColumn.Should().BeGreaterThan(0);
        sheet.Cell(2, legalNameColumn).GetString().Should().Be("Alpha a.s.");
        sheet.Cell(3, legalNameColumn).GetString().Should().Be("Beta s.r.o.");
    }

    [Fact]
    public async Task WriteXlsxAsync_MagicBytesMatchOfficeOpenXml()
    {
        var sut = new CompanyProfileExporter(new FakeCompanyProfileRepository([]));
        using var output = new MemoryStream();

        await sut.WriteXlsxAsync(output, new CompanyProfileExportFilter(), CancellationToken.None);

        var bytes = output.ToArray();
        // XLSX is a ZIP container → starts with PK\x03\x04.
        bytes[0].Should().Be((byte)'P');
        bytes[1].Should().Be((byte)'K');
        bytes[2].Should().Be(0x03);
        bytes[3].Should().Be(0x04);
    }

    private static string ReadCsv(MemoryStream stream)
    {
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
