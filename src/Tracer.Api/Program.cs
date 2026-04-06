using System.Globalization;
using FluentValidation;
using Serilog;
using Tracer.Application;
using Tracer.Api.Endpoints;
using Tracer.Api.Middleware;
using Tracer.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

// OpenAPI
builder.Services.AddOpenApi();

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

// Health checks
builder.Services.AddHealthChecks();

// ProblemDetails (RFC 7807)
builder.Services.AddProblemDetails();

var app = builder.Build();

// Middleware pipeline
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
app.UseApiKeyAuth();

// Endpoints
app.MapHealthChecks("/health");
app.MapTraceEndpoints();
app.MapProfileEndpoints();
app.MapStatsEndpoints();
app.MapHub<Tracer.Api.Hubs.TraceHub>("/hubs/trace");

app.Run();

// Make Program accessible to WebApplicationFactory in integration tests via InternalsVisibleTo
internal partial class Program;
