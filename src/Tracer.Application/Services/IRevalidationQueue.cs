namespace Tracer.Application.Services;

/// <summary>
/// Bounded in-memory queue of <see cref="Guid"/> profile IDs submitted
/// for immediate re-validation via <c>POST /api/profiles/{id}/revalidate</c>.
/// Consumed by <see cref="Tracer.Infrastructure.BackgroundJobs.RevalidationScheduler"/>
/// at the start of every tick, regardless of the off-peak gate.
/// </summary>
/// <remarks>
/// Backed by a bounded <see cref="System.Threading.Channels.Channel{T}"/>
/// with <c>capacity = 100</c> and <c>FullMode = DropWrite</c>. When the
/// queue is full, <see cref="TryEnqueueAsync"/> returns <c>false</c> and
/// the API endpoint responds with HTTP 429 so clients can retry.
/// Persistent / distributed queueing is a Phase 4 concern (Redis).
/// </remarks>
public interface IRevalidationQueue
{
    /// <summary>
    /// Attempts to enqueue <paramref name="profileId"/>. Returns <c>false</c>
    /// when the queue is full without blocking.
    /// </summary>
    ValueTask<bool> TryEnqueueAsync(Guid profileId, CancellationToken cancellationToken);

    /// <summary>
    /// Streams profile IDs in FIFO order until cancellation. Intended for
    /// a single scheduler consumer.
    /// </summary>
    IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Non-blocking read of a single item. Returns <c>false</c> if the
    /// queue is empty.
    /// </summary>
    bool TryDequeue(out Guid profileId);
}
