using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Tracer.Application.Services;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.ValueObjects;

namespace Tracer.Application.Tests.Services;

public sealed class ChangeDetectorTests
{
    private readonly ChangeDetector _sut = new(Substitute.For<ILogger<ChangeDetector>>());

    private static CompanyProfile CreateProfile() => new("CZ:12345678", "CZ", "12345678");

    private static TracedField<object> StringField(string value, string source = "ares", double confidence = 0.9) =>
        new()
        {
            Value = value,
            Confidence = Confidence.Create(confidence),
            Source = source,
            EnrichedAt = DateTimeOffset.UtcNow,
        };

    private static TracedField<object> AddressField(Address address, string source = "ares") =>
        new()
        {
            Value = address,
            Confidence = Confidence.Create(0.9),
            Source = source,
            EnrichedAt = DateTimeOffset.UtcNow,
        };

    private static TracedField<object> GeoField(GeoCoordinate geo, string source = "google-maps") =>
        new()
        {
            Value = geo,
            Confidence = Confidence.Create(0.85),
            Source = source,
            EnrichedAt = DateTimeOffset.UtcNow,
        };

    // ── Empty input ─────────────────────────────────────────────────

    [Fact]
    public void DetectChanges_EmptyFields_ReturnsEmpty()
    {
        var profile = CreateProfile();
        var fields = new Dictionary<FieldName, TracedField<object>>();

        var result = _sut.DetectChanges(profile, fields);

        result.TotalChanges.Should().Be(0);
        result.HasCriticalChanges.Should().BeFalse();
        result.HasMajorChanges.Should().BeFalse();
    }

    // ── New field (Created) ─────────────────────────────────────────

    [Fact]
    public void DetectChanges_NewField_ReturnsCreatedChangeEvent()
    {
        var profile = CreateProfile();
        var fields = new Dictionary<FieldName, TracedField<object>>
        {
            [FieldName.LegalName] = StringField("Acme s.r.o."),
        };

        var result = _sut.DetectChanges(profile, fields);

        result.TotalChanges.Should().Be(1);
        result.Changes.Should().ContainSingle()
            .Which.ChangeType.Should().Be(ChangeType.Created);
        profile.LegalName.Should().NotBeNull();
        profile.LegalName!.Value.Should().Be("Acme s.r.o.");
    }

    // ── Same value (no change) ──────────────────────────────────────

    [Fact]
    public void DetectChanges_SameValue_ReturnsNoChanges()
    {
        var profile = CreateProfile();
        profile.UpdateField(FieldName.LegalName, new TracedField<string>
        {
            Value = "Acme s.r.o.",
            Confidence = Confidence.Create(0.8),
            Source = "ares",
            EnrichedAt = DateTimeOffset.UtcNow.AddDays(-1),
        }, "ares");

        var fields = new Dictionary<FieldName, TracedField<object>>
        {
            [FieldName.LegalName] = StringField("Acme s.r.o."),
        };

        var result = _sut.DetectChanges(profile, fields);

        result.TotalChanges.Should().Be(0);
    }

    // ── Updated value ───────────────────────────────────────────────

    [Fact]
    public void DetectChanges_ChangedValue_ReturnsUpdatedChangeEvent()
    {
        var profile = CreateProfile();
        profile.UpdateField(FieldName.LegalName, new TracedField<string>
        {
            Value = "Old Name",
            Confidence = Confidence.Create(0.8),
            Source = "gleif-lei",
            EnrichedAt = DateTimeOffset.UtcNow.AddDays(-30),
        }, "gleif-lei");

        var fields = new Dictionary<FieldName, TracedField<object>>
        {
            [FieldName.LegalName] = StringField("New Name s.r.o."),
        };

        var result = _sut.DetectChanges(profile, fields);

        result.TotalChanges.Should().Be(1);
        var change = result.Changes.Single();
        change.ChangeType.Should().Be(ChangeType.Updated);
        change.Severity.Should().Be(ChangeSeverity.Major);
        change.PreviousValueJson.Should().Contain("Old Name");
        change.NewValueJson.Should().Contain("New Name s.r.o.");
    }

    // ── Severity classification ─────────────────────────────────────

    [Fact]
    public void DetectChanges_EntityStatusChange_IsCritical()
    {
        var profile = CreateProfile();
        profile.UpdateField(FieldName.EntityStatus, new TracedField<string>
        {
            Value = "Active",
            Confidence = Confidence.Create(0.95),
            Source = "ares",
            EnrichedAt = DateTimeOffset.UtcNow.AddDays(-10),
        }, "ares");

        var fields = new Dictionary<FieldName, TracedField<object>>
        {
            [FieldName.EntityStatus] = StringField("In Liquidation"),
        };

        var result = _sut.DetectChanges(profile, fields);

        result.TotalChanges.Should().Be(1);
        result.HasCriticalChanges.Should().BeTrue();
        result.Changes.Single().Severity.Should().Be(ChangeSeverity.Critical);
    }

    [Fact]
    public void DetectChanges_LegalNameChange_IsMajor()
    {
        var profile = CreateProfile();
        profile.UpdateField(FieldName.LegalName, new TracedField<string>
        {
            Value = "Old",
            Confidence = Confidence.Create(0.8),
            Source = "ares",
            EnrichedAt = DateTimeOffset.UtcNow.AddDays(-30),
        }, "ares");

        var fields = new Dictionary<FieldName, TracedField<object>>
        {
            [FieldName.LegalName] = StringField("New"),
        };

        var result = _sut.DetectChanges(profile, fields);

        result.HasMajorChanges.Should().BeTrue();
        result.Changes.Single().Severity.Should().Be(ChangeSeverity.Major);
    }

    [Fact]
    public void DetectChanges_PhoneChange_IsMinor()
    {
        var profile = CreateProfile();
        profile.UpdateField(FieldName.Phone, new TracedField<string>
        {
            Value = "+420111222333",
            Confidence = Confidence.Create(0.7),
            Source = "google-maps",
            EnrichedAt = DateTimeOffset.UtcNow.AddDays(-60),
        }, "google-maps");

        var fields = new Dictionary<FieldName, TracedField<object>>
        {
            [FieldName.Phone] = StringField("+420999888777"),
        };

        var result = _sut.DetectChanges(profile, fields);

        result.Changes.Single().Severity.Should().Be(ChangeSeverity.Minor);
    }

    [Fact]
    public void DetectChanges_NewIndustryField_IsCosmetic()
    {
        var profile = CreateProfile();
        var fields = new Dictionary<FieldName, TracedField<object>>
        {
            [FieldName.Industry] = StringField("Manufacturing"),
        };

        var result = _sut.DetectChanges(profile, fields);

        result.Changes.Single().Severity.Should().Be(ChangeSeverity.Cosmetic);
    }

    // ── Multiple fields ─────────────────────────────────────────────

    [Fact]
    public void DetectChanges_MultipleNewFields_ReturnsAllChanges()
    {
        var profile = CreateProfile();
        var fields = new Dictionary<FieldName, TracedField<object>>
        {
            [FieldName.LegalName] = StringField("Acme s.r.o."),
            [FieldName.Phone] = StringField("+420111222333"),
            [FieldName.Email] = StringField("info@acme.cz"),
            [FieldName.Website] = StringField("https://acme.cz"),
        };

        var result = _sut.DetectChanges(profile, fields);

        result.TotalChanges.Should().Be(4);
    }

    [Fact]
    public void DetectChanges_MixedChangesAndNoChanges_ReturnsOnlyChanges()
    {
        var profile = CreateProfile();
        profile.UpdateField(FieldName.LegalName, new TracedField<string>
        {
            Value = "Acme s.r.o.",
            Confidence = Confidence.Create(0.9),
            Source = "ares",
            EnrichedAt = DateTimeOffset.UtcNow.AddDays(-1),
        }, "ares");

        var fields = new Dictionary<FieldName, TracedField<object>>
        {
            [FieldName.LegalName] = StringField("Acme s.r.o."),  // same → no change
            [FieldName.Phone] = StringField("+420111222333"),       // new → change
        };

        var result = _sut.DetectChanges(profile, fields);

        result.TotalChanges.Should().Be(1);
        result.Changes.Single().Field.Should().Be(FieldName.Phone);
    }

    // ── Address fields ──────────────────────────────────────────────

    [Fact]
    public void DetectChanges_AddressField_HandledCorrectly()
    {
        var profile = CreateProfile();
        var addr = new Address
        {
            Street = "Hlavní 1",
            City = "Praha",
            PostalCode = "11000",
            Country = "CZ",
        };
        var fields = new Dictionary<FieldName, TracedField<object>>
        {
            [FieldName.RegisteredAddress] = AddressField(addr),
        };

        var result = _sut.DetectChanges(profile, fields);

        result.TotalChanges.Should().Be(1);
        profile.RegisteredAddress.Should().NotBeNull();
        profile.RegisteredAddress!.Value.City.Should().Be("Praha");
    }

    // ── GeoCoordinate fields ────────────────────────────────────────

    [Fact]
    public void DetectChanges_LocationField_HandledCorrectly()
    {
        var profile = CreateProfile();
        var geo = GeoCoordinate.Create(50.0755, 14.4378);
        var fields = new Dictionary<FieldName, TracedField<object>>
        {
            [FieldName.Location] = GeoField(geo),
        };

        var result = _sut.DetectChanges(profile, fields);

        result.TotalChanges.Should().Be(1);
        profile.Location.Should().NotBeNull();
        profile.Location!.Value.Latitude.Should().Be(50.0755);
    }

    // ── GetBySeverity filter ────────────────────────────────────────

    [Fact]
    public void DetectChanges_GetBySeverity_FilterCorrectly()
    {
        var profile = CreateProfile();
        profile.UpdateField(FieldName.EntityStatus, new TracedField<string>
        {
            Value = "Active",
            Confidence = Confidence.Create(0.95),
            Source = "ares",
            EnrichedAt = DateTimeOffset.UtcNow.AddDays(-10),
        }, "ares");

        var fields = new Dictionary<FieldName, TracedField<object>>
        {
            [FieldName.EntityStatus] = StringField("Dissolved"),       // Critical
            [FieldName.Phone] = StringField("+420111222333"),           // Cosmetic (new)
        };

        var result = _sut.DetectChanges(profile, fields);

        result.TotalChanges.Should().Be(2);
        result.GetBySeverity(ChangeSeverity.Critical).Should().HaveCount(1);
        result.GetBySeverity(ChangeSeverity.Major).Should().HaveCount(1);  // Critical ≥ Major
        result.GetBySeverity(ChangeSeverity.Cosmetic).Should().HaveCount(2); // All ≥ Cosmetic
    }

    // ── Domain events ───────────────────────────────────────────────

    [Fact]
    public void DetectChanges_CriticalChange_RaisesDomainEvents()
    {
        var profile = CreateProfile();
        profile.UpdateField(FieldName.EntityStatus, new TracedField<string>
        {
            Value = "Active",
            Confidence = Confidence.Create(0.95),
            Source = "ares",
            EnrichedAt = DateTimeOffset.UtcNow.AddDays(-10),
        }, "ares");

        // Clear events from the setup call
        var initialEvents = profile.DomainEvents.ToList();

        var fields = new Dictionary<FieldName, TracedField<object>>
        {
            [FieldName.EntityStatus] = StringField("In Liquidation"),
        };

        var result = _sut.DetectChanges(profile, fields);

        result.HasCriticalChanges.Should().BeTrue();
        // Profile should have accumulated domain events (FieldChangedEvent + CriticalChangeDetectedEvent)
        profile.DomainEvents.Count.Should().BeGreaterThan(initialEvents.Count);
    }

    // ── Null guards ─────────────────────────────────────────────────

    [Fact]
    public void DetectChanges_NullProfile_Throws()
    {
        var act = () => _sut.DetectChanges(null!, new Dictionary<FieldName, TracedField<object>>());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DetectChanges_NullFields_Throws()
    {
        var profile = CreateProfile();

        var act = () => _sut.DetectChanges(profile, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ── ChangeDetectionResult.Empty ─────────────────────────────────

    [Fact]
    public void Empty_ReturnsZeroChanges()
    {
        var empty = ChangeDetectionResult.Empty;

        empty.TotalChanges.Should().Be(0);
        empty.HasCriticalChanges.Should().BeFalse();
        empty.HasMajorChanges.Should().BeFalse();
        empty.Changes.Should().BeEmpty();
    }
}
