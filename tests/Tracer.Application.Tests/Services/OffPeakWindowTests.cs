using FluentAssertions;
using Tracer.Application.Services;

namespace Tracer.Application.Tests.Services;

public sealed class OffPeakWindowTests
{
    [Fact]
    public void IsWithin_WhenDisabled_AlwaysReturnsTrue()
    {
        var sut = new OffPeakWindow { Enabled = false, StartHourUtc = 22, EndHourUtc = 6 };
        sut.IsWithin(new DateTimeOffset(2026, 4, 21, 12, 0, 0, TimeSpan.Zero)).Should().BeTrue();
    }

    [Fact]
    public void IsWithin_SameDayWindow_InsideReturnsTrue()
    {
        var sut = new OffPeakWindow { Enabled = true, StartHourUtc = 2, EndHourUtc = 6 };
        sut.IsWithin(new DateTimeOffset(2026, 4, 21, 3, 0, 0, TimeSpan.Zero)).Should().BeTrue();
    }

    [Fact]
    public void IsWithin_SameDayWindow_StartBoundaryIsInclusive()
    {
        var sut = new OffPeakWindow { Enabled = true, StartHourUtc = 2, EndHourUtc = 6 };
        sut.IsWithin(new DateTimeOffset(2026, 4, 21, 2, 0, 0, TimeSpan.Zero)).Should().BeTrue();
    }

    [Fact]
    public void IsWithin_SameDayWindow_EndBoundaryIsExclusive()
    {
        var sut = new OffPeakWindow { Enabled = true, StartHourUtc = 2, EndHourUtc = 6 };
        sut.IsWithin(new DateTimeOffset(2026, 4, 21, 6, 0, 0, TimeSpan.Zero)).Should().BeFalse();
    }

    [Fact]
    public void IsWithin_WrapAround_22To6_At23ReturnsTrue()
    {
        var sut = new OffPeakWindow { Enabled = true, StartHourUtc = 22, EndHourUtc = 6 };
        sut.IsWithin(new DateTimeOffset(2026, 4, 21, 23, 0, 0, TimeSpan.Zero)).Should().BeTrue();
    }

    [Fact]
    public void IsWithin_WrapAround_22To6_At3ReturnsTrue()
    {
        var sut = new OffPeakWindow { Enabled = true, StartHourUtc = 22, EndHourUtc = 6 };
        sut.IsWithin(new DateTimeOffset(2026, 4, 21, 3, 0, 0, TimeSpan.Zero)).Should().BeTrue();
    }

    [Fact]
    public void IsWithin_WrapAround_22To6_At12ReturnsFalse()
    {
        var sut = new OffPeakWindow { Enabled = true, StartHourUtc = 22, EndHourUtc = 6 };
        sut.IsWithin(new DateTimeOffset(2026, 4, 21, 12, 0, 0, TimeSpan.Zero)).Should().BeFalse();
    }

    [Fact]
    public void IsWithin_EmptyWindow_AlwaysFalse()
    {
        var sut = new OffPeakWindow { Enabled = true, StartHourUtc = 5, EndHourUtc = 5 };
        sut.IsWithin(new DateTimeOffset(2026, 4, 21, 5, 0, 0, TimeSpan.Zero)).Should().BeFalse();
        sut.IsWithin(new DateTimeOffset(2026, 4, 21, 12, 0, 0, TimeSpan.Zero)).Should().BeFalse();
    }

    [Fact]
    public void IsWithin_NonUtcInput_ConvertedToUtcFirst()
    {
        var sut = new OffPeakWindow { Enabled = true, StartHourUtc = 22, EndHourUtc = 6 };
        // 05:30 +05:00 == 00:30 UTC → inside 22–6 window
        var input = new DateTimeOffset(2026, 4, 21, 5, 30, 0, TimeSpan.FromHours(5));
        sut.IsWithin(input).Should().BeTrue();
    }
}
