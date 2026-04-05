using Microsoft.EntityFrameworkCore;
using Tracer.Domain.Entities;
using Tracer.Domain.Interfaces;

namespace Tracer.Infrastructure.Persistence;

/// <summary>
/// EF Core database context for the Tracer application.
/// Implements <see cref="IUnitOfWork"/> — domain event dispatch will be added in a later block.
/// </summary>
public sealed class TracerDbContext : DbContext, IUnitOfWork
{
    public TracerDbContext(DbContextOptions<TracerDbContext> options)
        : base(options)
    {
    }

    public DbSet<TraceRequest> TraceRequests => Set<TraceRequest>();
    public DbSet<CompanyProfile> CompanyProfiles => Set<CompanyProfile>();
    public DbSet<SourceResult> SourceResults => Set<SourceResult>();
    public DbSet<ChangeEvent> ChangeEvents => Set<ChangeEvent>();
    public DbSet<ValidationRecord> ValidationRecords => Set<ValidationRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TracerDbContext).Assembly);
    }
}
