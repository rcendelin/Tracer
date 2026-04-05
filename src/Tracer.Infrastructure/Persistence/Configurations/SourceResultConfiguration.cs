using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tracer.Domain.Entities;

namespace Tracer.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="SourceResult"/>.
/// </summary>
internal sealed class SourceResultConfiguration : IEntityTypeConfiguration<SourceResult>
{
    public void Configure(EntityTypeBuilder<SourceResult> builder)
    {
        builder.ToTable("SourceResults");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ProviderId).HasMaxLength(50).IsRequired();
        builder.Property(e => e.ErrorMessage).HasMaxLength(2000);
        builder.Property(e => e.RawResponseJson).HasMaxLength(50_000);

        // Relationships
        builder.HasOne<TraceRequest>()
            .WithMany()
            .HasForeignKey(e => e.TraceRequestId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes
        builder.HasIndex(e => e.TraceRequestId);
        builder.HasIndex(e => e.CreatedAt).IsDescending();
    }
}
