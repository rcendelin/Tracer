using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry.Trace;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Serilog;
using Tracer.Application;
using Tracer.Api.Endpoints;
using Tracer.Api.Middleware;
using Tracer.Api.OpenApi;
using Tracer.Api.Telemetry;
using Tracer.Application.Services;
using Tracer.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Serilog — enriched with W3C TraceId/SpanId from the active OpenTelemetry Activity.
// ActivityTraceEnricher adds TraceId and SpanId properties to every log event,
// enabling correlation between Serilog logs and App Insights distributed traces.
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.With<ActivityTraceEnricher>()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

// OpenTelemetry + Azure Monitor (conditional)
// UseAzureMonitor exports distributed traces and metrics to Application Insights.
// Connection string from APPLICATIONINSIGHTS_CONNECTION_STRING env var or AzureMonitor:ConnectionString config.
// If neither is set (local dev, CI), we still register the metric pipeline — instruments are readable
// via dotnet-counters or any local MeterListener. Tests pass without App Insights configured.
var appInsightsConnectionString =
    Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")
    ?? builder.Configuration["AzureMonitor:ConnectionString"];

// WithTracing is always registered so Activity.Current is populated for every HTTP request.
// This ensures ActivityTraceEnricher produces TraceId/SpanId in Serilog logs even in local dev,
// where UseAzureMonitor (below) is skipped. ASP.NET Core and HttpClient instrumentation
// packages are included transitively by Azure.Monitor.OpenTelemetry.AspNetCore.
var otelBuilder = builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter(ITracerMetrics.MeterName))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation());

if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
    otelBuilder.UseAzureMonitor(options => options.ConnectionString = appInsightsConnectionString);

// OpenAPI (B-82)
// TracerOpenApiOptions drives Info, Servers, Tag descriptions and the `X-Api-Key` security
// scheme via TracerOpenApiDocumentTransformer. ApiKeySecurityRequirementTransformer attaches
// the requirement to every operation except the /health + /openapi allowlist that matches
// ApiKeyAuthMiddleware. Scalar UI mount is conditional (see below) on Development or an
// explicit OpenApi:EnableUi opt-in.
builder.Services.AddOptions<TracerOpenApiOptions>()
    .Bind(builder.Configuration.GetSection(TracerOpenApiOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(
        o => o.ServerUrls.All(url => Uri.TryCreate(url, UriKind.Absolute, out _)),
        "OpenApi:ServerUrls must all be absolute URIs.")
    .ValidateOnStart();

builder.Services.AddSingleton<TracerOpenApiDocumentTransformer>();
builder.Services.AddSingleton<ApiKeySecurityRequirementTransformer>();

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<TracerOpenApiDocumentTransformer>();
    options.AddOperationTransformer<ApiKeySecurityRequirementTransformer>();
});

// Global JSON options: serialize enums as strings so the frontend receives
// "LegalName" instead of 0, "Critical" instead of 1, etc.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// SignalR
builder.Services.AddSignalR();
builder.Services.AddScoped<Tracer.Application.Services.ITraceNotificationService, Tracer.Api.Hubs.TraceNotificationService>();

// Application layer: MediatR, FluentValidation, Orchestrator, Scorer, Merger, Resolver, CKB persistence
builder.Services.AddApplication();

// GDPR classification and retention policy (B-69) — bound from the "Gdpr" section.
// ValidateOnStart ensures a misconfigured retention window fails at boot, not
// at first resolve, and that the container never hands out an invalid policy.
builder.Services.AddOptions<Tracer.Application.Services.GdprOptions>()
    .Bind(builder.Configuration.GetSection(Tracer.Application.Services.GdprOptions.SectionName))
    .Validate(
        o => o.PersonalDataRetentionDays > 0,
        "Gdpr:PersonalDataRetentionDays must be a positive number of days.")
    .ValidateOnStart();

// Field TTL policy (B-68) — bound from "Revalidation:FieldTtl" section.
// The section shape is a flat FieldName -> TimeSpan map, so we can't use the
// default .Bind() helper; we read children and project into FieldTtlOptions.Overrides.
// Unparseable values throw immediately — silent drops would produce a half-applied
// policy that's hard to diagnose. Keys / values are validated again by ValidateOnStart
// and once more defensively in FieldTtlPolicy's constructor.
builder.Services.AddOptions<Tracer.Application.Services.FieldTtlOptions>()
    .Configure<IConfiguration>((options, configuration) =>
    {
        var section = configuration.GetSection(Tracer.Application.Services.FieldTtlOptions.SectionName);
        foreach (var entry in section.GetChildren())
        {
            if (string.IsNullOrWhiteSpace(entry.Value))
                continue;

            if (!TimeSpan.TryParse(entry.Value, System.Globalization.CultureInfo.InvariantCulture, out var ttl))
                throw new InvalidOperationException(
                    $"Revalidation:FieldTtl['{entry.Key}'] is not a valid TimeSpan (got '{entry.Value}').");

            options.Overrides[entry.Key] = ttl;
        }
    })
    .Validate(
        o => o.Overrides.Values.All(v => v > TimeSpan.Zero),
        "Revalidation:FieldTtl values must be strictly positive TimeSpans.")
    .Validate(
        o => o.Overrides.Keys.All(k => Enum.TryParse<Tracer.Domain.Enums.FieldName>(k, ignoreCase: true, out _)),
        "Revalidation:FieldTtl keys must match Tracer.Domain.Enums.FieldName members.")
    .ValidateOnStart();

// Infrastructure layer: DbContext, Repositories, HTTP clients, Providers
var connectionString = builder.Configuration.GetConnectionString("TracerDb")
    ?? throw new InvalidOperationException("ConnectionStrings:TracerDb is not configured.");
builder.Services.AddInfrastructure(connectionString, builder.Configuration);

// Cache warming (B-79) — opt-in startup pre-population of the distributed cache.
// Disabled by default; enable in production via Cache:Warming:Enabled = true once
// Redis is provisioned and the App Service has a stable connection.
if (builder.Configuration.GetValue<bool>("Cache:Warming:Enabled"))
{
    builder.Services.AddHostedService<Tracer.Infrastructure.BackgroundJobs.CacheWarmingService>();
}

// Re-validation scheduler (B-65) — hourly BackgroundService that walks CKB for expired fields.
// Options are always bound so the API layer (POST /revalidate) can inspect them; the
// BackgroundService itself is only registered when Revalidation:Enabled = true.
builder.Services.AddOptions<Tracer.Application.Services.RevalidationOptions>()
    .Bind(builder.Configuration.GetSection(Tracer.Application.Services.RevalidationOptions.SectionName));

// Deep re-validation (B-67) — threshold that triggers full waterfall re-enrichment.
// ValidateOnStart fails fast if an operator sets a non-positive threshold in config.
builder.Services.AddOptions<Tracer.Application.Services.DeepRevalidationOptions>()
    .Bind(builder.Configuration.GetSection(Tracer.Application.Services.DeepRevalidationOptions.SectionName))
    .Validate(
        o => o.Threshold >= 1,
        "Revalidation:Deep:Threshold must be at least 1.")
    .ValidateOnStart();

// Lightweight re-validation (B-66) — composite runner dispatches lightweight ↔ deep
// using this Threshold. Threshold ≥ 0 is required; 0 means "lightweight is only used
// when there are no expired fields", which is effectively no-op. The composite will
// then go deep for any expired field. Set Enabled=false to disable lightweight entirely
// (composite always goes deep — pre-B-66 behaviour).
builder.Services.AddOptions<Tracer.Application.Services.LightweightRevalidationOptions>()
    .Bind(builder.Configuration.GetSection(Tracer.Application.Services.LightweightRevalidationOptions.SectionName))
    .Validate(
        o => o.Threshold >= 0,
        "Revalidation:Lightweight:Threshold must be ≥ 0.")
    .ValidateOnStart();

var revalidationEnabled = builder.Configuration
    .GetSection(Tracer.Application.Services.RevalidationOptions.SectionName)
    .GetValue<bool?>("Enabled") ?? true;
if (revalidationEnabled)
{
    builder.Services.AddHostedService<Tracer.Infrastructure.BackgroundJobs.RevalidationScheduler>();
}

// CKB archival (B-83) — daily BackgroundService that archives one-shot profiles
// (TraceCount ≤ 1, LastEnrichedAt > 12 months ago). Options are bound + validated;
// the service is only registered when Archival:Enabled = true.
// Un-archive on incoming trace is wired into CkbPersistenceService and runs regardless.
builder.Services.AddOptions<Tracer.Application.Services.ArchivalOptions>()
    .Bind(builder.Configuration.GetSection(Tracer.Application.Services.ArchivalOptions.SectionName))
    .Validate(o => o.IntervalHours >= 1, "Archival:IntervalHours must be >= 1.")
    .Validate(o => o.MinAgeDays >= 1, "Archival:MinAgeDays must be >= 1.")
    .Validate(o => o.MaxTraceCount >= 0, "Archival:MaxTraceCount must be >= 0.")
    .Validate(o => o.BatchSize is >= 1 and <= 10_000, "Archival:BatchSize must be between 1 and 10000.")
    .ValidateOnStart();

var archivalEnabled = builder.Configuration
    .GetSection(Tracer.Application.Services.ArchivalOptions.SectionName)
    .GetValue<bool?>("Enabled") ?? true;
if (archivalEnabled)
{
    builder.Services.AddHostedService<Tracer.Infrastructure.BackgroundJobs.ArchivalService>();
}

// Service Bus consumer (optional — only when connection string is configured)
var sbConnectionString = builder.Configuration.GetConnectionString("ServiceBus");
if (!string.IsNullOrWhiteSpace(sbConnectionString))
{
    builder.Services.AddSingleton(_ => new Azure.Messaging.ServiceBus.ServiceBusClient(sbConnectionString));
    builder.Services.Configure<Tracer.Infrastructure.Messaging.ServiceBusOptions>(
        builder.Configuration.GetSection("ServiceBus"));
    builder.Services.AddHostedService<Tracer.Infrastructure.Messaging.ServiceBusConsumer>();
}

// CORS for Tracer.Web SPA
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
if ((corsOrigins is null || corsOrigins.Length == 0) && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException("Cors:AllowedOrigins must be configured in production.");

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(corsOrigins ?? ["http://localhost:5173"])
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Forwarded headers — required for correct RemoteIpAddress behind Azure App Service / Front Door.
// KnownIPNetworks restricts which upstream proxies are trusted to set X-Forwarded-For.
// Without this, rate limiting and logging would use the proxy IP, not the client IP.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Azure infrastructure uses RFC 1918 space; restrict to private ranges only
    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("10.0.0.0/8"));
    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("172.16.0.0/12"));
    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("192.168.0.0/16"));
});

// Health checks — SQL connectivity probe (catches misconfigured connection strings early)
// plus Redis probe (B-79) when Cache:Provider = Redis.
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("API is running"))
    .AddInfrastructureHealthChecks(builder.Configuration);

// ProblemDetails (RFC 7807)
builder.Services.AddProblemDetails();

// Security: API key options (B-87). Supports both flat string form and
// rotation-friendly { Key, Label, ExpiresAt } object form. Validation runs
// at startup so misconfigured keys (too short, duplicate, already-expired)
// fail fast rather than letting anonymous traffic through.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<
    Microsoft.Extensions.Options.IValidateOptions<Tracer.Api.Middleware.ApiKeyOptions>,
    Tracer.Api.Middleware.ApiKeyOptionsValidator>();
builder.Services.AddOptions<Tracer.Api.Middleware.ApiKeyOptions>()
    .Configure<IConfiguration>((options, configuration) =>
    {
        var bound = Tracer.Api.Middleware.ApiKeyOptionsBinder.Bind(configuration);
        options.ApiKeys = bound.ApiKeys;
    })
    .ValidateOnStart();

// Security: response header options (B-87). Default values are production-ready;
// override via the "Security:Headers" config section if needed.
builder.Services.AddOptions<Tracer.Api.Middleware.SecurityHeadersOptions>()
    .Bind(builder.Configuration.GetSection(Tracer.Api.Middleware.SecurityHeadersOptions.SectionName));

// HSTS — emitted by the built-in UseHsts middleware in production only.
// The values here feed `Strict-Transport-Security: max-age=...; includeSubDomains[; preload]`.
var securityHeadersSection = builder.Configuration.GetSection(Tracer.Api.Middleware.SecurityHeadersOptions.SectionName);
builder.Services.Configure<Microsoft.AspNetCore.HttpsPolicy.HstsOptions>(hsts =>
{
    hsts.MaxAge = TimeSpan.FromSeconds(
        securityHeadersSection.GetValue<int?>(nameof(Tracer.Api.Middleware.SecurityHeadersOptions.HstsMaxAgeSeconds))
        ?? 63_072_000);
    hsts.IncludeSubDomains = securityHeadersSection
        .GetValue<bool?>(nameof(Tracer.Api.Middleware.SecurityHeadersOptions.HstsIncludeSubDomains))
        ?? true;
    hsts.Preload = securityHeadersSection
        .GetValue<bool?>(nameof(Tracer.Api.Middleware.SecurityHeadersOptions.HstsPreload))
        ?? false;
});

// Rate limiting — batch endpoint: 5 requests per minute per client IP,
// export endpoints: 10 requests per minute per client IP (B-81).
// Exports are IO-heavy (up to 10k rows written to the response) — a per-IP
// fixed-window limiter keeps a single client from monopolising worker threads.
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("batch", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
            }));

    options.AddPolicy("export", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
            }));

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();

// Middleware pipeline
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();

// HSTS is production-only — adds Strict-Transport-Security with max-age,
// includeSubDomains, and optional preload per HstsOptions. In development we
// skip it so local HTTPS dev certs do not become permanently pinned in the
// browser (B-87).
if (!app.Environment.IsDevelopment())
    app.UseHsts();

app.UseHttpsRedirection();

// Additional response headers (CSP, Referrer-Policy, Permissions-Policy,
// X-Content-Type-Options, X-Frame-Options, COOP, CORP). Runs early so every
// response — including errors from later middleware — carries the headers.
app.UseSecurityHeaders();

app.UseCors();
app.UseSerilogRequestLogging();

// OpenAPI spec + Scalar UI (B-82). The spec endpoint is always mapped so integrators
// can consume the JSON contract (ApiKeyAuthMiddleware treats /openapi as anonymous).
// The Scalar UI is extra and mounted only in Development or when OpenApi:EnableUi is true.
var openApiOptions = app.Services.GetRequiredService<IOptions<TracerOpenApiOptions>>().Value;
app.MapOpenApi();
if (app.Environment.IsDevelopment() || openApiOptions.EnableUi)
{
    app.MapScalarApiReference(options =>
    {
        options.WithTitle(openApiOptions.Title)
            .WithOpenApiRoutePattern("/openapi/{documentName}.json");
    });
}

// API key authentication — validates X-Api-Key header. Skips /health and /openapi.
// When no keys are configured (Auth:ApiKeys empty), all requests pass (dev mode).
// Must run before UseRateLimiter so unauthenticated requests don't consume rate limit slots.
app.UseApiKeyAuth();
app.UseRateLimiter();

// Endpoints
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
            }),
        };
        await context.Response.WriteAsJsonAsync(result).ConfigureAwait(false);
    },
});
app.MapTraceEndpoints();
app.MapProfileEndpoints();
app.MapChangesEndpoints();
app.MapStatsEndpoints();
app.MapValidationEndpoints();
app.MapAnalyticsEndpoints();
app.MapHub<Tracer.Api.Hubs.TraceHub>("/hubs/trace");

app.Run();

// Make Program accessible to WebApplicationFactory in integration tests via InternalsVisibleTo
internal partial class Program;
