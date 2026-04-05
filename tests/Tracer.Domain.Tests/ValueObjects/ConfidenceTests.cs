using FluentAssertions;
using Tracer.Domain.ValueObjects;

namespace Tracer.Domain.Tests.ValueObjects;

public sealed class ConfidenceTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void Create_ValidValue_ReturnsConfidence(double value)
    {
        var confidence = Confidence.Create(value);
        confidence.Value.Should().Be(value);
    }

    [Fact]
    public void Create_NegativeValue_ThrowsArgumentOutOfRangeException()
    {
        var act = () => Confidence.Create(-0.01);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("value");
    }

    [Fact]
    public void Create_ValueAboveOne_ThrowsArgumentOutOfRangeException()
    {
        var act = () => Confidence.Create(1.01);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("value");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Create_NonFiniteValue_ThrowsArgumentOutOfRangeException(double value)
    {
        // NaN comparisons always return false in C# — IsFinite check prevents NaN from
        // silently bypassing the range guard.
        var act = () => Confidence.Create(value);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName(nameof(value));
    }

    [Fact]
    public void ImplicitConversion_ToDouble_Works()
    {
        var confidence = Confidence.Create(0.75);
        double value = confidence;
        value.Should().Be(0.75);
    }

    [Fact]
    public void ExplicitConversion_FromDouble_Works()
    {
        var confidence = (Confidence)0.6;
        confidence.Value.Should().Be(0.6);
    }

    [Fact]
    public void ExplicitConversion_InvalidDouble_Throws()
    {
        var act = () => _ = (Confidence)1.5;
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ToString_UsesInvariantCulture_WithTwoDecimalPlaces()
    {
        var confidence = Confidence.Create(0.75);
        confidence.ToString().Should().Be("0.75");
    }

    [Fact]
    public void Zero_HasValueZero()
    {
        Confidence.Zero.Value.Should().Be(0.0);
    }

    [Fact]
    public void Full_HasValueOne()
    {
        Confidence.Full.Value.Should().Be(1.0);
    }

    [Fact]
    public void ValueEquality_TwoIdenticalConfidences_AreEqual()
    {
        var a = Confidence.Create(0.8);
        var b = Confidence.Create(0.8);
        a.Should().Be(b);
    }
}
