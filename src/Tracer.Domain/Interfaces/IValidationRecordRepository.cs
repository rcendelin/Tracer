using Tracer.Domain.Entities;

namespace Tracer.Domain.Interfaces;

/// <summary>
/// Repository for <see cref="ValidationRecord"/> entities.
/// </summary>
public interface IValidationRecordRepository
{
    /// <summary>
    /// Persists a new validation record.
    /// </summary>
    Task AddAsync(ValidationRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists validation records for a specific company profile, ordered by validated date descending.
    /// </summary>
    /// <param name="companyProfileId">The company profile ID.</param>
    /// <param name="page">Zero-based page index.</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyCollection<ValidationRecord>> ListByProfileAsync(Guid companyProfileId, int page, int pageSize, CancellationToken cancellationToken = default);
}
