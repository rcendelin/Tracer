using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Tracer.Infrastructure.HealthChecks;

namespace Tracer.Infrastructure.Tests.HealthChecks;

/// <summary>
/// Verifies <see cref="RedisHealthCheck"/> never throws and never reports
/// <see cref="HealthStatus.Unhealthy"/>. Cache failures degrade the API
/// rather than triggering Azure auto-restart.
/// </summary>
public sealed class RedisHealthCheckTests
{
    private readonly IDistributedCache _cache = Substitute.For<IDistributedCache>();
    private readonly HealthCheckContext _context = new();

    [Fact]
    public async Task RoundTripSucceeds_ReturnsHealthy()
    {
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(System.Text.Encoding.UTF8.GetBytes("ok"));

        var sut = new RedisHealthCheck(_cache);

        var result = await sut.CheckHealthAsync(_context, CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task SetThrows_ReturnsDegradedNotUnhealthy()
    {
        _cache.SetAsync(
                Arg.Any<string>(),
                Arg.Any<byte[]>(),
                Arg.Any<DistributedCacheEntryOptions>(),
                Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("redis down"));

        var sut = new RedisHealthCheck(_cache);

        var result = await sut.CheckHealthAsync(_context, CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Degraded);
        // Description must mention failure type but never the exception message
        // (CWE-209 — Redis errors can include connection-string credentials).
        result.Description.Should().NotContain("redis down");
        result.Description.Should().Contain("InvalidOperationException");
    }

    [Fact]
    public async Task ReadReturnsNull_ReturnsDegraded()
    {
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((byte[]?)null);

        var sut = new RedisHealthCheck(_cache);

        var result = await sut.CheckHealthAsync(_context, CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task RemoveCleanupFailure_DoesNotAffectVerdict()
    {
        // Set + Get succeed → verdict should be Healthy even when cleanup throws.
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(System.Text.Encoding.UTF8.GetBytes("ok"));
        _cache.RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("eviction race"));

        var sut = new RedisHealthCheck(_cache);

        var result = await sut.CheckHealthAsync(_context, CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Healthy);
    }
}
