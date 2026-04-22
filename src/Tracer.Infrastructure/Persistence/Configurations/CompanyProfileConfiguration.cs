using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Tracer.Domain.Entities;
using Tracer.Domain.ValueObjects;

namespace Tracer.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="CompanyProfile"/>.
/// Enriched fields are stored as a JSON column for flexibility.
/// </summary>
internal sealed class CompanyProfileConfiguration : IEntityTypeConfiguration<CompanyProfile>
{
    public void Configure(EntityTypeBuilder<CompanyProfile> builder)
    {
        builder.ToTable("CompanyProfiles");

        builder.HasKey(e => e.Id);

        // Identity fields
        builder.Property(e => e.NormalizedKey).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Country).HasMaxLength(2).IsRequired();
        builder.Property(e => e.RegistrationId).HasMaxLength(50);

        // Confidence stored as double
        builder.Property(e => e.OverallConfidence)
            .HasConversion(new ValueConverter<Confidence?, double?>(
                v => v.HasValue ? v.Value.Value : null,
                v => v.HasValue ? Confidence.Create(v.Value) : null));

        // TracedField<string> properties stored as JSON columns
        builder.OwnsOne(e => e.LegalName, ConfigureStringField);
        builder.OwnsOne(e => e.TradeName, ConfigureStringField);
        builder.OwnsOne(e => e.TaxId, ConfigureStringField);
        builder.OwnsOne(e => e.LegalForm, ConfigureStringField);
        builder.OwnsOne(e => e.Phone, ConfigureStringField);
        builder.OwnsOne(e => e.Email, ConfigureStringField);
        builder.OwnsOne(e => e.Website, ConfigureStringField);
        builder.OwnsOne(e => e.Industry, ConfigureStringField);
        builder.OwnsOne(e => e.EmployeeRange, ConfigureStringField);
        builder.OwnsOne(e => e.EntityStatus, ConfigureStringField);
        builder.OwnsOne(e => e.ParentCompany, ConfigureStringField);

        // TracedField<Address> properties — stored as JSON
        builder.OwnsOne(e => e.RegisteredAddress, b =>
        {
            b.ToJson();
        });
        builder.OwnsOne(e => e.OperatingAddress, b =>
        {
            b.ToJson();
        });

        // TracedField<GeoCoordinate> — stored as JSON
        builder.OwnsOne(e => e.Location, b =>
        {
            b.ToJson();
        });

        // Indexes
        builder.HasIndex(e => e.NormalizedKey).IsUnique();
        builder.HasIndex(e => new { e.RegistrationId, e.Country });
        // SQL Server-only filtered index. Integration tests must use SQL Server Testcontainer, not SQLite.
        builder.HasIndex(e => e.LastValidatedAt)
            .HasFilter("[IsArchived] = 0");
        // Cache warming + fuzzy candidate selection scan ordered DESC. Filtered to non-archived
        // rows because every TraceCount-ordered query (warming, fuzzy match, list) excludes them.
        builder.HasIndex(e => e.TraceCount)
            .IsDescending()
            .HasFilter("[IsArchived] = 0");

        // Ignore domain events collection (not persisted)
        builder.Ignore(e => e.DomainEvents);
    }

    private static void ConfigureStringField<T>(OwnedNavigationBuilder<CompanyProfile, T> b)
        where T : class
    {
        b.ToJson();
    }
}
