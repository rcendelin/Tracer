using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Tracer.Application.Services;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;
using Tracer.Infrastructure.BackgroundJobs;

namespace Tracer.Infrastructure.Tests.BackgroundJobs;

/// <summary>
/// Unit tests for <see cref="RevalidationScheduler"/>. Drives a single
/// tick via the internal <c>RunTickAsync</c> seam so tests never wait on
/// real clock delays. The scheduler is wired up with NSubstitute fakes
/// for the repository, runner, metrics, and queue — no SQL, no HTTP.
/// </summary>
public sealed class RevalidationSchedulerTests
{
    private readonly ICompanyProfileRepository _repository = Substitute.For<ICompanyProfileRepository>();
    private readonly IRevalidationRunner _runner = Substitute.For<IRevalidationRunner>();
    private readonly IRevalidationQueue _manualQueue = Substitute.For<IRevalidationQueue>();
    private readonly ITracerMetrics _metrics = Substitute.For<ITracerMetrics>();

    private RevalidationScheduler CreateScheduler(
        RevalidationOptions options,
        DateTimeOffset? now = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_repository);
        services.AddSingleton(_runner);
        services.AddSingleton(_manualQueue);
        var provider = services.BuildServiceProvider();

        // Default: manual queue returns empty so auto sweep dominates the test.
        _manualQueue.TryDequeue(out Arg.Any<Guid>()).Returns(call =>
        {
            call[0] = Guid.Empty;
            return false;
        });

        return new RevalidationScheduler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            _manualQueue,
            _metrics,
            Options.Create(options),
            NullLogger<RevalidationScheduler>.Instance)
        {
            Clock = () => now ?? DateTimeOffset.UtcNow,
        };
    }

    private static CompanyProfile ProfileWithExpiredEntityStatus()
    {
        var profile = new CompanyProfile("CZ:12345678", "CZ", "12345678");
        // EntityStatus TTL is 30 days — 60 days ago is expired.
        profile.UpdateField(
            FieldName.EntityStatus,
            new TracedField<string>
            {
                Value = "active",
                Confidence = Confidence.Create(0.9),
                Source = "ares",
                EnrichedAt = DateTimeOffset.UtcNow.AddDays(-60),
            },
            "ares");
        return profile;
    }

    private static CompanyProfile ProfileWithFreshFields()
    {
        var profile = new CompanyProfile("CZ:87654321", "CZ", "87654321");
        profile.UpdateField(
            FieldName.EntityStatus,
            new TracedField<string>
            {
                Value = "active",
                Confidence = Confidence.Create(0.9),
                Source = "ares",
                EnrichedAt = DateTimeOffset.UtcNow,
            },
            "ares");
        return profile;
    }

    [Fact]
    public async Task RunTickAsync_OutsideOffPeak_SkipsAutomaticSweep()
    {
        var options = new RevalidationOptions
        {
            MaxProfilesPerRun = 10,
            OffPeak = new OffPeakWindow { Enabled = true, StartHourUtc = 22, EndHourUtc = 6 },
        };
        // 12:00 UTC is outside the 22-6 window.
        var sut = CreateScheduler(options, new DateTimeOffset(2026, 4, 21, 12, 0, 0, TimeSpan.Zero));

        await sut.RunTickAsync(CancellationToken.None);

        await _repository.DidNotReceive().GetRevalidationQueueAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _runner.DidNotReceive().RunAsync(Arg.Any<CompanyProfile>(), Arg.Any<CancellationToken>());
        _metrics.DidNotReceive().RecordRevalidationRun(
            "auto", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<double>());
    }

    [Fact]
    public async Task RunTickAsync_InsideOffPeak_CallsRepositoryWithBudget()
    {
        var options = new RevalidationOptions
        {
            MaxProfilesPerRun = 42,
            OffPeak = new OffPeakWindow { Enabled = true, StartHourUtc = 22, EndHourUtc = 6 },
        };
        var sut = CreateScheduler(options, new DateTimeOffset(2026, 4, 21, 23, 0, 0, TimeSpan.Zero));

        _repository.GetRevalidationQueueAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CompanyProfile>());

        await sut.RunTickAsync(CancellationToken.None);

        await _repository.Received(1).GetRevalidationQueueAsync(42, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunTickAsync_WithExpiredProfile_CallsRunner()
    {
        var profile = ProfileWithExpiredEntityStatus();
        var sut = CreateScheduler(new RevalidationOptions());

        _repository.GetRevalidationQueueAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { profile });
        _runner.RunAsync(Arg.Any<CompanyProfile>(), Arg.Any<CancellationToken>())
            .Returns(RevalidationOutcome.Lightweight);

        await sut.RunTickAsync(CancellationToken.None);

        await _runner.Received(1).RunAsync(profile, Arg.Any<CancellationToken>());
        _metrics.Received().RecordRevalidationRun(
            "auto", 1, 0, 0, Arg.Any<double>());
    }

    [Fact]
    public async Task RunTickAsync_SkipsProfilesWithoutExpiredFields()
    {
        var fresh = ProfileWithFreshFields();
        var sut = CreateScheduler(new RevalidationOptions());

        _repository.GetRevalidationQueueAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { fresh });

        await sut.RunTickAsync(CancellationToken.None);

        await _runner.DidNotReceive().RunAsync(Arg.Any<CompanyProfile>(), Arg.Any<CancellationToken>());
        _metrics.Received().RecordRevalidationRun(
            "auto", 0, 1, 0, Arg.Any<double>());
    }

    [Fact]
    public async Task RunTickAsync_WhenRunnerThrows_IsCountedAsFailed()
    {
        var profile = ProfileWithExpiredEntityStatus();
        var sut = CreateScheduler(new RevalidationOptions());

        _repository.GetRevalidationQueueAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { profile });
        _runner.RunAsync(Arg.Any<CompanyProfile>(), Arg.Any<CancellationToken>())
            .Returns<RevalidationOutcome>(_ => throw new InvalidOperationException("boom"));

        await sut.RunTickAsync(CancellationToken.None);

        _metrics.Received().RecordRevalidationRun(
            "auto", 0, 0, 1, Arg.Any<double>());
    }

    [Fact]
    public async Task RunTickAsync_ManualQueue_IsProcessedBeforeOffPeakGate()
    {
        // Outside off-peak → auto sweep skipped, but manual queue must still drain.
        var profileId = Guid.NewGuid();
        var options = new RevalidationOptions
        {
            OffPeak = new OffPeakWindow { Enabled = true, StartHourUtc = 22, EndHourUtc = 6 },
        };
        var sut = CreateScheduler(options, new DateTimeOffset(2026, 4, 21, 12, 0, 0, TimeSpan.Zero));

        var callCount = 0;
        _manualQueue.TryDequeue(out Arg.Any<Guid>()).Returns(call =>
        {
            callCount++;
            if (callCount == 1)
            {
                call[0] = profileId;
                return true;
            }
            call[0] = Guid.Empty;
            return false;
        });
        var profile = ProfileWithExpiredEntityStatus();
        _repository.GetByIdAsync(profileId, Arg.Any<CancellationToken>()).Returns(profile);
        _runner.RunAsync(Arg.Any<CompanyProfile>(), Arg.Any<CancellationToken>())
            .Returns(RevalidationOutcome.Lightweight);

        await sut.RunTickAsync(CancellationToken.None);

        await _runner.Received(1).RunAsync(profile, Arg.Any<CancellationToken>());
        _metrics.Received().RecordRevalidationRun(
            "manual", 1, 0, 0, Arg.Any<double>());
        // Auto sweep was gated out.
        await _repository.DidNotReceive().GetRevalidationQueueAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunTickAsync_ManualQueue_MissingProfile_IsDropped()
    {
        var profileId = Guid.NewGuid();
        var sut = CreateScheduler(new RevalidationOptions());

        var callCount = 0;
        _manualQueue.TryDequeue(out Arg.Any<Guid>()).Returns(call =>
        {
            callCount++;
            if (callCount == 1)
            {
                call[0] = profileId;
                return true;
            }
            call[0] = Guid.Empty;
            return false;
        });
        _repository.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns((CompanyProfile?)null);
        _repository.GetRevalidationQueueAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CompanyProfile>());

        await sut.RunTickAsync(CancellationToken.None);

        await _runner.DidNotReceive().RunAsync(Arg.Any<CompanyProfile>(), Arg.Any<CancellationToken>());
        _metrics.Received().RecordRevalidationRun(
            "manual", 0, 1, 0, Arg.Any<double>());
    }
}
