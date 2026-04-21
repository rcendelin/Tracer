using FluentAssertions;
using Microsoft.Extensions.Options;
using Tracer.Application.Services;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.ValueObjects;

namespace Tracer.Application.Tests.Services;

/// <summary>
/// Tests for <see cref="FieldTtlPolicy"/> — merges per-field overrides
/// from configuration with platform defaults and answers re-validation
/// questions about a <see cref="CompanyProfile"/>.
/// </summary>
public sealed class FieldTtlPolicyTests
{
    private static FieldTtlPolicy CreateSut(IDictionary<string, TimeSpan>? overrides = null)
    {
        var opts = new FieldTtlOptions { Overrides = overrides ?? new Dictionary<string, TimeSpan>() };
        return new FieldTtlPolicy(Options.Create(opts));
    }

    private static CompanyProfile CreateProfile() =>
        new("CZ:12345678", "CZ", "12345678");

    private static TracedField<string> StringField(string value, DateTimeOffset enrichedAt) => new()
    {
        Value = value,
        Confidence = Confidence.Create(0.9),
        Source = "ares",
        EnrichedAt = enrichedAt,
    };

    // ── GetTtl ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(FieldName.EntityStatus, 30)]
    [InlineData(FieldName.Officers, 90)]
    [InlineData(FieldName.Phone, 180)]
    [InlineData(FieldName.Email, 180)]
    [InlineData(FieldName.Website, 180)]
    [InlineData(FieldName.RegisteredAddress, 365)]
    [InlineData(FieldName.OperatingAddress, 365)]
    [InlineData(FieldName.RegistrationId, 730)]
    [InlineData(FieldName.TaxId, 730)]
    [InlineData(FieldName.LegalName, 180)] // default bucket
    public void GetTtl_NoOverride_ReturnsPlatformDefault(FieldName field, int expectedDays)
    {
        CreateSut().GetTtl(field).Should().Be(TimeSpan.FromDays(expectedDays));
    }

    [Fact]
    public void GetTtl_WithOverride_ReturnsOverride()
    {
        var sut = CreateSut(new Dictionary<string, TimeSpan>
        {
            ["EntityStatus"] = TimeSpan.FromDays(7),
        });

        sut.GetTtl(FieldName.EntityStatus).Should().Be(TimeSpan.FromDays(7));
    }

    [Fact]
    public void GetTtl_OverrideKeyIsCaseInsensitive()
    {
        var sut = CreateSut(new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase)
        {
            ["ENTITYSTATUS"] = TimeSpan.FromDays(5),
        });

        sut.GetTtl(FieldName.EntityStatus).Should().Be(TimeSpan.FromDays(5));
    }

    [Fact]
    public void GetTtl_OnlyOneOverride_OtherFieldsKeepDefaults()
    {
        var sut = CreateSut(new Dictionary<string, TimeSpan>
        {
            ["Phone"] = TimeSpan.FromDays(10),
        });

        sut.GetTtl(FieldName.Phone).Should().Be(TimeSpan.FromDays(10));
        sut.GetTtl(FieldName.Email).Should().Be(TimeSpan.FromDays(180));
        sut.GetTtl(FieldName.EntityStatus).Should().Be(TimeSpan.FromDays(30));
    }

    // ── Constructor guards ──────────────────────────────────────────────

    [Fact]
    public void Ctor_NullOptions_Throws()
    {
        var act = () => new FieldTtlPolicy(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Ctor_UnknownFieldName_Throws()
    {
        var opts = Options.Create(new FieldTtlOptions
        {
            Overrides = new Dictionary<string, TimeSpan> { ["NotAField"] = TimeSpan.FromDays(1) },
        });

        var act = () => new FieldTtlPolicy(opts);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*NotAField*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Ctor_NonPositiveTtl_Throws(int seconds)
    {
        var opts = Options.Create(new FieldTtlOptions
        {
            Overrides = new Dictionary<string, TimeSpan> { ["Phone"] = TimeSpan.FromSeconds(seconds) },
        });

        var act = () => new FieldTtlPolicy(opts);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Ctor_NullOverridesDictionary_UsesDefaults()
    {
        var opts = Options.Create(new FieldTtlOptions { Overrides = null! });

        var sut = new FieldTtlPolicy(opts);

        sut.GetTtl(FieldName.EntityStatus).Should().Be(TimeSpan.FromDays(30));
    }

    // ── GetExpiredFields ────────────────────────────────────────────────

    [Fact]
    public void GetExpiredFields_EmptyProfile_ReturnsEmpty()
    {
        var sut = CreateSut();
        var now = DateTimeOffset.UtcNow;

        sut.GetExpiredFields(CreateProfile(), now).Should().BeEmpty();
    }

    [Fact]
    public void GetExpiredFields_AllFresh_ReturnsEmpty()
    {
        var sut = CreateSut();
        var now = DateTimeOffset.UtcNow;
        var profile = CreateProfile();
        profile.UpdateField(FieldName.LegalName, StringField("Acme", now.AddDays(-10)), "ares");
        profile.UpdateField(FieldName.EntityStatus, StringField("active", now.AddDays(-5)), "ares");

        sut.GetExpiredFields(profile, now).Should().BeEmpty();
    }

    [Fact]
    public void GetExpiredFields_EntityStatusOlderThan30Days_Returned()
    {
        var sut = CreateSut();
        var now = DateTimeOffset.UtcNow;
        var profile = CreateProfile();
        profile.UpdateField(FieldName.EntityStatus, StringField("active", now.AddDays(-31)), "ares");

        sut.GetExpiredFields(profile, now).Should().ContainSingle().Which.Should().Be(FieldName.EntityStatus);
    }

    [Fact]
    public void GetExpiredFields_MixFreshAndExpired_ReturnsOnlyExpired()
    {
        var sut = CreateSut();
        var now = DateTimeOffset.UtcNow;
        var profile = CreateProfile();
        profile.UpdateField(FieldName.LegalName, StringField("Acme", now.AddDays(-10)), "ares");           // fresh (TTL 180)
        profile.UpdateField(FieldName.EntityStatus, StringField("active", now.AddDays(-31)), "ares");     // expired (TTL 30)
        profile.UpdateField(FieldName.Phone, StringField("+420111", now.AddDays(-200)), "ares");          // expired (TTL 180)

        sut.GetExpiredFields(profile, now).Should().BeEquivalentTo(new[]
        {
            FieldName.Phone, FieldName.EntityStatus,
        });
    }

    [Fact]
    public void GetExpiredFields_OverrideShortenedTtl_FieldBecomesExpired()
    {
        var sut = CreateSut(new Dictionary<string, TimeSpan>
        {
            ["LegalName"] = TimeSpan.FromDays(1),
        });
        var now = DateTimeOffset.UtcNow;
        var profile = CreateProfile();
        profile.UpdateField(FieldName.LegalName, StringField("Acme", now.AddDays(-2)), "ares");

        sut.GetExpiredFields(profile, now).Should().ContainSingle().Which.Should().Be(FieldName.LegalName);
    }

    [Fact]
    public void GetExpiredFields_NullProfile_Throws()
    {
        var sut = CreateSut();
        var act = () => sut.GetExpiredFields(null!, DateTimeOffset.UtcNow);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── GetNextExpirationDate ───────────────────────────────────────────

    [Fact]
    public void GetNextExpirationDate_EmptyProfile_ReturnsNull()
    {
        var sut = CreateSut();

        sut.GetNextExpirationDate(CreateProfile(), DateTimeOffset.UtcNow).Should().BeNull();
    }

    [Fact]
    public void GetNextExpirationDate_ReturnsEarliestExpiration()
    {
        var sut = CreateSut();
        var now = DateTimeOffset.UtcNow;
        var profile = CreateProfile();
        // EntityStatus (TTL 30d) enriched now → expiration now + 30d
        // Phone (TTL 180d) enriched now → expiration now + 180d
        // Expect earliest: EntityStatus → now + 30d
        profile.UpdateField(FieldName.EntityStatus, StringField("active", now), "ares");
        profile.UpdateField(FieldName.Phone, StringField("+420111", now), "ares");

        var next = sut.GetNextExpirationDate(profile, now);

        next.Should().NotBeNull();
        next.Should().BeCloseTo(now + TimeSpan.FromDays(30), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void GetNextExpirationDate_MixExpiredAndFresh_ReturnsPastExpiration()
    {
        var sut = CreateSut();
        var now = DateTimeOffset.UtcNow;
        var profile = CreateProfile();
        // EntityStatus enriched 40 days ago → already expired (expiration 10 days in the past)
        // Phone enriched now → fresh (expiration 180 days in the future)
        profile.UpdateField(FieldName.EntityStatus, StringField("active", now.AddDays(-40)), "ares");
        profile.UpdateField(FieldName.Phone, StringField("+420111", now), "ares");

        var next = sut.GetNextExpirationDate(profile, now);

        next.Should().NotBeNull();
        next!.Value.Should().BeBefore(now);
    }

    [Fact]
    public void GetNextExpirationDate_NullProfile_Throws()
    {
        var sut = CreateSut();
        var act = () => sut.GetNextExpirationDate(null!, DateTimeOffset.UtcNow);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── IsRevalidationDue ───────────────────────────────────────────────

    [Fact]
    public void IsRevalidationDue_EmptyProfile_False()
    {
        CreateSut().IsRevalidationDue(CreateProfile(), DateTimeOffset.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void IsRevalidationDue_AllFresh_False()
    {
        var sut = CreateSut();
        var now = DateTimeOffset.UtcNow;
        var profile = CreateProfile();
        profile.UpdateField(FieldName.EntityStatus, StringField("active", now.AddDays(-5)), "ares");

        sut.IsRevalidationDue(profile, now).Should().BeFalse();
    }

    [Fact]
    public void IsRevalidationDue_OneFieldExpired_True()
    {
        var sut = CreateSut();
        var now = DateTimeOffset.UtcNow;
        var profile = CreateProfile();
        profile.UpdateField(FieldName.EntityStatus, StringField("active", now.AddDays(-31)), "ares");

        sut.IsRevalidationDue(profile, now).Should().BeTrue();
    }

    [Fact]
    public void IsRevalidationDue_NullProfile_Throws()
    {
        var sut = CreateSut();
        var act = () => sut.IsRevalidationDue(null!, DateTimeOffset.UtcNow);
        act.Should().Throw<ArgumentNullException>();
    }
}
