using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Tracer.Domain.Interfaces;
using Tracer.Infrastructure.Persistence;
using Tracer.Infrastructure.Persistence.Repositories;
using Tracer.Infrastructure.Providers.Ares;
using Tracer.Infrastructure.Providers.GleifLei;
using Tracer.Infrastructure.Providers.GoogleMaps;
using Tracer.Infrastructure.Providers.AzureMaps;
using Tracer.Infrastructure.Messaging;
using Tracer.Application.Services;
using Tracer.Application.Messaging;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Tracer.Infrastructure.Providers.CompaniesHouse;

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
        services.AddScoped<ISourceResultRepository, SourceResultRepository>();

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

        // Azure Maps Geocoding — subscription key from configuration
        services.AddHttpClient<IAzureMapsClient, AzureMapsClient>((sp, client) =>
        {
            var cfg = sp.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
            var azureMapsKey = cfg["Providers:AzureMaps:SubscriptionKey"]
                ?? throw new InvalidOperationException(
                    "Azure Maps subscription key is not configured. Set 'Providers:AzureMaps:SubscriptionKey'.");

            client.BaseAddress = new Uri("https://atlas.microsoft.com/");
            client.Timeout = Timeout.InfiniteTimeSpan;
            // Pass subscription key via default query string is not possible with HttpClient,
            // so we store it in a custom header and the client reads it.
            client.DefaultRequestHeaders.Add("X-AzureMaps-Key", azureMapsKey);
        })
        .AddStandardResilienceHandler(options =>
        {
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);
            options.Retry.MaxRetryAttempts = 2;
        });

        // Webhook callback client with retry (3x exponential backoff)
        services.AddHttpClient<IWebhookCallbackService, Webhooks.WebhookCallbackService>(client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
        })
        .AddStandardResilienceHandler(options =>
        {
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(90);
            options.Retry.MaxRetryAttempts = 3;
            options.Retry.Delay = TimeSpan.FromSeconds(2);
        });

        // Companies House API (UK) — optional, requires API key
        services.AddHttpClient<ICompaniesHouseClient, CompaniesHouseClient>((sp, client) =>
        {
            var chConfig = sp.GetRequiredService<IConfiguration>();
            var apiKey = chConfig["Providers:CompaniesHouse:ApiKey"] ?? string.Empty;

            client.BaseAddress = new Uri("https://api.company-information.service.gov.uk/");
            client.Timeout = Timeout.InfiniteTimeSpan;

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                var credentials = Convert.ToBase64String(
                    System.Text.Encoding.ASCII.GetBytes($"{apiKey}:"));
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
            }
        })
        .AddStandardResilienceHandler(options =>
        {
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
            options.Retry.MaxRetryAttempts = 3;
        });

        // Enrichment providers
        services.AddTransient<IEnrichmentProvider, AresProvider>();
        services.AddTransient<IEnrichmentProvider, GleifProvider>();
        services.AddTransient<IEnrichmentProvider, GoogleMapsProvider>();
        services.AddTransient<IEnrichmentProvider, AzureMapsProvider>();
        services.AddTransient<IEnrichmentProvider, CompaniesHouseProvider>();

        // Service Bus (optional — activated only if connection string is configured)
        services.AddSingleton<IServiceBusPublisher>(sp =>
        {
            var sbConfig = sp.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
            var sbConnectionString = sbConfig["ConnectionStrings:ServiceBus"];
            if (string.IsNullOrWhiteSpace(sbConnectionString))
                return new NullServiceBusPublisher();

            var client = sp.GetRequiredService<ServiceBusClient>();
            var sbOptions = new ServiceBusOptions();
            sbConfig.GetSection(ServiceBusOptions.SectionName).Bind(sbOptions);
            var optionsWrapper = Microsoft.Extensions.Options.Options.Create(sbOptions);
            var logger = sp.GetRequiredService<ILogger<ServiceBusPublisher>>();
            return new ServiceBusPublisher(client, optionsWrapper, logger);
        });

        // Note: ServiceBusConsumer (BackgroundService) is registered in Program.cs
        // only when ConnectionStrings:ServiceBus is configured, to avoid startup failures.

        return services;
    }
}
