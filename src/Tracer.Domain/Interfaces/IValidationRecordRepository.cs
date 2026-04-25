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

    /// <summary>
    /// Counts validation records whose <see cref="ValidationRecord.ValidatedAt"/> is
    /// greater than or equal to <paramref name="since"/>.
    /// </summary>
    /// <param name="since">Inclusive lower bound. Typically start-of-day UTC.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> CountSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default);
}
