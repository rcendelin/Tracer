using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Tracer.Domain.Entities;
using Tracer.Domain.ValueObjects;

namespace Tracer.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="TraceRequest"/>.
/// </summary>
internal sealed class TraceRequestConfiguration : IEntityTypeConfiguration<TraceRequest>
{
    public void Configure(EntityTypeBuilder<TraceRequest> builder)
    {
        builder.ToTable("TraceRequests");

        builder.HasKey(e => e.Id);

        // Input fields
        builder.Property(e => e.CompanyName).HasMaxLength(500);
        builder.Property(e => e.Phone).HasMaxLength(50);
        builder.Property(e => e.Email).HasMaxLength(320);
        builder.Property(e => e.Website).HasMaxLength(2000);
        builder.Property(e => e.Address).HasMaxLength(500);
        builder.Property(e => e.City).HasMaxLength(200);
        builder.Property(e => e.Country).HasMaxLength(2);
        builder.Property(e => e.RegistrationId).HasMaxLength(50);
        builder.Property(e => e.TaxId).HasMaxLength(50);
        builder.Property(e => e.IndustryHint).HasMaxLength(200);

        // Control fields
        builder.Property(e => e.Source).HasMaxLength(50).IsRequired();
        builder.Property(e => e.CallbackUrl).HasMaxLength(2000)
            .HasConversion(
                v => v != null ? v.AbsoluteUri : null,
                v => v != null ? new Uri(v) : null);

        // Result fields
        builder.Property(e => e.FailureReason).HasMaxLength(2000);

        // Confidence stored as double
        builder.Property(e => e.OverallConfidence)
            .HasConversion(new ValueConverter<Confidence?, double?>(
                v => v.HasValue ? v.Value.Value : null,
                v => v.HasValue ? Confidence.Create(v.Value) : null));

        // Indexes
        builder.HasIndex(e => e.CreatedAt).IsDescending();
        builder.HasIndex(e => e.Status);

        // Ignore domain events (not persisted)
        builder.Ignore(e => e.DomainEvents);
    }
}
