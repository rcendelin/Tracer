using FluentAssertions;
using NSubstitute;
using Tracer.Application.Queries.GetChangeTrend;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Tests.Queries.GetChangeTrend;

public sealed class GetChangeTrendHandlerTests
{
    private readonly IChangeEventRepository _repository = Substitute.For<IChangeEventRepository>();

    private GetChangeTrendHandler CreateSut(DateTimeOffset now) =>
        new(_repository) { NowProvider = () => now };

    // ── Bucketing ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ZeroRows_ReturnsDenseSeriesOfEmptyBuckets()
    {
        var now = new DateTimeOffset(2026, 4, 15, 10, 0, 0, TimeSpan.Zero);
        _repository.GetMonthlyTrendAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ChangeTrendBucketRow>());

        var result = await CreateSut(now).Handle(new GetChangeTrendQuery(TrendPeriod.Monthly, 3), CancellationToken.None);

        result.Period.Should().Be(TrendPeriod.Monthly);
        result.Months.Should().Be(3);
        result.Buckets.Should().HaveCount(3);
        result.Buckets.Should().OnlyContain(b => b.Total == 0);

        // Ordered oldest → newest, ending at April 2026 (current month inclusive).
        result.Buckets[0].PeriodStart.Should().Be(new DateOnly(2026, 2, 1));
        result.Buckets[1].PeriodStart.Should().Be(new DateOnly(2026, 3, 1));
        result.Buckets[2].PeriodStart.Should().Be(new DateOnly(2026, 4, 1));
    }

    [Fact]
    public async Task Handle_PivotsRowsBySeverity()
    {
        var now = new DateTimeOffset(2026, 4, 15, 10, 0, 0, TimeSpan.Zero);
        _repository.GetMonthlyTrendAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new ChangeTrendBucketRow[]
            {
                new() { Year = 2026, Month = 3, Severity = ChangeSeverity.Critical, Count = 2 },
                new() { Year = 2026, Month = 3, Severity = ChangeSeverity.Major,    Count = 5 },
                new() { Year = 2026, Month = 3, Severity = ChangeSeverity.Minor,    Count = 1 },
                new() { Year = 2026, Month = 4, Severity = ChangeSeverity.Critical, Count = 1 },
                new() { Year = 2026, Month = 4, Severity = ChangeSeverity.Cosmetic, Count = 3 },
            });

        var result = await CreateSut(now).Handle(new GetChangeTrendQuery(TrendPeriod.Monthly, 3), CancellationToken.None);

        var feb = result.Buckets.Single(b => b.PeriodStart == new DateOnly(2026, 2, 1));
        var mar = result.Buckets.Single(b => b.PeriodStart == new DateOnly(2026, 3, 1));
        var apr = result.Buckets.Single(b => b.PeriodStart == new DateOnly(2026, 4, 1));

        feb.Total.Should().Be(0);
        mar.Critical.Should().Be(2);
        mar.Major.Should().Be(5);
        mar.Minor.Should().Be(1);
        mar.Cosmetic.Should().Be(0);
        mar.Total.Should().Be(8);
        apr.Critical.Should().Be(1);
        apr.Cosmetic.Should().Be(3);
        apr.Total.Should().Be(4);
    }

    [Fact]
    public async Task Handle_MultipleRowsSameBucketSeverity_AccumulatesCounts()
    {
        // Defensive: the repository normally returns one row per (year,month,severity),
        // but duplicates must be summed rather than overwritten.
        var now = new DateTimeOffset(2026, 4, 15, 10, 0, 0, TimeSpan.Zero);
        _repository.GetMonthlyTrendAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new ChangeTrendBucketRow[]
            {
                new() { Year = 2026, Month = 4, Severity = ChangeSeverity.Major, Count = 5 },
                new() { Year = 2026, Month = 4, Severity = ChangeSeverity.Major, Count = 3 },
            });

        var result = await CreateSut(now).Handle(new GetChangeTrendQuery(TrendPeriod.Monthly, 1), CancellationToken.None);

        result.Buckets.Should().ContainSingle().Which.Major.Should().Be(8);
    }

    // ── Date range calculation ────────────────────────────────────────────

    [Fact]
    public async Task Handle_RollingWindow_QueriesExclusiveUpperBound()
    {
        var now = new DateTimeOffset(2026, 4, 30, 23, 59, 59, TimeSpan.Zero);
        _repository.GetMonthlyTrendAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ChangeTrendBucketRow>());

        await CreateSut(now).Handle(new GetChangeTrendQuery(TrendPeriod.Monthly, 12), CancellationToken.None);

        // From = start of May 2025, To = start of May 2026 (exclusive).
        await _repository.Received(1).GetMonthlyTrendAsync(
            new DateTimeOffset(2025, 5, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            Arg.Any<CancellationToken>());
    }

    // ── Defensive clamping ────────────────────────────────────────────────

    [Fact]
    public async Task Handle_MonthsAboveUpperBound_ClampsTo36()
    {
        var now = new DateTimeOffset(2026, 4, 15, 10, 0, 0, TimeSpan.Zero);
        _repository.GetMonthlyTrendAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ChangeTrendBucketRow>());

        // Validator would reject 99; clamp is a belt-and-braces guard in the handler itself.
        var result = await CreateSut(now).Handle(new GetChangeTrendQuery(TrendPeriod.Monthly, 99), CancellationToken.None);

        result.Months.Should().Be(36);
        result.Buckets.Should().HaveCount(36);
    }

    // ── Cancellation ──────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CancelledToken_Propagates()
    {
        var now = new DateTimeOffset(2026, 4, 15, 10, 0, 0, TimeSpan.Zero);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _repository.GetMonthlyTrendAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromCanceled<IReadOnlyList<ChangeTrendBucketRow>>(cts.Token));

        var act = () => CreateSut(now).Handle(new GetChangeTrendQuery(TrendPeriod.Monthly, 3), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
