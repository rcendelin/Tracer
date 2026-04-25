using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Tracer.Domain.Interfaces;

namespace Tracer.Infrastructure.Tests.Security;

/// <summary>
/// Integration tests for the B-87 API key authentication + rotation flow.
///
/// All tests run in Development mode because Tracer.Api's startup will
/// throw when no keys are configured in non-Development — we always inject
/// our own keys so that's not a concern — but Development keeps the
/// configuration narrow (no CORS origin requirement, no HSTS).
/// </summary>
public sealed class ApiKeyAuthTests
{
    private const string ActiveKey = "active-rotation-key-123";
    private const string ExpiredKey = "expired-rotation-key-456";

    private static WebApplicationFactory<Program> CreateFactory(
        IDictionary<string, string?> apiKeysConfig,
        TimeProvider? timeProvider = null)
    {
        #pragma warning disable CA2000 // factory disposed by caller
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");

                builder.ConfigureAppConfiguration((_, config) =>
                {
                    var values = new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:TracerDb"] = "Server=(localdb)\\mssqldb;Database=x;",
                    };
                    foreach (var (k, v) in apiKeysConfig)
                        values[k] = v;

                    config.AddInMemoryCollection(values);
                });

                builder.ConfigureTestServices(services =>
                {
                    services.ConfigureAll<Microsoft.Extensions.Http.Resilience.HttpStandardResilienceOptions>(
                        options => options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(2));

                    services.AddScoped(_ => Substitute.For<ITraceRequestRepository>());
                    services.AddScoped(_ => Substitute.For<IUnitOfWork>());

                    if (timeProvider is not null)
                    {
                        // Replace the real TimeProvider so tests can steer the "now".
                        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(TimeProvider));
                        if (descriptor is not null)
                            services.Remove(descriptor);
                        services.AddSingleton(timeProvider);
                    }
                });
            });
        #pragma warning restore CA2000
    }

    [Fact]
    public async Task UnknownKey_Returns401()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Auth:ApiKeys:0:Key"] = ActiveKey,
            ["Auth:ApiKeys:0:Label"] = "active",
        });

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "completely-wrong-key");

        using var response = await client.GetAsync(new Uri("/api/profiles", UriKind.Relative)).ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ActiveKey_WithoutExpiry_IsAccepted()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Auth:ApiKeys:0:Key"] = ActiveKey,
            ["Auth:ApiKeys:0:Label"] = "ci",
        });

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ActiveKey);

        using var response = await client.GetAsync(new Uri("/api/profiles", UriKind.Relative)).ConfigureAwait(true);

        // We accept any status other than 401/403 — the endpoint may still fail
        // further downstream (no DB), but the auth layer let the request through.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ExpiredKey_Returns401_EvenThoughListedInConfig()
    {
        var now = new DateTimeOffset(2026, 4, 22, 12, 0, 0, TimeSpan.Zero);
        var frozen = new FrozenTimeProvider(now);
        using var factory = CreateFactory(
            new Dictionary<string, string?>
            {
                ["Auth:ApiKeys:0:Key"] = ExpiredKey,
                ["Auth:ApiKeys:0:Label"] = "old",
                // Expires one hour from "now", i.e. still valid at config-validation time,
                // but below we advance the clock past expiry.
                ["Auth:ApiKeys:0:ExpiresAt"] = now.AddHours(1).ToString("O"),
                ["Auth:ApiKeys:1:Key"] = ActiveKey,
                ["Auth:ApiKeys:1:Label"] = "new",
            },
            frozen);

        using var client = factory.CreateClient();

        // Only advance the clock AFTER startup completed so ValidateOnStart passes.
        frozen.Advance(TimeSpan.FromHours(2));

        client.DefaultRequestHeaders.Add("X-Api-Key", ExpiredKey);
        using var response = await client.GetAsync(new Uri("/api/profiles", UriKind.Relative)).ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task OverlappingRotation_BothKeysAccepted()
    {
        var now = new DateTimeOffset(2026, 4, 22, 12, 0, 0, TimeSpan.Zero);
        var frozen = new FrozenTimeProvider(now);
        using var factory = CreateFactory(
            new Dictionary<string, string?>
            {
                // Old key still valid for 7 days
                ["Auth:ApiKeys:0:Key"] = ExpiredKey,
                ["Auth:ApiKeys:0:ExpiresAt"] = now.AddDays(7).ToString("O"),
                // New key already active, indefinite
                ["Auth:ApiKeys:1:Key"] = ActiveKey,
            },
            frozen);

        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Add("X-Api-Key", ExpiredKey);
        using (var r1 = await client.GetAsync(new Uri("/api/profiles", UriKind.Relative)).ConfigureAwait(true))
            r1.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);

        client.DefaultRequestHeaders.Remove("X-Api-Key");
        client.DefaultRequestHeaders.Add("X-Api-Key", ActiveKey);
        using (var r2 = await client.GetAsync(new Uri("/api/profiles", UriKind.Relative)).ConfigureAwait(true))
            r2.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void StartupFails_WhenKeyTooShort()
    {
        var act = () =>
        {
            using var factory = CreateFactory(new Dictionary<string, string?>
            {
                ["Auth:ApiKeys:0"] = "short",
            });
            // Force host build
            using var _ = factory.CreateClient();
        };

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*at least 16 characters*");
    }

    [Fact]
    public void StartupFails_WhenKeyAlreadyExpired()
    {
        var act = () =>
        {
            using var factory = CreateFactory(new Dictionary<string, string?>
            {
                ["Auth:ApiKeys:0:Key"] = ActiveKey,
                ["Auth:ApiKeys:0:ExpiresAt"] = "2000-01-01T00:00:00Z",
            });
            using var _ = factory.CreateClient();
        };

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*in the past*");
    }

    [Fact]
    public void StartupFails_WhenKeysDuplicate()
    {
        var act = () =>
        {
            using var factory = CreateFactory(new Dictionary<string, string?>
            {
                ["Auth:ApiKeys:0:Key"] = ActiveKey,
                ["Auth:ApiKeys:1:Key"] = ActiveKey,
            });
            using var _ = factory.CreateClient();
        };

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*duplicate*");
    }

    /// <summary>
    /// Mutable TimeProvider for tests that need to advance the clock after the
    /// host starts up.
    /// </summary>
    private sealed class FrozenTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public FrozenTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
