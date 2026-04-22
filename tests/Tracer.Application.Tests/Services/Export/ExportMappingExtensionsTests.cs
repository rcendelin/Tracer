using FluentAssertions;
using Tracer.Application.Services.Export;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.ValueObjects;

namespace Tracer.Application.Tests.Services.Export;

public sealed class ExportMappingExtensionsTests
{
    [Fact]
    public void ToExportRow_Profile_FlattensPrimitiveFields()
    {
        var profile = new CompanyProfile("CZ:12345678", "CZ", "12345678");
        profile.UpdateField(
            FieldName.LegalName,
            new TracedField<string>
            {
                Value = "Alpha a.s.",
                Confidence = Confidence.Create(0.95),
                Source = "ares",
                EnrichedAt = DateTimeOffset.UtcNow,
            },
            "ares");

        var row = profile.ToExportRow();

        row.Id.Should().Be(profile.Id);
        row.LegalName.Should().Be("Alpha a.s.");
        row.Country.Should().Be("CZ");
        row.RegistrationId.Should().Be("12345678");
    }

    [Fact]
    public void ToExportRow_AddressWithFormattedAddress_UsesFormatted()
    {
        var profile = new CompanyProfile("CZ:12345678", "CZ", "12345678");
        profile.UpdateField(
            FieldName.RegisteredAddress,
            new TracedField<Address>
            {
                Value = new Address
                {
                    Street = "Wenceslas Square 1",
                    City = "Prague",
                    PostalCode = "11000",
                    Country = "CZ",
                    FormattedAddress = "Wenceslas Square 1, 110 00 Prague, Czechia",
                },
                Confidence = Confidence.Create(0.9),
                Source = "google-maps",
                EnrichedAt = DateTimeOffset.UtcNow,
            },
            "google-maps");

        var row = profile.ToExportRow();

        row.RegisteredAddress.Should().Be("Wenceslas Square 1, 110 00 Prague, Czechia");
    }

    [Fact]
    public void ToExportRow_AddressWithoutFormatted_ConstructsFromStructured()
    {
        var profile = new CompanyProfile("CZ:12345678", "CZ", "12345678");
        profile.UpdateField(
            FieldName.RegisteredAddress,
            new TracedField<Address>
            {
                Value = new Address
                {
                    Street = "Hauptstraße 10",
                    City = "Berlin",
                    PostalCode = "10115",
                    Country = "DE",
                },
                Confidence = Confidence.Create(0.9),
                Source = "handelsregister",
                EnrichedAt = DateTimeOffset.UtcNow,
            },
            "handelsregister");

        var row = profile.ToExportRow();

        row.RegisteredAddress.Should().Be("Hauptstraße 10, Berlin 10115, DE");
    }

    [Fact]
    public void ToExportRow_ProfileWithInjectionInLegalName_Sanitised()
    {
        var profile = new CompanyProfile("CZ:12345678", "CZ", "12345678");
        profile.UpdateField(
            FieldName.LegalName,
            new TracedField<string>
            {
                Value = "=cmd|' /c calc'!A1",
                Confidence = Confidence.Create(0.9),
                Source = "ares",
                EnrichedAt = DateTimeOffset.UtcNow,
            },
            "ares");

        var row = profile.ToExportRow();

        row.LegalName.Should().StartWith("'=");
    }

    [Fact]
    public void ToExportRow_ChangeEvent_MapsAllFields()
    {
        var profileId = Guid.NewGuid();
        var evt = new ChangeEvent(
            profileId,
            FieldName.EntityStatus,
            ChangeType.Updated,
            ChangeSeverity.Critical,
            "\"active\"",
            "\"dissolved\"",
            "revalidation-scheduler");

        var row = evt.ToExportRow();

        row.Id.Should().Be(evt.Id);
        row.CompanyProfileId.Should().Be(profileId);
        row.Field.Should().Be(FieldName.EntityStatus);
        row.Severity.Should().Be(ChangeSeverity.Critical);
        row.ChangeType.Should().Be(ChangeType.Updated);
        row.DetectedBy.Should().Be("revalidation-scheduler");
        row.PreviousValueJson.Should().Be("\"active\"");
        row.NewValueJson.Should().Be("\"dissolved\"");
        row.IsNotified.Should().BeFalse();
    }
}
