using System.Threading.Channels;

namespace Tracer.Application.Services;

/// <summary>
/// Default in-memory implementation of <see cref="IRevalidationQueue"/>.
/// Backed by a bounded <see cref="Channel{T}"/> so misbehaving clients
/// cannot exhaust memory. A full queue yields HTTP 429 at the API layer.
/// </summary>
internal sealed class RevalidationQueue : IRevalidationQueue
{
    // 100 is enough headroom for manual bulk-revalidate actions from the
    // operator UI without turning the queue into unbounded state.
    internal const int Capacity = 100;

    private readonly Channel<Guid> _channel;

    public RevalidationQueue()
    {
        _channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public async ValueTask<bool> TryEnqueueAsync(Guid profileId, CancellationToken cancellationToken)
    {
        if (profileId == Guid.Empty)
            throw new ArgumentException("Profile ID must not be empty.", nameof(profileId));

        // WaitToWriteAsync returns false when the channel is completed,
        // which never happens in our case; TryWrite respects the bounded
        // capacity and DropWrite full mode without blocking.
        if (!await _channel.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false))
            return false;

        return _channel.Writer.TryWrite(profileId);
    }

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);

    public bool TryDequeue(out Guid profileId)
        => _channel.Reader.TryRead(out profileId);
}
