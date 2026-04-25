using FluentAssertions;
using NSubstitute;
using Tracer.Application.Queries.GetDashboardStats;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Tests.Queries.GetDashboardStats;

public sealed class GetDashboardStatsHandlerTests
{
    private readonly ITraceRequestRepository _traceRepo = Substitute.For<ITraceRequestRepository>();
    private readonly ICompanyProfileRepository _profileRepo = Substitute.For<ICompanyProfileRepository>();

    private GetDashboardStatsHandler CreateSut() => new(_traceRepo, _profileRepo);

    [Fact]
    public async Task Handle_AllEmpty_ReturnsZeros()
    {
        _traceRepo.CountAsync(
                status: Arg.Any<Domain.Enums.TraceStatus?>(),
                dateFrom: Arg.Any<DateTimeOffset?>(),
                dateTo: Arg.Any<DateTimeOffset?>(),
                search: Arg.Any<string?>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(0);
        _profileRepo.CountAsync(
                search: Arg.Any<string?>(),
                country: Arg.Any<string?>(),
                minConfidence: Arg.Any<double?>(),
                maxConfidence: Arg.Any<double?>(),
                validatedBefore: Arg.Any<DateTimeOffset?>(),
                includeArchived: Arg.Any<bool>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(0);
        _profileRepo.GetAverageConfidenceAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(0.0);

        var result = await CreateSut().Handle(new GetDashboardStatsQuery(), CancellationToken.None);

        result.TracesToday.Should().Be(0);
        result.TracesThisWeek.Should().Be(0);
        result.TotalProfiles.Should().Be(0);
        result.AverageConfidence.Should().Be(0.0);
    }

    [Fact]
    public async Task Handle_PopulatedData_ProjectsRepositoryValues()
    {
        _traceRepo.CountAsync(
                status: Arg.Any<Domain.Enums.TraceStatus?>(),
                dateFrom: Arg.Any<DateTimeOffset?>(),
                dateTo: Arg.Any<DateTimeOffset?>(),
                search: Arg.Any<string?>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(7, 42);          // first call = today, second call = this week
        _profileRepo.CountAsync(
                search: Arg.Any<string?>(),
                country: Arg.Any<string?>(),
                minConfidence: Arg.Any<double?>(),
                maxConfidence: Arg.Any<double?>(),
                validatedBefore: Arg.Any<DateTimeOffset?>(),
                includeArchived: Arg.Any<bool>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(128);
        _profileRepo.GetAverageConfidenceAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(0.81);

        var result = await CreateSut().Handle(new GetDashboardStatsQuery(), CancellationToken.None);

        result.TracesToday.Should().Be(7);
        result.TracesThisWeek.Should().Be(42);
        result.TotalProfiles.Should().Be(128);
        result.AverageConfidence.Should().BeApproximately(0.81, 1e-9);
    }

    [Fact]
    public async Task Handle_AverageConfidenceRepoReturnsZero_PassesThroughAsNoDataSignal()
    {
        // Empty set / all-null confidences come back as 0.0 from the repo — the handler
        // must forward that verbatim so the UI can render the "no data" placeholder.
        _traceRepo.CountAsync(
                status: Arg.Any<Domain.Enums.TraceStatus?>(),
                dateFrom: Arg.Any<DateTimeOffset?>(),
                dateTo: Arg.Any<DateTimeOffset?>(),
                search: Arg.Any<string?>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(0);
        _profileRepo.CountAsync(
                search: Arg.Any<string?>(),
                country: Arg.Any<string?>(),
                minConfidence: Arg.Any<double?>(),
                maxConfidence: Arg.Any<double?>(),
                validatedBefore: Arg.Any<DateTimeOffset?>(),
                includeArchived: Arg.Any<bool>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(3);
        _profileRepo.GetAverageConfidenceAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(0.0);

        var result = await CreateSut().Handle(new GetDashboardStatsQuery(), CancellationToken.None);

        result.AverageConfidence.Should().Be(0.0);
    }

    [Fact]
    public async Task Handle_PropagatesCancellationToAllRepositories()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _traceRepo.CountAsync(
                status: Arg.Any<Domain.Enums.TraceStatus?>(),
                dateFrom: Arg.Any<DateTimeOffset?>(),
                dateTo: Arg.Any<DateTimeOffset?>(),
                search: Arg.Any<string?>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new OperationCanceledException(cts.Token));

        var act = () => CreateSut().Handle(new GetDashboardStatsQuery(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
