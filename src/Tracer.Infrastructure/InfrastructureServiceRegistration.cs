using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
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
using Tracer.Infrastructure.Providers.AbnLookup;
using Tracer.Infrastructure.Providers.SecEdgar;
using Tracer.Infrastructure.Providers.WebScraper;
using Tracer.Infrastructure.Providers.Handelsregister;
using Tracer.Infrastructure.Providers.BrazilCnpj;
using Tracer.Infrastructure.Providers.StateSos;
using Tracer.Infrastructure.Providers.StateSos.Adapters;
using Tracer.Infrastructure.Providers.LatamRegistry;
using Tracer.Infrastructure.Providers.LatamRegistry.Adapters;
using Tracer.Infrastructure.Providers.LatamRegistry.Providers;
using Tracer.Infrastructure.Providers.AiExtractor;
using Tracer.Infrastructure.Telemetry;
using Tracer.Infrastructure.Caching;
using Azure;
using Azure.AI.OpenAI;

namespace Tracer.Infrastructure;

/// <summary>
/// Registers Infrastructure layer services in the DI container.
/// </summary>
public static class InfrastructureServiceRegistration
{
    /// <summary>
    /// Adds Infrastructure layer services: DbContext, repositories, distributed cache,
    /// HTTP-backed providers, and Service Bus messaging.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <param name="configuration">
    /// Application configuration. Used to bind cache options (B-79), HTTP-client provider
    /// keys, Service Bus, and Azure OpenAI settings. Required so a Redis migration
    /// (<c>Cache:Provider = Redis</c>) can be enabled by configuration without changing
    /// the call site.
    /// </param>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString, nameof(connectionString));
        ArgumentNullException.ThrowIfNull(configuration);

        // Observability metrics — Singleton because Meter is thread-safe and long-lived
        services.AddSingleton<ITracerMetrics, TracerMetrics>();

        services.AddDbContext<TracerDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(TracerDbContext).Assembly.FullName);
                sql.EnableRetryOnFailure(maxRetryCount: 3);
            }));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<TracerDbContext>());

        // Distributed cache (B-79) — driven by Cache:Provider config.
        // Default = InMemory (transparent fallback for dev/CI).
        // Redis = Azure Cache for Redis when ConnectionStrings:Redis is present.
        services.AddTracerDistributedCache(configuration);
        services.AddSingleton<IProfileCacheService, Caching.ProfileCacheService>();
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

        // ABN Lookup (Australia) — optional, requires GUID
        services.AddHttpClient<IAbnLookupClient, AbnLookupClient>((sp, client) =>
        {
            var abnConfig = sp.GetRequiredService<IConfiguration>();
            var guid = abnConfig["Providers:AbnLookup:Guid"] ?? string.Empty;

            client.BaseAddress = new Uri("https://abr.business.gov.au/json/");
            client.Timeout = Timeout.InfiniteTimeSpan;

            if (!string.IsNullOrWhiteSpace(guid))
                client.DefaultRequestHeaders.Add("X-Abn-Guid", guid);
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
        services.AddTransient<IEnrichmentProvider, AbnLookupProvider>();
        services.AddTransient<IEnrichmentProvider, SecEdgarProvider>();

        // SEC EDGAR — free API, no key, User-Agent required
        services.AddHttpClient<ISecEdgarClient, SecEdgarClient>(client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Tracer/1.0 (tracer@xtuning.cz)");
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        })
        .AddStandardResilienceHandler(options =>
        {
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
            options.Retry.MaxRetryAttempts = 2;
        });

        // Web scraper — fetches company websites and extracts structured data via AngleSharp
        // No retries: scraping is idempotent but retrying a slow or broken site wastes time.
        // Polly timeout (10s) prevents hanging on unresponsive servers.
        // AllowAutoRedirect = false: prevents SSRF bypass via 302 redirect to internal hosts
        // (SSRF IP check runs before the request; redirect would bypass it).
        services.AddHttpClient<IWebScraperClient, Providers.WebScraper.WebScraperClient>(client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan; // Polly controls timeouts
            // Identify ourselves politely; some servers block blank User-Agents
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Tracer/1.0 (tracer@xtuning.cz)");
            // Accept HTML only — we decline to process other content types
            client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml;q=0.9");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            // Disabled: a 302 to an internal host would bypass SSRF IP validation performed
            // before the initial request. Without redirects, each target URL is independently
            // validated.
            AllowAutoRedirect = false,
        })
        .AddStandardResilienceHandler(options =>
        {
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
            // TotalRequestTimeout (15s) caps everything: a retry would get at most ~5s before
            // being cancelled, so retrying a slow site has negligible cost in practice.
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);
            // Minimum allowed by Polly validator (must be ≥ 1); TotalRequestTimeout ensures
            // the retry almost never completes anyway.
            options.Retry.MaxRetryAttempts = 1;
        });

        // Provider registered after its HTTP client dependency (follows project convention)
        services.AddTransient<IEnrichmentProvider, Providers.WebScraper.WebScraperProvider>();

        // Handelsregister.de — German commercial register scraper (Tier 2, Priority 200)
        // No API key required; scraping with rate limit enforcement (60 req/hour per German law).
        // Cookie support via SocketsHttpHandler; no auto-redirect for SSRF protection.
        services.AddHttpClient<IHandelsregisterClient, HandelsregisterClient>(client =>
        {
            client.BaseAddress = new Uri("https://www.handelsregister.de/rp_web/");
            client.Timeout = Timeout.InfiniteTimeSpan; // Polly controls all timeouts
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Tracer/1.0 (tracer@xtuning.cz)");
            client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml;q=0.9");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            // Cookie support for session management across search + detail requests
            UseCookies = true,
            CookieContainer = new System.Net.CookieContainer(),
            // Disabled: a 302 to an internal host would bypass SSRF IP validation
            AllowAutoRedirect = false,
        })
        .AddStandardResilienceHandler(options =>
        {
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(12);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
            // Minimum allowed by Polly validator; TotalRequestTimeout caps total cost
            options.Retry.MaxRetryAttempts = 1;
        });

        services.AddTransient<IEnrichmentProvider, HandelsregisterProvider>();

        // BrasilAPI CNPJ — Brazilian Federal Revenue company data (Tier 2, Priority 200)
        // Free, no API key required. JSON REST API.
        services.AddHttpClient<IBrazilCnpjClient, BrazilCnpjClient>(client =>
        {
            client.BaseAddress = new Uri("https://brasilapi.com.br/api/");
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

        services.AddTransient<IEnrichmentProvider, BrazilCnpjProvider>();

        // US State Secretary of State registries (Tier 2, Priority 200)
        // Per-state adapters: CA, DE, NY. Singletons — stateless HTML parsers.
        services.AddSingleton<IStateSosAdapter, CaliforniaAdapter>();
        services.AddSingleton<IStateSosAdapter, DelawareAdapter>();
        services.AddSingleton<IStateSosAdapter, NewYorkAdapter>();

        // StateSos client — HTML scraping with rate limiting and SSRF guard.
        // No auto-redirect: prevents SSRF bypass via 302 to internal hosts.
        services.AddHttpClient<IStateSosClient, StateSosClient>(client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan; // Polly controls all timeouts
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Tracer/1.0 (tracer@xtuning.cz)");
            client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml;q=0.9");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            UseCookies = true,
            CookieContainer = new System.Net.CookieContainer(),
            AllowAutoRedirect = false,
        })
        .AddStandardResilienceHandler(options =>
        {
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(12);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
            options.Retry.MaxRetryAttempts = 1;
        });

        services.AddTransient<IEnrichmentProvider, StateSosProvider>();

        // LATAM registry providers — Argentina (AFIP), Chile (SII), Colombia (RUES),
        // Mexico (SAT). Shared HTTP client dispatches on CountryCode; adapters are
        // Singletons (stateless parsers). Tier 2, Priority 200.
        services.AddSingleton<ILatamRegistryAdapter, ArgentinaAfipAdapter>();
        services.AddSingleton<ILatamRegistryAdapter, ChileSiiAdapter>();
        services.AddSingleton<ILatamRegistryAdapter, ColombiaRuesAdapter>();
        services.AddSingleton<ILatamRegistryAdapter, MexicoSatAdapter>();

        services.AddHttpClient<ILatamRegistryClient, LatamRegistryClient>(client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan; // Polly controls all timeouts
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Tracer/1.0 (tracer@xtuning.cz)");
            client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml;q=0.9");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            UseCookies = true,
            CookieContainer = new System.Net.CookieContainer(),
            // Disabled: a 302 to an internal host would bypass SSRF IP validation.
            AllowAutoRedirect = false,
        })
        .AddStandardResilienceHandler(options =>
        {
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(12);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
            // Minimum allowed by Polly validator; TotalRequestTimeout caps total cost.
            options.Retry.MaxRetryAttempts = 1;
        });

        services.AddTransient<IEnrichmentProvider, ArgentinaAfipProvider>();
        services.AddTransient<IEnrichmentProvider, ChileSiiProvider>();
        services.AddTransient<IEnrichmentProvider, ColombiaRuesProvider>();
        services.AddTransient<IEnrichmentProvider, MexicoSatProvider>();

        // Azure OpenAI — AI Extractor client (optional: only registered if endpoint is configured)
        // AzureOpenAIClient is Singleton: thread-safe, manages its own HTTP pipeline.
        // AiExtractorClient is Singleton: stateless per-call; _chatClient is thread-safe.
        services.AddSingleton<IAiExtractorClient>(sp =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var endpoint = cfg["Providers:AzureOpenAI:Endpoint"];
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                // Optional provider — if not configured, return a no-op implementation.
                // AiExtractorProvider.CanHandle still returns true for Deep traces with a website;
                // NullAiExtractorClient short-circuits ExtractCompanyInfoAsync returning null,
                // causing EnrichAsync to return NotFound without enriching anything.
                return new NullAiExtractorClient();
            }

            var deploymentName = cfg["Providers:AzureOpenAI:DeploymentName"] ?? "gpt-4o-mini";
            var maxTokens = int.TryParse(cfg["Providers:AzureOpenAI:MaxTokens"], out var t) ? t : 2000;

            // Azure.AI.OpenAI 2.x uses AzureKeyCredential when a key is provided;
            // falls back to DefaultAzureCredential (Managed Identity) when key is absent.
            var apiKey = cfg["Providers:AzureOpenAI:ApiKey"];
            var azureClient = string.IsNullOrWhiteSpace(apiKey)
                ? new AzureOpenAIClient(new Uri(endpoint), new Azure.Identity.DefaultAzureCredential())
                : new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));

            var logger = sp.GetRequiredService<ILogger<AiExtractorClient>>();
            return new AiExtractorClient(azureClient, deploymentName, maxTokens, logger);
        });

        // AI Extractor provider — registered after its client dependency (follows project convention).
        // Transient: thin stateless wrapper; IAiExtractorClient and IWebScraperClient are Singleton/Transient.
        services.AddTransient<IEnrichmentProvider, Providers.AiExtractor.AiExtractorProvider>();

        // LLM Disambiguator (B-64) — Azure OpenAI-backed entity disambiguation for ambiguous
        // fuzzy candidates (0.70–0.85). Overrides the NullLlmDisambiguatorClient registered by
        // the Application layer when Providers:AzureOpenAI:Endpoint is configured.
        // Singleton: AzureOpenAIClient is thread-safe; LlmDisambiguatorClient is stateless per call.
        services.AddSingleton<Application.Services.ILlmDisambiguatorClient>(sp =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var endpoint = cfg["Providers:AzureOpenAI:Endpoint"];
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return new Application.Services.NullLlmDisambiguatorClient();
            }

            var deploymentName =
                cfg["Providers:AzureOpenAI:DisambiguatorDeploymentName"]
                ?? cfg["Providers:AzureOpenAI:DeploymentName"]
                ?? "gpt-4o-mini";
            var maxTokens = int.TryParse(cfg["Providers:AzureOpenAI:DisambiguatorMaxTokens"], out var t) ? t : 500;

            var apiKey = cfg["Providers:AzureOpenAI:ApiKey"];
            var azureClient = string.IsNullOrWhiteSpace(apiKey)
                ? new AzureOpenAIClient(new Uri(endpoint), new Azure.Identity.DefaultAzureCredential())
                : new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));

            var logger = sp.GetRequiredService<ILogger<Providers.LlmDisambiguator.LlmDisambiguatorClient>>();
            return new Providers.LlmDisambiguator.LlmDisambiguatorClient(
                azureClient, deploymentName, maxTokens, logger);
        });

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

    /// <summary>
    /// Registers Infrastructure-layer health checks: the database connectivity probe
    /// and, when <c>Cache:Provider = Redis</c>, a Redis round-trip probe (B-79).
    /// Kept here because <see cref="Persistence.TracerDbContext"/> is internal to Infrastructure.
    /// </summary>
    public static IHealthChecksBuilder AddInfrastructureHealthChecks(
        this IHealthChecksBuilder builder,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        builder.AddCheck<HealthChecks.DatabaseHealthCheck>("database");

        if (Caching.CacheOptions.ResolveProvider(configuration) == Caching.CacheProvider.Redis)
        {
            builder.AddCheck<HealthChecks.RedisHealthCheck>("redis");
        }

        return builder;
    }
}
