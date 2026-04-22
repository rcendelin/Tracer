using FluentAssertions;
using Tracer.Application.Services.Export;

namespace Tracer.Application.Tests.Services.Export;

public sealed class CsvInjectionSanitizerTests
{
    [Theory]
    [InlineData("=SUM(A1)", "'=SUM(A1)")]
    [InlineData("+cmd|'/c calc'!A1", "'+cmd|'/c calc'!A1")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("@import", "'@import")]
    [InlineData("\tHIDDEN", "'\tHIDDEN")]
    [InlineData("\rreturn", "'\rreturn")]
    public void Sanitize_DangerousLeadingChar_PrefixedWithApostrophe(string input, string expected)
    {
        CsvInjectionSanitizer.Sanitize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("BHP Group Limited")]
    [InlineData("Škoda Auto a.s.")]
    [InlineData("123 Main Street")]
    [InlineData("john@example.com")] // @ in middle, not leading
    [InlineData("example=value")]   // = in middle, not leading
    public void Sanitize_SafeValue_ReturnedUnchanged(string input)
    {
        CsvInjectionSanitizer.Sanitize(input).Should().Be(input);
    }

    [Fact]
    public void Sanitize_Null_ReturnsNull()
    {
        CsvInjectionSanitizer.Sanitize(null).Should().BeNull();
    }

    [Fact]
    public void Sanitize_EmptyString_ReturnedUnchanged()
    {
        CsvInjectionSanitizer.Sanitize(string.Empty).Should().Be(string.Empty);
    }
}
