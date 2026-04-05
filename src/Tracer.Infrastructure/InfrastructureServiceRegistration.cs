using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Tracer.Domain.Interfaces;
using Tracer.Infrastructure.Persistence;
using Tracer.Infrastructure.Persistence.Repositories;
using Tracer.Infrastructure.Providers.Ares;
using Tracer.Infrastructure.Providers.GleifLei;
using Tracer.Infrastructure.Providers.GoogleMaps;

namespace Tracer.Infrastructure;

/// <summary>
/// Registers Infrastructure layer services in the DI container.
/// </summary>
public static class InfrastructureServiceRegistration
{
    /// <summary>
    /// Adds Infrastructure layer services: DbContext, repositories, and unit of work.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The SQL Server connection string.</param>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString, nameof(connectionString));

        services.AddDbContext<TracerDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(TracerDbContext).Assembly.FullName);
                sql.EnableRetryOnFailure(maxRetryCount: 3);
            }));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<TracerDbContext>());
        services.AddScoped<ITraceRequestRepository, TraceRequestRepository>();
        services.AddScoped<ICompanyProfileRepository, CompanyProfileRepository>();
        services.AddScoped<IChangeEventRepository, ChangeEventRepository>();
        services.AddScoped<IValidationRecordRepository, ValidationRecordRepository>();

        // ARES client with resilience (retry 3x, timeout 10s)
        services.AddHttpClient<IAresClient, AresClient>(client =>
        {
            client.BaseAddress = new Uri("https://ares.gov.cz/ekonomicke-subjekty-v-be/rest/");
            client.Timeout = Timeout.InfiniteTimeSpan; // Polly controls all timeouts
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        })
        .AddStandardResilienceHandler(options =>
        {
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
            options.Retry.MaxRetryAttempts = 3;
        });

        // GLEIF LEI client with resilience (free API, no key)
        services.AddHttpClient<IGleifClient, GleifClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.gleif.org/api/v1/");
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.api+json"));
        })
        .AddStandardResilienceHandler(options =>
        {
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
            options.Retry.MaxRetryAttempts = 3;
        });

        // Google Maps Places API (New) — requires API key from configuration
        services.AddHttpClient<IGoogleMapsClient, GoogleMapsClient>((sp, client) =>
        {
            var config = sp.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
            var apiKey = config["Providers:GoogleMaps:ApiKey"]
                ?? throw new InvalidOperationException(
                    "Google Maps API key is not configured. Set 'Providers:GoogleMaps:ApiKey'.");

            client.BaseAddress = new Uri("https://places.googleapis.com/");
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.Add("X-Goog-Api-Key", apiKey);
        })
        .AddStandardResilienceHandler(options =>
        {
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);
            options.Retry.MaxRetryAttempts = 2;
        });

        // Enrichment providers
        services.AddTransient<IEnrichmentProvider, AresProvider>();
        services.AddTransient<IEnrichmentProvider, GleifProvider>();
        services.AddTransient<IEnrichmentProvider, GoogleMapsProvider>();

        return services;
    }
}
