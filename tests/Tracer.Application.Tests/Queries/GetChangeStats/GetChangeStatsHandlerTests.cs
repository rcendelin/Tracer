using FluentAssertions;
using NSubstitute;
using Tracer.Application.Queries.GetChangeStats;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Tests.Queries.GetChangeStats;

public sealed class GetChangeStatsHandlerTests
{
    private readonly IChangeEventRepository _repository = Substitute.For<IChangeEventRepository>();

    private GetChangeStatsHandler CreateSut() => new(_repository);

    private void SetupCounts(int critical = 0, int major = 0, int minor = 0, int cosmetic = 0)
    {
        _repository.CountAsync(ChangeSeverity.Critical, null, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>()).Returns(critical);
        _repository.CountAsync(ChangeSeverity.Major,    null, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>()).Returns(major);
        _repository.CountAsync(ChangeSeverity.Minor,    null, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>()).Returns(minor);
        _repository.CountAsync(ChangeSeverity.Cosmetic, null, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>()).Returns(cosmetic);
    }

    // ── Basic counts ──────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_AllZero_ReturnsTotalZero()
    {
        SetupCounts();

        var result = await CreateSut().Handle(new GetChangeStatsQuery(), CancellationToken.None);

        result.TotalCount.Should().Be(0);
        result.CriticalCount.Should().Be(0);
        result.MajorCount.Should().Be(0);
        result.MinorCount.Should().Be(0);
        result.CosmeticCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_MixedCounts_SumsTotalCorrectly()
    {
        SetupCounts(critical: 2, major: 5, minor: 10, cosmetic: 3);

        var result = await CreateSut().Handle(new GetChangeStatsQuery(), CancellationToken.None);

        result.TotalCount.Should().Be(20);
        result.CriticalCount.Should().Be(2);
        result.MajorCount.Should().Be(5);
        result.MinorCount.Should().Be(10);
        result.CosmeticCount.Should().Be(3);
    }

    [Fact]
    public async Task Handle_OnlyCritical_TotalEqualsCount()
    {
        SetupCounts(critical: 7);

        var result = await CreateSut().Handle(new GetChangeStatsQuery(), CancellationToken.None);

        result.TotalCount.Should().Be(7);
        result.CriticalCount.Should().Be(7);
        result.MajorCount.Should().Be(0);
        result.MinorCount.Should().Be(0);
        result.CosmeticCount.Should().Be(0);
    }

    // ── Sequential execution ──────────────────────────────────────────────

    [Fact]
    public async Task Handle_CallsAllFourSeveritiesSequentially()
    {
        SetupCounts(critical: 1, major: 2, minor: 3, cosmetic: 4);

        await CreateSut().Handle(new GetChangeStatsQuery(), CancellationToken.None);

        // All four severity counts must be queried — no severity skipped
        await _repository.Received(1).CountAsync(ChangeSeverity.Critical, null, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
        await _repository.Received(1).CountAsync(ChangeSeverity.Major,    null, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
        await _repository.Received(1).CountAsync(ChangeSeverity.Minor,    null, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
        await _repository.Received(1).CountAsync(ChangeSeverity.Cosmetic, null, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DoesNotPassProfileId_AllNullProfileIds()
    {
        SetupCounts();

        await CreateSut().Handle(new GetChangeStatsQuery(), CancellationToken.None);

        // profileId must always be null for global stats
        await _repository.Received(4).CountAsync(
            Arg.Any<ChangeSeverity?>(), Arg.Is<Guid?>(g => g == null), Arg.Any<CancellationToken>());
    }

    // ── Cancellation ──────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CancelledToken_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(true);

        _repository.CountAsync(Arg.Any<ChangeSeverity?>(), Arg.Any<Guid?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromCanceled<int>(cts.Token));

        var act = () => CreateSut().Handle(new GetChangeStatsQuery(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
