using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tracer.Domain.Entities;

namespace Tracer.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="ChangeEvent"/>.
/// </summary>
internal sealed class ChangeEventConfiguration : IEntityTypeConfiguration<ChangeEvent>
{
    public void Configure(EntityTypeBuilder<ChangeEvent> builder)
    {
        builder.ToTable("ChangeEvents");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.DetectedBy).HasMaxLength(100).IsRequired();
        builder.Property(e => e.PreviousValueJson).HasMaxLength(4000);
        builder.Property(e => e.NewValueJson).HasMaxLength(4000);

        // Relationships
        builder.HasOne<CompanyProfile>()
            .WithMany()
            .HasForeignKey(e => e.CompanyProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes
        builder.HasIndex(e => e.CompanyProfileId);
        builder.HasIndex(e => e.DetectedAt).IsDescending();
        builder.HasIndex(e => e.Severity);
    }
}
