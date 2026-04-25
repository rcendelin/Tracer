using FluentAssertions;
using Tracer.Application.Services.Export;

namespace Tracer.Application.Tests.Services.Export;

public sealed class ExportLimitsTests
{
    [Theory]
    [InlineData(null, 1_000)]
    [InlineData(0, 1_000)]
    [InlineData(-5, 1_000)]
    [InlineData(1, 1)]
    [InlineData(500, 500)]
    [InlineData(10_000, 10_000)]
    [InlineData(50_000, 10_000)]
    public void Clamp_ReturnsValueInRange(int? requested, int expected)
    {
        ExportLimits.Clamp(requested).Should().Be(expected);
    }

    [Fact]
    public void Constants_MatchSpec()
    {
        ExportLimits.MaxRows.Should().Be(10_000);
        ExportLimits.DefaultRows.Should().Be(1_000);
    }
}
