using FluentAssertions;
using Tracer.Domain.ValueObjects;

namespace Tracer.Domain.Tests.ValueObjects;

public sealed class GeoCoordinateTests
{
    [Fact]
    public void Create_ValidCoordinates_ReturnsGeoCoordinate()
    {
        var coord = GeoCoordinate.Create(50.0755, 14.4378); // Prague
        coord.Latitude.Should().Be(50.0755);
        coord.Longitude.Should().Be(14.4378);
    }

    [Theory]
    [InlineData(-90.0)]
    [InlineData(0.0)]
    [InlineData(90.0)]
    public void Create_BoundaryLatitudes_Work(double latitude)
    {
        var act = () => GeoCoordinate.Create(latitude, 0.0);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(-180.0)]
    [InlineData(0.0)]
    [InlineData(180.0)]
    public void Create_BoundaryLongitudes_Work(double longitude)
    {
        var act = () => GeoCoordinate.Create(0.0, longitude);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(-90.01)]
    [InlineData(90.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Create_LatitudeOutOfRange_ThrowsArgumentOutOfRangeException(double latitude)
    {
        var act = () => GeoCoordinate.Create(latitude, 0.0);
        // CA1507 suppressed: "latitude" is the parameter name of GeoCoordinate.Create,
        // not a local symbol — nameof cannot reference it from this scope.
#pragma warning disable CA1507
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("latitude");
#pragma warning restore CA1507
    }

    [Theory]
    [InlineData(-180.01)]
    [InlineData(180.01)]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    public void Create_LongitudeOutOfRange_ThrowsArgumentOutOfRangeException(double longitude)
    {
        var act = () => GeoCoordinate.Create(0.0, longitude);
        // CA1507 suppressed: "longitude" is the parameter name of GeoCoordinate.Create,
        // not a local symbol — nameof cannot reference it from this scope.
#pragma warning disable CA1507
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("longitude");
#pragma warning restore CA1507
    }

    [Fact]
    public void ValueEquality_TwoIdenticalCoordinates_AreEqual()
    {
        var a = GeoCoordinate.Create(48.1486, 17.1077); // Bratislava
        var b = GeoCoordinate.Create(48.1486, 17.1077);
        a.Should().Be(b);
    }
}
