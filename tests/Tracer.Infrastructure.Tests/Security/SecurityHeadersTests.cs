using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Tracer.Domain.Interfaces;

namespace Tracer.Infrastructure.Tests.Security;

/// <summary>
/// Integration tests for the B-87 security header + HSTS pipeline.
/// Uses <see cref="WebApplicationFactory{TEntryPoint}"/> without a subclass
/// per the project convention (Program is internal partial).
///
/// The tests hit <c>GET /health</c>. The security headers middleware runs
/// before routing, so headers are applied regardless of whether the health
/// probe itself returns 200 or 503. Response status is intentionally not
/// asserted — only the headers are in scope.
/// </summary>
public sealed class SecurityHeadersTests
{
    private const string TestApiKey = "test-security-headers-key";

    private static WebApplicationFactory<Program> CreateFactory(
        string environmentName,
        IDictionary<string, string?>? extraConfig = null)
    {
        #pragma warning disable CA2000 // factory disposed by caller
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environmentName);

                builder.ConfigureAppConfiguration((_, config) =>
                {
                    var values = new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:TracerDb"] = "Server=(localdb)\\mssqldb;Database=x;",
                        ["Auth:ApiKeys:0"] = TestApiKey,
                        ["Cors:AllowedOrigins:0"] = "https://example.com",
                    };
                    if (extraConfig is not null)
                    {
                        foreach (var (k, v) in extraConfig)
                            values[k] = v;
                    }

                    config.AddInMemoryCollection(values);
                });

                builder.ConfigureTestServices(services =>
                {
                    services.ConfigureAll<Microsoft.Extensions.Http.Resilience.HttpStandardResilienceOptions>(
                        options => options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(2));

                    // Mock out repositories that back the endpoints we might hit.
                    services.AddScoped(_ => Substitute.For<ITraceRequestRepository>());
                    services.AddScoped(_ => Substitute.For<IUnitOfWork>());
                });
            });
        #pragma warning restore CA2000
    }

    private static HttpClient CreateAuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiKey);
        return client;
    }

    private static IReadOnlyDictionary<string, string> CollectHeaders(HttpResponseMessage response)
    {
        // Security headers may land on either the response or the content headers depending on
        // which middleware ultimately wrote them. We flatten both into a single case-insensitive dict.
        return response.Headers.Concat(response.Content.Headers)
            .ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Production_Includes_AllSecurityHeaders()
    {
        using var factory = CreateFactory("Production");
        using var client = CreateAuthenticatedClient(factory);

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative)).ConfigureAwait(true);

        var headers = CollectHeaders(response);

        headers.Should().ContainKey("Strict-Transport-Security");
        headers["Strict-Transport-Security"].Should().Contain("max-age=").And.Contain("includeSubDomains");
        headers.Should().ContainKey("Content-Security-Policy");
        headers["Content-Security-Policy"].Should().Contain("default-src 'none'");
        headers.Should().ContainKey("X-Content-Type-Options");
        headers["X-Content-Type-Options"].Should().Be("nosniff");
        headers.Should().ContainKey("X-Frame-Options");
        headers["X-Frame-Options"].Should().Be("DENY");
        headers.Should().ContainKey("Referrer-Policy");
        headers["Referrer-Policy"].Should().Be("no-referrer");
        headers.Should().ContainKey("Permissions-Policy");
        headers.Should().ContainKey("Cross-Origin-Opener-Policy");
        headers.Should().ContainKey("Cross-Origin-Resource-Policy");
    }

    [Fact]
    public async Task Development_Omits_HstsHeader()
    {
        using var factory = CreateFactory("Development");
        using var client = CreateAuthenticatedClient(factory);

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative)).ConfigureAwait(true);

        response.Headers.Contains("Strict-Transport-Security").Should().BeFalse(
            "HSTS would pin localhost HTTPS dev certs in the browser");

        // Other headers are still applied in development.
        var headers = CollectHeaders(response);
        headers.Should().ContainKey("Content-Security-Policy");
    }

    [Fact]
    public async Task UnauthorizedResponse_StillIncludes_SecurityHeaders()
    {
        using var factory = CreateFactory("Production");
        // No X-Api-Key → expect 401.
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync(new Uri("/api/profiles", UriKind.Relative)).ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var headers = CollectHeaders(response);
        headers.Should().ContainKey("Content-Security-Policy");
        headers.Should().ContainKey("X-Content-Type-Options");
    }

    [Fact]
    public async Task WhenDisabled_MiddlewareHeadersAreOmitted_ButHstsStillEmitted()
    {
        using var factory = CreateFactory("Production", new Dictionary<string, string?>
        {
            ["Security:Headers:Enabled"] = "false",
        });
        using var client = CreateAuthenticatedClient(factory);

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative)).ConfigureAwait(true);

        response.Headers.Contains("Content-Security-Policy").Should().BeFalse();
        response.Headers.Contains("Permissions-Policy").Should().BeFalse();
        response.Headers.Contains("Strict-Transport-Security").Should().BeTrue(
            "HSTS is emitted by the built-in UseHsts middleware, independent of our flag");
    }

    [Fact]
    public async Task Response_DoesNotLeakServerHeader()
    {
        using var factory = CreateFactory("Production");
        using var client = CreateAuthenticatedClient(factory);

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative)).ConfigureAwait(true);

        response.Headers.Contains("Server").Should().BeFalse(
            "the Server header is stripped to reduce stack fingerprinting");
    }
}
