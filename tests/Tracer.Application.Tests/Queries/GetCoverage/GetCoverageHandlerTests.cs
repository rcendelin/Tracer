using FluentAssertions;
using NSubstitute;
using Tracer.Application.Queries.GetCoverage;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Tests.Queries.GetCoverage;

public sealed class GetCoverageHandlerTests
{
    private readonly ICompanyProfileRepository _repository = Substitute.For<ICompanyProfileRepository>();

    private GetCoverageHandler CreateSut(DateTimeOffset now) =>
        new(_repository) { NowProvider = () => now };

    // ── Averages ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_MultipleCountries_ComputesAveragesFromSums()
    {
        var now = new DateTimeOffset(2026, 4, 22, 0, 0, 0, TimeSpan.Zero);
        _repository.GetCoverageByCountryAsync(now, GetCoverageHandler.MaxCountries, Arg.Any<CancellationToken>())
            .Returns(new CoverageByCountryRow[]
            {
                new()
                {
                    Country = "CZ",
                    ProfileCount = 100,
                    ConfidenceSampleCount = 80,
                    ConfidenceSum = 64.0,       // avg = 0.8
                    EnrichedSampleCount = 50,
                    EnrichedSumDays = 2500,     // avg = 50 days
                },
                new()
                {
                    Country = "DE",
                    ProfileCount = 40,
                    ConfidenceSampleCount = 40,
                    ConfidenceSum = 32.0,       // avg = 0.8
                    EnrichedSampleCount = 20,
                    EnrichedSumDays = 1400,     // avg = 70 days
                },
            });

        var result = await CreateSut(now).Handle(new GetCoverageQuery(CoverageGroupBy.Country), CancellationToken.None);

        result.GroupBy.Should().Be(CoverageGroupBy.Country);
        result.Entries.Should().HaveCount(2);
        var cz = result.Entries.Single(e => e.Group == "CZ");
        cz.ProfileCount.Should().Be(100);
        cz.AvgConfidence.Should().BeApproximately(0.8, 0.001);
        cz.AvgDataAgeDays.Should().BeApproximately(50.0, 0.001);
        var de = result.Entries.Single(e => e.Group == "DE");
        de.AvgDataAgeDays.Should().BeApproximately(70.0, 0.001);
    }

    [Fact]
    public async Task Handle_NoSamples_AvgDefaultsToZero()
    {
        var now = new DateTimeOffset(2026, 4, 22, 0, 0, 0, TimeSpan.Zero);
        _repository.GetCoverageByCountryAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new CoverageByCountryRow[]
            {
                new()
                {
                    Country = "XX",
                    ProfileCount = 5,
                    ConfidenceSampleCount = 0,
                    ConfidenceSum = 0,
                    EnrichedSampleCount = 0,
                    EnrichedSumDays = 0,
                },
            });

        var result = await CreateSut(now).Handle(new GetCoverageQuery(CoverageGroupBy.Country), CancellationToken.None);

        var xx = result.Entries.Single();
        xx.AvgConfidence.Should().Be(0d);
        xx.AvgDataAgeDays.Should().Be(0d);
        xx.ProfileCount.Should().Be(5);
    }

    [Fact]
    public async Task Handle_EmptyCountryString_MapsToNullGroup()
    {
        // Defence: if a migration ever lets a blank Country slip through, the UI
        // should render it as "unknown" rather than an empty string.
        var now = new DateTimeOffset(2026, 4, 22, 0, 0, 0, TimeSpan.Zero);
        _repository.GetCoverageByCountryAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new CoverageByCountryRow[]
            {
                new()
                {
                    Country = "",
                    ProfileCount = 1,
                    ConfidenceSampleCount = 0,
                    ConfidenceSum = 0,
                    EnrichedSampleCount = 0,
                    EnrichedSumDays = 0,
                },
            });

        var result = await CreateSut(now).Handle(new GetCoverageQuery(CoverageGroupBy.Country), CancellationToken.None);

        result.Entries.Single().Group.Should().BeNull();
    }

    // ── Repository contract ───────────────────────────────────────────────

    [Fact]
    public async Task Handle_PassesNowAndMaxCountriesToRepository()
    {
        var now = new DateTimeOffset(2026, 4, 22, 0, 0, 0, TimeSpan.Zero);
        _repository.GetCoverageByCountryAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CoverageByCountryRow>());

        await CreateSut(now).Handle(new GetCoverageQuery(CoverageGroupBy.Country), CancellationToken.None);

        await _repository.Received(1).GetCoverageByCountryAsync(
            now, GetCoverageHandler.MaxCountries, Arg.Any<CancellationToken>());
    }

    // ── Cancellation ──────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CancelledToken_Propagates()
    {
        var now = new DateTimeOffset(2026, 4, 22, 0, 0, 0, TimeSpan.Zero);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _repository.GetCoverageByCountryAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromCanceled<IReadOnlyList<CoverageByCountryRow>>(cts.Token));

        var act = () => CreateSut(now).Handle(new GetCoverageQuery(CoverageGroupBy.Country), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
