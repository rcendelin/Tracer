using FluentAssertions;
using Tracer.Domain.ValueObjects;

namespace Tracer.Domain.Tests.ValueObjects;

public sealed class TracedFieldTests
{
    [Fact]
    public void IsExpired_ReturnsFalse_WhenFieldEnrichedRecently()
    {
        var field = new TracedField<string>
        {
            Value = "Škoda Auto a.s.",
            Confidence = Confidence.Create(0.9),
            Source = "ares",
            EnrichedAt = DateTimeOffset.UtcNow,
        };

        field.IsExpired(TimeSpan.FromDays(1)).Should().BeFalse();
    }

    [Fact]
    public void IsExpired_ReturnsTrue_WhenTtlExceeded()
    {
        var field = new TracedField<string>
        {
            Value = "Škoda Auto a.s.",
            Confidence = Confidence.Create(0.9),
            Source = "ares",
            EnrichedAt = DateTimeOffset.UtcNow.AddDays(-2),
        };

        field.IsExpired(TimeSpan.FromDays(1)).Should().BeTrue();
    }

    [Fact]
    public void IsExpired_ReturnsFalse_WhenEnrichedAtIsJustBeforeTtlBoundary()
    {
        // Enriched (ttl - 1 second) ago — should not yet be expired.
        var ttl = TimeSpan.FromDays(30);
        var field = new TracedField<string>
        {
            Value = "test",
            Confidence = Confidence.Create(0.5),
            Source = "test",
            EnrichedAt = DateTimeOffset.UtcNow - ttl + TimeSpan.FromSeconds(1),
        };

        field.IsExpired(ttl).Should().BeFalse();
    }

    [Fact]
    public void ValueEquality_TwoIdenticalTracedFields_AreEqual()
    {
        var enrichedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var a = new TracedField<int>
        {
            Value = 42,
            Confidence = Confidence.Create(0.8),
            Source = "gleif",
            EnrichedAt = enrichedAt,
        };

        var b = new TracedField<int>
        {
            Value = 42,
            Confidence = Confidence.Create(0.8),
            Source = "gleif",
            EnrichedAt = enrichedAt,
        };

        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void ValueEquality_DifferentValues_AreNotEqual()
    {
        var enrichedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var a = new TracedField<string>
        {
            Value = "Alpha",
            Confidence = Confidence.Create(0.8),
            Source = "ares",
            EnrichedAt = enrichedAt,
        };

        var b = new TracedField<string>
        {
            Value = "Beta",
            Confidence = Confidence.Create(0.8),
            Source = "ares",
            EnrichedAt = enrichedAt,
        };

        a.Should().NotBe(b);
    }

    [Fact]
    public void TracedField_SupportsGenericComplexType()
    {
        var address = Address.Empty;
        var field = new TracedField<Address>
        {
            Value = address,
            Confidence = Confidence.Create(0.7),
            Source = "google-maps",
            EnrichedAt = DateTimeOffset.UtcNow,
        };

        field.Value.Should().Be(address);
    }
}
