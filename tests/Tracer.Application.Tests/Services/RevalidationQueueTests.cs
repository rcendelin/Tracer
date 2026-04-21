using FluentAssertions;
using Tracer.Application.Services;

namespace Tracer.Application.Tests.Services;

public sealed class RevalidationQueueTests
{
    [Fact]
    public async Task TryEnqueueAsync_EmptyGuid_Throws()
    {
        var sut = new RevalidationQueue();

        var act = async () => await sut.TryEnqueueAsync(Guid.Empty, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task TryEnqueueAsync_Valid_ReturnsTrue()
    {
        var sut = new RevalidationQueue();

        var ok = await sut.TryEnqueueAsync(Guid.NewGuid(), CancellationToken.None);

        ok.Should().BeTrue();
    }

    [Fact]
    public async Task TryDequeue_AfterEnqueue_ReturnsSameItemFifo()
    {
        var sut = new RevalidationQueue();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await sut.TryEnqueueAsync(a, CancellationToken.None);
        await sut.TryEnqueueAsync(b, CancellationToken.None);

        sut.TryDequeue(out var first).Should().BeTrue();
        sut.TryDequeue(out var second).Should().BeTrue();
        sut.TryDequeue(out _).Should().BeFalse();
        first.Should().Be(a);
        second.Should().Be(b);
    }

    [Fact]
    public async Task TryEnqueueAsync_WhenFull_ReturnsFalse()
    {
        var sut = new RevalidationQueue();

        // Fill the channel to the exact configured capacity.
        for (var i = 0; i < RevalidationQueue.Capacity; i++)
        {
            (await sut.TryEnqueueAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeTrue();
        }

        var overflow = await sut.TryEnqueueAsync(Guid.NewGuid(), CancellationToken.None);

        overflow.Should().BeFalse();
    }
}
