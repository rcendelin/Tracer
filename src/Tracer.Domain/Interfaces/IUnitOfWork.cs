namespace Tracer.Domain.Interfaces;

/// <summary>
/// Coordinates the persistence of changes across multiple repositories in a single transaction.
/// Implemented by the Infrastructure layer (EF Core DbContext).
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Saves all pending changes to the database and dispatches accumulated domain events.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of state entries written to the database.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
