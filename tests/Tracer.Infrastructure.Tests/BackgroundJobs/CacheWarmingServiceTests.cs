using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Tracer.Application.DTOs;
using Tracer.Application.Services;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;
using Tracer.Infrastructure.BackgroundJobs;
using Tracer.Infrastructure.Caching;

namespace Tracer.Infrastructure.Tests.BackgroundJobs;

/// <summary>
/// Unit tests for <see cref="CacheWarmingService"/>. Drives <c>WarmAsync</c>
/// directly so tests do not wait on the startup delay; the
/// <see cref="CacheWarmingService.DelayAsync"/> seam is exercised separately.
/// </summary>
public sealed class CacheWarmingServiceTests
{
    private readonly ICompanyProfileRepository _repository = Substitute.For<ICompanyProfileRepository>();
    private readonly IProfileCacheService _cache = Substitute.For<IProfileCacheService>();

    private CacheWarmingService CreateService(CacheOptions options)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_repository);
        services.AddSingleton(_cache);
        var provider = services.BuildServiceProvider();

        return new CacheWarmingService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<CacheWarmingService>.Instance)
        {
            DelayAsync = static (_, _) => Task.CompletedTask,
        };
    }

    private static CompanyProfile BuildProfile(string registrationId)
    {
        var profile = new CompanyProfile($"CZ:{registrationId}", "CZ", registrationId);
        profile.UpdateField(
            FieldName.LegalName,
            new TracedField<string>
            {
                Value = $"Company {registrationId}",
                Confidence = Confidence.Create(0.9),
                Source = "ares",
                EnrichedAt = DateTimeOffset.UtcNow,
            },
            "ares");
        return profile;
    }

    [Fact]
    public async Task WarmAsync_LoadsTopProfilesAndPopulatesCache()
    {
        var profiles = new[] { BuildProfile("11111111"), BuildProfile("22222222") };
        _repository.ListTopByTraceCountAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(profiles);

        var sut = CreateService(new CacheOptions
        {
            Warming = new CacheWarmingOptions { Enabled = true, MaxProfiles = 50 },
        });

        await sut.WarmAsync(CancellationToken.None);

        await _repository.Received(1).ListTopByTraceCountAsync(50, Arg.Any<CancellationToken>());
        await _cache.Received(1).SetAsync("CZ:11111111", Arg.Any<CompanyProfileDto>(), Arg.Any<CancellationToken>());
        await _cache.Received(1).SetAsync("CZ:22222222", Arg.Any<CompanyProfileDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WarmAsync_RepositoryThrows_DoesNotThrow()
    {
        _repository.ListTopByTraceCountAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("db down"));

        var sut = CreateService(new CacheOptions
        {
            Warming = new CacheWarmingOptions { Enabled = true, MaxProfiles = 50 },
        });

        var act = async () => await sut.WarmAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        await _cache.DidNotReceive()
            .SetAsync(Arg.Any<string>(), Arg.Any<CompanyProfileDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WarmAsync_PerProfileFailure_ContinuesWithRemaining()
    {
        var profiles = new[] { BuildProfile("11111111"), BuildProfile("22222222"), BuildProfile("33333333") };
        _repository.ListTopByTraceCountAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(profiles);

        // The middle SetAsync throws; the other two must still be called.
        _cache.SetAsync("CZ:22222222", Arg.Any<CompanyProfileDto>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("redis transient error"));

        var sut = CreateService(new CacheOptions
        {
            Warming = new CacheWarmingOptions { Enabled = true, MaxProfiles = 50 },
        });

        await sut.WarmAsync(CancellationToken.None);

        await _cache.Received(1).SetAsync("CZ:11111111", Arg.Any<CompanyProfileDto>(), Arg.Any<CancellationToken>());
        await _cache.Received(1).SetAsync("CZ:33333333", Arg.Any<CompanyProfileDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WarmAsync_CancelledMidStream_StopsGracefully()
    {
        var profiles = new[] { BuildProfile("11111111"), BuildProfile("22222222") };
        _repository.ListTopByTraceCountAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(profiles);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sut = CreateService(new CacheOptions
        {
            Warming = new CacheWarmingOptions { Enabled = true, MaxProfiles = 50 },
        });

        var act = async () => await sut.WarmAsync(cts.Token);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_WhenWarmingDisabled_DoesNothing()
    {
        var sut = CreateService(new CacheOptions
        {
            Warming = new CacheWarmingOptions { Enabled = false },
        });

        await sut.StartAsync(CancellationToken.None);
        await sut.StopAsync(CancellationToken.None);

        await _repository.DidNotReceive()
            .ListTopByTraceCountAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
