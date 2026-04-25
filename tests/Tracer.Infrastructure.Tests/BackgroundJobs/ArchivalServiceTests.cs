using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Tracer.Application.Services;
using Tracer.Domain.Interfaces;
using Tracer.Infrastructure.BackgroundJobs;

namespace Tracer.Infrastructure.Tests.BackgroundJobs;

/// <summary>
/// Unit tests for <see cref="ArchivalService"/>. Drive a single tick via the
/// internal <c>RunTickAsync</c> seam so tests never wait on real clock delays.
/// The repository is an NSubstitute fake — no SQL, no HTTP.
/// </summary>
public sealed class ArchivalServiceTests
{
    private readonly ICompanyProfileRepository _repository = Substitute.For<ICompanyProfileRepository>();
    private readonly ITracerMetrics _metrics = Substitute.For<ITracerMetrics>();

    private ArchivalService CreateService(
        ArchivalOptions options,
        DateTimeOffset? now = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_repository);
        var provider = services.BuildServiceProvider();

        return new ArchivalService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            _metrics,
            Options.Create(options),
            NullLogger<ArchivalService>.Instance)
        {
            Clock = () => now ?? DateTimeOffset.UtcNow,
        };
    }

    [Fact]
    public async Task RunTickAsync_NoCandidates_RecordsZeroAndDoesNotCallMetricsCounter()
    {
        _repository.ArchiveStaleAsync(
            Arg.Any<DateTimeOffset>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>())
            .Returns(0);

        var sut = CreateService(new ArchivalOptions { MinAgeDays = 365, MaxTraceCount = 1, BatchSize = 500 });

        await sut.RunTickAsync(CancellationToken.None);

        await _repository.Received(1).ArchiveStaleAsync(
            Arg.Any<DateTimeOffset>(), 1, 500, Arg.Any<CancellationToken>());
        _metrics.DidNotReceive().RecordCkbArchived(Arg.Any<int>());
    }

    [Fact]
    public async Task RunTickAsync_CutoffIsClockMinusMinAgeDays()
    {
        var now = new DateTimeOffset(2026, 4, 22, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset? seenCutoff = null;

        _repository.ArchiveStaleAsync(
            Arg.Do<DateTimeOffset>(c => seenCutoff = c),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>())
            .Returns(0);

        var sut = CreateService(
            new ArchivalOptions { MinAgeDays = 365, MaxTraceCount = 1, BatchSize = 500 },
            now);

        await sut.RunTickAsync(CancellationToken.None);

        seenCutoff.Should().Be(now - TimeSpan.FromDays(365));
    }

    [Fact]
    public async Task RunTickAsync_FullBatch_IteratesUntilShortBatch()
    {
        // First call returns BatchSize → keep iterating; second returns fewer → stop.
        var calls = 0;
        _repository.ArchiveStaleAsync(
            Arg.Any<DateTimeOffset>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls++;
                return calls == 1 ? 500 : 42;
            });

        var sut = CreateService(new ArchivalOptions { BatchSize = 500 });

        await sut.RunTickAsync(CancellationToken.None);

        calls.Should().Be(2);
        _metrics.Received(1).RecordCkbArchived(542);
    }

    [Fact]
    public async Task RunTickAsync_ShortBatchOnFirstCall_StopsImmediately()
    {
        _repository.ArchiveStaleAsync(
            Arg.Any<DateTimeOffset>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>())
            .Returns(7);

        var sut = CreateService(new ArchivalOptions { BatchSize = 500 });

        await sut.RunTickAsync(CancellationToken.None);

        await _repository.Received(1).ArchiveStaleAsync(
            Arg.Any<DateTimeOffset>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
        _metrics.Received(1).RecordCkbArchived(7);
    }

    [Fact]
    public async Task RunTickAsync_RepositoryThrows_IsPropagatedToOuterHandler()
    {
        _repository.ArchiveStaleAsync(
            Arg.Any<DateTimeOffset>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        var sut = CreateService(new ArchivalOptions());

        // RunTickAsync surfaces the exception; outer ExecuteAsync loop is
        // responsible for swallowing and logging. We test that contract via
        // the fact that RunTickAsync does NOT swallow directly (tighter unit).
        await FluentActions.Awaiting(() => sut.RunTickAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();

        _metrics.DidNotReceive().RecordCkbArchived(Arg.Any<int>());
    }

    [Fact]
    public async Task RunTickAsync_WhenHostAlreadyCanceled_DoesNothing()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var sut = CreateService(new ArchivalOptions());

        await sut.RunTickAsync(cts.Token);

        await _repository.DidNotReceiveWithAnyArgs().ArchiveStaleAsync(
            Arg.Any<DateTimeOffset>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
        _metrics.DidNotReceive().RecordCkbArchived(Arg.Any<int>());
    }

    [Fact]
    public async Task RunTickAsync_ClampsNonPositiveOptionValues()
    {
        // MinAgeDays = 0 / BatchSize = 0 are guarded at startup via ValidateOnStart,
        // but the service also clamps defensively: Math.Max(1, ...). Verify the
        // repository is never called with zero or negative parameters.
        _repository.ArchiveStaleAsync(
            Arg.Any<DateTimeOffset>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>())
            .Returns(0);

        var sut = CreateService(new ArchivalOptions
        {
            MinAgeDays = 0,
            MaxTraceCount = -5,
            BatchSize = 0,
        });

        await sut.RunTickAsync(CancellationToken.None);

        await _repository.Received(1).ArchiveStaleAsync(
            Arg.Any<DateTimeOffset>(),
            Arg.Is<int>(mtc => mtc >= 0),
            Arg.Is<int>(bs => bs >= 1),
            Arg.Any<CancellationToken>());
    }
}
