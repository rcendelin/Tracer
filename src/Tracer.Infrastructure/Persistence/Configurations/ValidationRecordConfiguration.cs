using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tracer.Domain.Entities;

namespace Tracer.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="ValidationRecord"/>.
/// </summary>
internal sealed class ValidationRecordConfiguration : IEntityTypeConfiguration<ValidationRecord>
{
    public void Configure(EntityTypeBuilder<ValidationRecord> builder)
    {
        builder.ToTable("ValidationRecords");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ProviderId).HasMaxLength(50).IsRequired();

        // Relationships
        builder.HasOne<CompanyProfile>()
            .WithMany()
            .HasForeignKey(e => e.CompanyProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes
        builder.HasIndex(e => e.CompanyProfileId);
        builder.HasIndex(e => e.ValidatedAt).IsDescending();
    }
}
