using FluentAssertions;
using NSubstitute;
using Tracer.Application.Queries.GetValidationStats;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Tests.Queries.GetValidationStats;

public sealed class GetValidationStatsHandlerTests
{
    private readonly ICompanyProfileRepository _profiles = Substitute.For<ICompanyProfileRepository>();
    private readonly IValidationRecordRepository _validations = Substitute.For<IValidationRecordRepository>();
    private readonly IChangeEventRepository _changes = Substitute.For<IChangeEventRepository>();

    private GetValidationStatsHandler CreateSut() => new(_profiles, _validations, _changes);

    [Fact]
    public async Task Handle_AllZeroes_ReturnsZeroDto()
    {
        _profiles.CountRevalidationCandidatesAsync(Arg.Any<CancellationToken>()).Returns(0);
        _validations.CountSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(0);
        _changes.CountSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(0);
        _profiles.AverageDaysSinceLastValidationAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(0d);

        var result = await CreateSut().Handle(new GetValidationStatsQuery(), CancellationToken.None);

        result.PendingCount.Should().Be(0);
        result.ProcessedToday.Should().Be(0);
        result.ChangesDetectedToday.Should().Be(0);
        result.AverageDataAgeDays.Should().Be(0);
    }

    [Fact]
    public async Task Handle_AggregatesFromRepositories()
    {
        _profiles.CountRevalidationCandidatesAsync(Arg.Any<CancellationToken>()).Returns(42);
        _validations.CountSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(12);
        _changes.CountSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(3);
        _profiles.AverageDaysSinceLastValidationAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(18.3456);

        var result = await CreateSut().Handle(new GetValidationStatsQuery(), CancellationToken.None);

        result.PendingCount.Should().Be(42);
        result.ProcessedToday.Should().Be(12);
        result.ChangesDetectedToday.Should().Be(3);
        // 2-decimal rounding away-from-zero
        result.AverageDataAgeDays.Should().Be(18.35);
    }

    [Fact]
    public async Task Handle_PassesStartOfDayUtcToCountSinceAsync()
    {
        _profiles.CountRevalidationCandidatesAsync(Arg.Any<CancellationToken>()).Returns(0);
        _validations.CountSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(0);
        _changes.CountSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(0);
        _profiles.AverageDaysSinceLastValidationAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(0d);

        var expectedStart = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);

        await CreateSut().Handle(new GetValidationStatsQuery(), CancellationToken.None);

        await _validations.Received(1).CountSinceAsync(
            Arg.Is<DateTimeOffset>(d => d == expectedStart),
            Arg.Any<CancellationToken>());
        await _changes.Received(1).CountSinceAsync(
            Arg.Is<DateTimeOffset>(d => d == expectedStart),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CancelledToken_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(true);

        _profiles.CountRevalidationCandidatesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromCanceled<int>(cts.Token));

        var act = () => CreateSut().Handle(new GetValidationStatsQuery(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
