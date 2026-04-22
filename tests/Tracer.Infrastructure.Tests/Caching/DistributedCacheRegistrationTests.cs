using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tracer.Infrastructure.Caching;

namespace Tracer.Infrastructure.Tests.Caching;

/// <summary>
/// Verifies the B-79 cache registration branch — default = in-memory,
/// Redis = StackExchangeRedisCache, Redis without connection string = boot failure.
/// </summary>
public sealed class DistributedCacheRegistrationTests
{
    private static IConfiguration BuildConfig(IDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Default_ResolvesToMemoryDistributedCache()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>());

        services.AddTracerDistributedCache(config);
        services.AddOptions();
        var provider = services.BuildServiceProvider();

        var cache = provider.GetRequiredService<IDistributedCache>();

        cache.Should().BeOfType<MemoryDistributedCache>();
    }

    [Fact]
    public void ProviderInMemory_ResolvesToMemoryDistributedCache()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Cache:Provider"] = "InMemory",
        });

        services.AddTracerDistributedCache(config);
        services.AddOptions();
        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IDistributedCache>().Should().BeOfType<MemoryDistributedCache>();
    }

    [Fact]
    public void ProviderRedis_RegistersStackExchangeRedisCache()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Cache:Provider"] = "Redis",
            ["ConnectionStrings:Redis"] = "localhost:6379,abortConnect=False",
        });

        services.AddTracerDistributedCache(config);
        services.AddOptions();

        // We deliberately do NOT resolve IDistributedCache — the StackExchange.Redis
        // implementation attempts a TCP connection on first use, which would make
        // this an integration test masquerading as a unit test. Inspect the registered
        // descriptors directly so we cover both ImplementationType and factory registrations.
        var descriptor = services.LastOrDefault(d => d.ServiceType == typeof(IDistributedCache));
        descriptor.Should().NotBeNull();

        var registeredTypeName = descriptor!.ImplementationType?.FullName
            ?? descriptor.ImplementationInstance?.GetType().FullName
            // Factory registration: probe the closure target's declaring type if reachable.
            ?? descriptor.ImplementationFactory?.Method.DeclaringType?.FullName;

        registeredTypeName.Should().NotBeNull();
        registeredTypeName!.Should().Contain("StackExchangeRedis");
        registeredTypeName.Should().NotContain("MemoryDistributedCache");
    }

    [Fact]
    public void ProviderRedis_WithoutConnectionString_FailsValidation()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Cache:Provider"] = "Redis",
            // No ConnectionStrings:Redis
        });

        services.AddTracerDistributedCache(config);
        services.AddOptions();
        var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<CacheOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*ConnectionStrings:Redis is required*");
    }

    [Fact]
    public void InvalidWarmingMaxProfiles_FailsValidation()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Cache:Warming:MaxProfiles"] = "0",
        });

        services.AddTracerDistributedCache(config);
        services.AddOptions();
        var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<CacheOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*Warming:MaxProfiles*");
    }

    [Fact]
    public void NegativeProfileTtl_FailsValidation()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Cache:ProfileTtl"] = "-00:00:01",
        });

        services.AddTracerDistributedCache(config);
        services.AddOptions();
        var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<CacheOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*ProfileTtl*");
    }

    [Fact]
    public void Defaults_AreSafeForDevAndCi()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>());

        services.AddTracerDistributedCache(config);
        services.AddOptions();
        var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<CacheOptions>>().Value;
        options.Provider.Should().Be(CacheProvider.InMemory);
        options.Warming.Enabled.Should().BeFalse();
        options.Warming.MaxProfiles.Should().Be(1000);
        options.ProfileTtl.Should().Be(TimeSpan.FromDays(7));
        options.RedisInstanceName.Should().Be("tracer:");
    }
}
