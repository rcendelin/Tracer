using System.Globalization;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

// OpenAPI
builder.Services.AddOpenApi();

// TODO B-08+: Register MediatR, FluentValidation, DbContext, Repositories, Providers
// builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(SubmitTraceCommand).Assembly));
// builder.Services.AddValidatorsFromAssembly(typeof(SubmitTraceCommand).Assembly);

// Health checks
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseHttpsRedirection();
// TODO B-23+: Add security headers middleware (CSP, HSTS, X-Content-Type-Options, X-Frame-Options)
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health");

// TODO B-22+: Map feature endpoints
// app.MapTraceEndpoints();
// app.MapProfileEndpoints();
// app.MapChangeEndpoints();
// app.MapValidationEndpoints();

app.Run();

// Make Program accessible to WebApplicationFactory in integration tests via InternalsVisibleTo
internal partial class Program { }
