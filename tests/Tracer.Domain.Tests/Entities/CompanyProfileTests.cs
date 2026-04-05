using FluentAssertions;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Events;
using Tracer.Domain.ValueObjects;

namespace Tracer.Domain.Tests.Entities;

public sealed class CompanyProfileTests
{
    private static CompanyProfile CreateSut(
        string normalizedKey = "CZ:12345678",
        string country = "CZ",
        string? registrationId = "12345678") =>
        new(normalizedKey, country, registrationId);

    private static TracedField<string> CreateStringField(
        string value,
        string source = "ares",
        double confidence = 0.9) =>
        new()
        {
            Value = value,
            Confidence = Confidence.Create(confidence),
            Source = source,
            EnrichedAt = DateTimeOffset.UtcNow,
        };

    private static TracedField<string> CreateExpiredStringField(
        string value,
        string source = "ares",
        int daysAgo = 400) =>
        new()
        {
            Value = value,
            Confidence = Confidence.Create(0.8),
            Source = source,
            EnrichedAt = DateTimeOffset.UtcNow.AddDays(-daysAgo),
        };

    // ── Constructor ─────────────────────────────────────────────────

    [Fact]
    public void Constructor_ValidInput_CreatesProfile()
    {
        var sut = CreateSut();

        sut.NormalizedKey.Should().Be("CZ:12345678");
        sut.Country.Should().Be("CZ");
        sut.RegistrationId.Should().Be("12345678");
        sut.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
        sut.TraceCount.Should().Be(0);
        sut.IsArchived.Should().BeFalse();
        sut.LegalName.Should().BeNull();
        sut.OverallConfidence.Should().BeNull();
        sut.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Constructor_RaisesProfileCreatedEvent()
    {
        var sut = CreateSut();

        sut.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<ProfileCreatedEvent>()
            .Which.Should().BeEquivalentTo(new
            {
                CompanyProfileId = sut.Id,
                NormalizedKey = "CZ:12345678",
            });
    }

    [Fact]
    public void Constructor_NullNormalizedKey_ThrowsArgumentException()
    {
        var act = () => new CompanyProfile(null!, "CZ");
        act.Should().Throw<ArgumentException>().WithParameterName("normalizedKey");
    }

    [Fact]
    public void Constructor_NullCountry_ThrowsArgumentException()
    {
        var act = () => new CompanyProfile("CZ:123", null!);
        act.Should().Throw<ArgumentException>().WithParameterName("country");
    }

    // ── UpdateField ─────────────────────────────────────────────────

    [Fact]
    public void UpdateField_NewField_CreatesChangeEventWithTypeCreated()
    {
        var sut = CreateSut();
        var field = CreateStringField("Acme s.r.o.");

        var change = sut.UpdateField(FieldName.LegalName, field, "ares");

        change.Should().NotBeNull();
        change!.ChangeType.Should().Be(ChangeType.Created);
        change.Severity.Should().Be(ChangeSeverity.Major); // LegalName = Major
        change.PreviousValueJson.Should().BeNull();
        change.NewValueJson.Should().Contain("Acme");
        sut.LegalName.Should().Be(field);
        sut.LastEnrichedAt.Should().NotBeNull();
    }

    [Fact]
    public void UpdateField_ValueChanged_CreatesChangeEventWithTypeUpdated()
    {
        var sut = CreateSut();
        sut.UpdateField(FieldName.Phone, CreateStringField("+420111"), "ares");

        var change = sut.UpdateField(FieldName.Phone, CreateStringField("+420222"), "gleif");

        change.Should().NotBeNull();
        change!.ChangeType.Should().Be(ChangeType.Updated);
        change.PreviousValueJson.Should().Contain("+420111");
        change.NewValueJson.Should().Contain("+420222");
    }

    [Fact]
    public void UpdateField_SameValue_ReturnsNull()
    {
        var sut = CreateSut();
        sut.UpdateField(FieldName.Phone, CreateStringField("+420111"), "ares");

        var change = sut.UpdateField(FieldName.Phone, CreateStringField("+420111"), "gleif");

        change.Should().BeNull();
    }

    [Fact]
    public void UpdateField_EntityStatus_RaisesCriticalChangeEvent()
    {
        var sut = CreateSut();
        sut.UpdateField(FieldName.EntityStatus, CreateStringField("active"), "ares");

        sut.UpdateField(FieldName.EntityStatus, CreateStringField("dissolved"), "ares");

        sut.DomainEvents.Should().Contain(e => e is CriticalChangeDetectedEvent);
        var critical = sut.DomainEvents.OfType<CriticalChangeDetectedEvent>().Last();
        critical.Field.Should().Be(FieldName.EntityStatus);
    }

    [Fact]
    public void UpdateField_Phone_RaisesFieldChangedEventWithMinorSeverity()
    {
        var sut = CreateSut();

        sut.UpdateField(FieldName.Phone, CreateStringField("+420111"), "ares");

        var evt = sut.DomainEvents.OfType<FieldChangedEvent>().Last();
        evt.Severity.Should().Be(ChangeSeverity.Minor);
    }

    [Fact]
    public void UpdateField_NullValue_ThrowsArgumentNullException()
    {
        var sut = CreateSut();

        var act = () => sut.UpdateField<string>(FieldName.LegalName, null!, "ares");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void UpdateField_EmptySource_ThrowsArgumentException()
    {
        var sut = CreateSut();

        var act = () => sut.UpdateField(FieldName.LegalName, CreateStringField("Test"), "");

        act.Should().Throw<ArgumentException>().WithParameterName("source");
    }

    [Fact]
    public void UpdateField_TypeMismatch_ThrowsInvalidOperationException()
    {
        var sut = CreateSut();
        var addressField = new TracedField<Address>
        {
            Value = Address.Empty,
            Confidence = Confidence.Create(0.5),
            Source = "test",
            EnrichedAt = DateTimeOffset.UtcNow,
        };

        // LegalName expects string, not Address
        var act = () => sut.UpdateField(FieldName.LegalName, addressField, "test");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*type mismatch*");
    }

    // ── NeedsRevalidation ───────────────────────────────────────────

    [Fact]
    public void NeedsRevalidation_NoFields_ReturnsFalse()
    {
        var sut = CreateSut();

        sut.NeedsRevalidation().Should().BeFalse();
    }

    [Fact]
    public void NeedsRevalidation_FreshFields_ReturnsFalse()
    {
        var sut = CreateSut();
        sut.UpdateField(FieldName.LegalName, CreateStringField("Acme"), "ares");
        sut.UpdateField(FieldName.Phone, CreateStringField("+420111"), "ares");

        sut.NeedsRevalidation().Should().BeFalse();
    }

    [Fact]
    public void NeedsRevalidation_ExpiredEntityStatus_ReturnsTrue()
    {
        var sut = CreateSut();
        // EntityStatus TTL = 30 days, set field to 31 days ago
        var expiredField = CreateExpiredStringField("active", daysAgo: 31);
        sut.UpdateField(FieldName.EntityStatus, expiredField, "ares");

        sut.NeedsRevalidation().Should().BeTrue();
    }

    [Fact]
    public void NeedsRevalidation_ExpiredPhone_ReturnsTrue()
    {
        var sut = CreateSut();
        // Phone TTL = 180 days, set field to 200 days ago
        var expiredField = CreateExpiredStringField("+420111", daysAgo: 200);
        sut.UpdateField(FieldName.Phone, expiredField, "ares");

        sut.NeedsRevalidation().Should().BeTrue();
    }

    // ── IncrementTraceCount ─────────────────────────────────────────

    [Fact]
    public void IncrementTraceCount_IncrementsByOne()
    {
        var sut = CreateSut();

        sut.IncrementTraceCount();
        sut.IncrementTraceCount();
        sut.IncrementTraceCount();

        sut.TraceCount.Should().Be(3);
    }

    // ── SetOverallConfidence ────────────────────────────────────────

    [Fact]
    public void SetOverallConfidence_SetsValue()
    {
        var sut = CreateSut();
        var confidence = Confidence.Create(0.85);

        sut.SetOverallConfidence(confidence);

        sut.OverallConfidence.Should().Be(confidence);
    }

    // ── MarkValidated ───────────────────────────────────────────────

    [Fact]
    public void MarkValidated_SetsLastValidatedAt()
    {
        var sut = CreateSut();

        sut.MarkValidated();

        sut.LastValidatedAt.Should().NotBeNull();
        sut.LastValidatedAt!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    // ── Archive / Unarchive ─────────────────────────────────────────

    [Fact]
    public void Archive_SetsIsArchivedTrue()
    {
        var sut = CreateSut();

        sut.Archive();

        sut.IsArchived.Should().BeTrue();
    }

    [Fact]
    public void Unarchive_SetsIsArchivedFalse()
    {
        var sut = CreateSut();
        sut.Archive();

        sut.Unarchive();

        sut.IsArchived.Should().BeFalse();
    }

    // ── Severity classification ─────────────────────────────────────

    [Theory]
    [InlineData(FieldName.EntityStatus, ChangeSeverity.Critical)]
    [InlineData(FieldName.LegalName, ChangeSeverity.Major)]
    [InlineData(FieldName.RegisteredAddress, ChangeSeverity.Major)]
    [InlineData(FieldName.Phone, ChangeSeverity.Minor)]
    [InlineData(FieldName.Email, ChangeSeverity.Minor)]
    [InlineData(FieldName.Website, ChangeSeverity.Minor)]
    public void UpdateField_ExistingField_HasCorrectSeverity(FieldName fieldName, ChangeSeverity expectedSeverity)
    {
        var sut = CreateSut();
        // First set the field
        sut.UpdateField(fieldName, CreateStringField("old-value"), "ares");
        // Then update it to trigger Updated change type
        var change = sut.UpdateField(fieldName, CreateStringField("new-value"), "gleif");

        change.Should().NotBeNull();
        change!.Severity.Should().Be(expectedSeverity);
    }
}
