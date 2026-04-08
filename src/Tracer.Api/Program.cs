using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry.Trace;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog;
using Tracer.Application;
using Tracer.Api.Endpoints;
using Tracer.Api.Middleware;
using Tracer.Api.Telemetry;
using Tracer.Application.Services;
using Tracer.Infrastructure;

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

// OpenAPI
builder.Services.AddOpenApi();

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

// Infrastructure layer: DbContext, Repositories, HTTP clients, Providers
var connectionString = builder.Configuration.GetConnectionString("TracerDb")
    ?? throw new InvalidOperationException("ConnectionStrings:TracerDb is not configured.");
builder.Services.AddInfrastructure(connectionString);

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
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("API is running"))
    .AddInfrastructureHealthChecks();

// ProblemDetails (RFC 7807)
builder.Services.AddProblemDetails();

// Rate limiting — batch endpoint: 5 requests per minute per client IP
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
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();

// Middleware pipeline
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseHttpsRedirection();
app.UseCors();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
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
app.MapHub<Tracer.Api.Hubs.TraceHub>("/hubs/trace");

app.Run();

// Make Program accessible to WebApplicationFactory in integration tests via InternalsVisibleTo
internal partial class Program;
