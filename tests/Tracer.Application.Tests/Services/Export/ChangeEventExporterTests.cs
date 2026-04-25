using System.Text;
using ClosedXML.Excel;
using FluentAssertions;
using Tracer.Application.Services.Export;
using Tracer.Domain.Enums;
using static Tracer.Application.Tests.Services.Export.ExporterTestFakes;

namespace Tracer.Application.Tests.Services.Export;

public sealed class ChangeEventExporterTests
{
    [Fact]
    public async Task WriteCsvAsync_EmptyRepository_WritesOnlyHeader()
    {
        var sut = new ChangeEventExporter(new FakeChangeEventRepository([]));
        using var output = new MemoryStream();

        await sut.WriteCsvAsync(output, new ChangeEventExportFilter(), CancellationToken.None);

        var csv = ReadCsv(output);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(1);
        lines[0].Should().Contain("Severity").And.Contain("Field").And.Contain("DetectedBy");
    }

    [Fact]
    public async Task WriteCsvAsync_FilterPropagatesToRepository()
    {
        var profileId = Guid.NewGuid();
        var events = new[]
        {
            CreateChangeEvent(profileId, FieldName.Phone, ChangeSeverity.Minor),
            CreateChangeEvent(profileId, FieldName.LegalName, ChangeSeverity.Major),
            CreateChangeEvent(profileId, FieldName.EntityStatus, ChangeSeverity.Critical),
        };
        var repo = new FakeChangeEventRepository(events);
        var sut = new ChangeEventExporter(repo);
        using var output = new MemoryStream();

        await sut.WriteCsvAsync(
            output,
            new ChangeEventExportFilter { Severity = ChangeSeverity.Critical },
            CancellationToken.None);

        var csv = ReadCsv(output);
        csv.Should().Contain("Critical");
        csv.Should().NotContain("Minor");
        csv.Should().NotContain("Major");
    }

    [Fact]
    public async Task WriteCsvAsync_MaxRowsRespected()
    {
        var profileId = Guid.NewGuid();
        var events = Enumerable.Range(0, 10)
            .Select(_ => CreateChangeEvent(profileId))
            .ToList();
        var repo = new FakeChangeEventRepository(events);
        var sut = new ChangeEventExporter(repo);
        using var output = new MemoryStream();

        await sut.WriteCsvAsync(output, new ChangeEventExportFilter { MaxRows = 4 }, CancellationToken.None);

        repo.CapturedMaxRows.Should().Be(4);
        var csv = ReadCsv(output);
        var dataLines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).Count();
        dataLines.Should().Be(4);
    }

    [Fact]
    public async Task WriteCsvAsync_SanitisesFormulaInjection()
    {
        var evt = CreateChangeEvent(
            Guid.NewGuid(),
            prev: "=SUM(A1:A9)",
            next: "=HYPERLINK(\"http://evil\")");
        var sut = new ChangeEventExporter(new FakeChangeEventRepository([evt]));
        using var output = new MemoryStream();

        await sut.WriteCsvAsync(output, new ChangeEventExportFilter(), CancellationToken.None);

        var csv = ReadCsv(output);
        csv.Should().Contain("'=SUM");
        csv.Should().Contain("'=HYPERLINK");
    }

    [Fact]
    public async Task WriteXlsxAsync_ProducesValidWorkbook()
    {
        var profileId = Guid.NewGuid();
        var events = new[]
        {
            CreateChangeEvent(profileId, FieldName.Phone),
            CreateChangeEvent(profileId, FieldName.Email),
        };
        var sut = new ChangeEventExporter(new FakeChangeEventRepository(events));
        using var output = new MemoryStream();

        await sut.WriteXlsxAsync(output, new ChangeEventExportFilter(), CancellationToken.None);

        output.Position = 0;
        using var workbook = new XLWorkbook(output);
        var sheet = workbook.Worksheets.First();
        sheet.Name.Should().Be("Changes");
        sheet.Cell(1, 1).GetString().Should().Be(nameof(ChangeExportRow.Id));
    }

    private static string ReadCsv(MemoryStream stream)
    {
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
