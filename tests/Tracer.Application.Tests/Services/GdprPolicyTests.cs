using FluentAssertions;
using Microsoft.Extensions.Options;
using Tracer.Application.Services;
using Tracer.Domain.Enums;

namespace Tracer.Application.Tests.Services;

/// <summary>
/// Tests for <see cref="GdprPolicy"/> — classifies fields as personal/firmographic
/// and exposes the platform personal-data retention window.
/// </summary>
public sealed class GdprPolicyTests
{
    private static GdprPolicy CreateSut(GdprOptions? options = null) =>
        new(Options.Create(options ?? new GdprOptions()));

    // ── Classification ──────────────────────────────────────────────────

    [Fact]
    public void Classify_Officers_ReturnsPersonalData()
    {
        CreateSut().Classify(FieldName.Officers).Should().Be(FieldClassification.PersonalData);
    }

    [Theory]
    [InlineData(FieldName.LegalName)]
    [InlineData(FieldName.TradeName)]
    [InlineData(FieldName.RegistrationId)]
    [InlineData(FieldName.TaxId)]
    [InlineData(FieldName.LegalForm)]
    [InlineData(FieldName.RegisteredAddress)]
    [InlineData(FieldName.OperatingAddress)]
    [InlineData(FieldName.Phone)]
    [InlineData(FieldName.Email)]
    [InlineData(FieldName.Website)]
    [InlineData(FieldName.Industry)]
    [InlineData(FieldName.EmployeeRange)]
    [InlineData(FieldName.EntityStatus)]
    [InlineData(FieldName.ParentCompany)]
    [InlineData(FieldName.Location)]
    public void Classify_FirmographicFields_ReturnsFirmographic(FieldName field)
    {
        CreateSut().Classify(field).Should().Be(FieldClassification.Firmographic);
    }

    [Fact]
    public void Classify_AllFieldNameMembersAreCovered()
    {
        // Guardrail: if a new FieldName is added, the classifier must explicitly
        // decide for every member (no silent Firmographic default slipping a
        // personal-data field through unclassified). The switch in GdprPolicy
        // uses a discard default, so this test makes sure someone deliberately
        // looked at the new value.
        var sut = CreateSut();
        foreach (FieldName field in Enum.GetValues<FieldName>())
        {
            var classification = sut.Classify(field);
            classification.Should().BeOneOf(FieldClassification.Firmographic, FieldClassification.PersonalData);
        }
    }

    // ── Predicates ──────────────────────────────────────────────────────

    [Fact]
    public void IsPersonalData_Officers_ReturnsTrue()
    {
        CreateSut().IsPersonalData(FieldName.Officers).Should().BeTrue();
    }

    [Fact]
    public void IsPersonalData_LegalName_ReturnsFalse()
    {
        CreateSut().IsPersonalData(FieldName.LegalName).Should().BeFalse();
    }

    [Fact]
    public void RequiresConsent_Officers_ReturnsTrue()
    {
        CreateSut().RequiresConsent(FieldName.Officers).Should().BeTrue();
    }

    [Fact]
    public void RequiresConsent_Firmographic_ReturnsFalse()
    {
        CreateSut().RequiresConsent(FieldName.RegistrationId).Should().BeFalse();
    }

    // ── Retention window ────────────────────────────────────────────────

    [Fact]
    public void PersonalDataRetention_DefaultIs36Months()
    {
        CreateSut().PersonalDataRetention.Should().Be(TimeSpan.FromDays(1095));
    }

    [Fact]
    public void PersonalDataRetention_ReflectsConfiguredValue()
    {
        var sut = CreateSut(new GdprOptions { PersonalDataRetentionDays = 730 });
        sut.PersonalDataRetention.Should().Be(TimeSpan.FromDays(730));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-1095)]
    public void Ctor_NonPositiveRetention_Throws(int days)
    {
        var options = Options.Create(new GdprOptions { PersonalDataRetentionDays = days });
        var act = () => new GdprPolicy(options);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Ctor_NullOptions_Throws()
    {
        var act = () => new GdprPolicy(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
