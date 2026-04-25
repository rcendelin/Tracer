# Contributing — Adding a New Enrichment Provider

This guide explains how to add a new data source to Tracer. The process is mechanical and follows an established pattern.

## Overview

Every provider lives in `src/Tracer.Infrastructure/Providers/<ProviderName>/` and implements `IEnrichmentProvider`. The waterfall orchestrator picks it up automatically via DI — no changes to orchestrator code needed.

## Step 1: Create the HTTP client

```csharp
// src/Tracer.Infrastructure/Providers/MyProvider/IMyProviderClient.cs
namespace Tracer.Infrastructure.Providers.MyProvider;

internal interface IMyProviderClient
{
    Task<MyProviderResult?> SearchByNameAsync(string name, CancellationToken ct);
    Task<MyProviderResult?> GetByIdAsync(string id, CancellationToken ct);
}
```

```csharp
// src/Tracer.Infrastructure/Providers/MyProvider/MyProviderClient.cs
internal sealed class MyProviderClient(HttpClient http) : IMyProviderClient
{
    public async Task<MyProviderResult?> SearchByNameAsync(string name, CancellationToken ct)
    {
        // Use http to call the external API
        // Truncate name to safe max length before sending (prevents overly large requests)
        var safeName = name.Length > 200 ? name[..200] : name;

        var response = await http.GetAsync($"search?name={Uri.EscapeDataString(safeName)}", ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content
            .ReadFromJsonAsync<MyProviderResult>(ct)
            .ConfigureAwait(false);
    }

    public async Task<MyProviderResult?> GetByIdAsync(string id, CancellationToken ct) { /* ... */ }
}
```

**Rules:**
- Class is `internal sealed` (never exposed outside Infrastructure)
- Constructor parameter is `HttpClient http` (typed client pattern)
- Never use `ex.Message` in error returns — return `null` or use generic error strings
- `OperationCanceledException` for timeout: use `catch (OperationCanceledException) when (!ct.IsCancellationRequested)` to distinguish timeout from caller cancellation

## Step 2: Create the provider

```csharp
// src/Tracer.Infrastructure/Providers/MyProvider/MyProviderProvider.cs
using Tracer.Application.Services;
using Tracer.Domain.Enums;
using Tracer.Domain.ValueObjects;

namespace Tracer.Infrastructure.Providers.MyProvider;

internal sealed class MyProviderProvider(IMyProviderClient client) : IEnrichmentProvider
{
    public string ProviderId => "my-provider";

    // Lower = higher priority. Registry APIs: 10-20. Global: 30. Geo: 50. Scraping: 150-200. AI: 250.
    public int Priority => 20;

    // Source quality for GoldenRecordMerger confidence weighting (0.0–1.0)
    public double SourceQuality => 0.85;

    public bool CanHandle(TraceContext context)
    {
        // Example: only handle companies from a specific country
        return context.Request.Country == "XY";
    }

    public async Task<ProviderResult> EnrichAsync(TraceContext context, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await client.SearchByNameAsync(
                context.Request.CompanyName ?? string.Empty, ct)
                .ConfigureAwait(false);

            if (result is null)
                return ProviderResult.NotFound(sw.Elapsed);

            var fields = new Dictionary<FieldName, object?>
            {
                [FieldName.LegalName] = result.LegalName,
                [FieldName.RegistrationId] = result.RegistrationId,
                [FieldName.RegisteredAddress] = result.Address is not null
                    ? new Address
                    {
                        Street = result.Address.Street,
                        City = result.Address.City,
                        PostalCode = result.Address.PostalCode,
                        Country = "XY",
                    }
                    : null,
            };

            return ProviderResult.Success(fields, sw.Elapsed);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Per-provider timeout (not caller cancellation)
            return ProviderResult.Timeout(sw.Elapsed);
        }
        catch (HttpRequestException)
        {
            return ProviderResult.Error("Provider unavailable", sw.Elapsed);
        }
    }
}
```

**Priority tiers:**

| Priority | Tier | Use for |
|----------|------|---------|
| 10–20 | Registry API | Official business registries (ARES, CH, ABN, EDGAR) |
| 30 | Global | GLEIF, Dun & Bradstreet |
| 50 | Geo | Google Maps, Azure Maps |
| 150–200 | Scraping | Web scraper, registry page scrapers |
| 250 | AI | Azure OpenAI extractor |

## Step 3: Register in DI

In `src/Tracer.Infrastructure/InfrastructureServiceRegistration.cs`:

```csharp
// HTTP client — Polly handles all timeouts; HttpClient.Timeout = InfiniteTimeSpan
services.AddHttpClient<IMyProviderClient, MyProviderClient>((sp, client) =>
{
    client.BaseAddress = new Uri("https://api.myprovider.com/v1/");
    client.Timeout = Timeout.InfiniteTimeSpan; // Polly controls timeouts

    // If API key required:
    var cfg = sp.GetRequiredService<IConfiguration>();
    var apiKey = cfg["Providers:MyProvider:ApiKey"] ?? string.Empty;
    if (!string.IsNullOrWhiteSpace(apiKey))
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
})
.AddStandardResilienceHandler(options =>
{
    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
    options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
    options.Retry.MaxRetryAttempts = 3;
});

// Provider (Transient — typed HttpClient factory manages handler lifetime)
services.AddTransient<IEnrichmentProvider, MyProviderProvider>();
```

**Important:** Providers must be registered as `Transient`. Registering as Singleton causes a captive dependency with the HttpClient handler.

## Step 4: Add configuration (if API key needed)

In `appsettings.Development.json` (for local dev):
```json
{
  "Providers": {
    "MyProvider": {
      "ApiKey": "dev-api-key-here"
    }
  }
}
```

In Azure Key Vault (production), create secret:
```
Providers--MyProvider--ApiKey = <production-key>
```

Add to `CLAUDE.md` environment variables section and `deploy/DEPLOYMENT.md`.

## Step 5: Write integration tests

```csharp
// tests/Tracer.Infrastructure.Tests/Providers/MyProvider/MyProviderClientTests.cs
public sealed class MyProviderClientTests
{
    private static MyProviderClient CreateSut(HttpMessageHandler handler) =>
        new(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.myprovider.com/v1/"),
        });

    [Fact]
    public async Task SearchByNameAsync_ValidName_ReturnsResult()
    {
        // Use FakeHttpMessageHandler for providers with absolute URLs
        // Use WireMock.Net for providers using BaseAddress-relative URLs
        var handler = new FakeHttpMessageHandler(request =>
        {
            // Return a recorded API response
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"legalName":"Acme Ltd","registrationId":"12345"}""",
                    Encoding.UTF8, "application/json"),
            };
        });

        var client = CreateSut(handler);
        var result = await client.SearchByNameAsync("Acme Ltd", CancellationToken.None);

        result.Should().NotBeNull();
        result!.LegalName.Should().Be("Acme Ltd");
    }
}
```

**Test gotchas:**
- Use `FakeHttpMessageHandler` (not WireMock) for providers that construct absolute URLs (e.g., SEC EDGAR style)
- Use `WireMock.Net` for providers that rely on `HttpClient.BaseAddress`
- `NullLogger<T>.Instance` instead of `Substitute.For<ILogger<T>>()` for `internal sealed` classes

## Step 6: Verify

```bash
dotnet build
dotnet test tests/Tracer.Infrastructure.Tests

# Manual: submit a trace for the new country/scenario
curl -X POST https://localhost:7100/api/trace \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: dev" \
  -d '{"companyName":"Acme Ltd","country":"XY","depth":"Quick"}'
```

## Checklist

- [ ] `IMyProviderClient` interface (internal)
- [ ] `MyProviderClient` class (internal sealed, HttpClient injection)
- [ ] `MyProviderProvider` class (internal sealed, implements IEnrichmentProvider)
- [ ] DI registration in `InfrastructureServiceRegistration.cs`
- [ ] Configuration section in `appsettings.Development.json`
- [ ] Key Vault secret documented in `deploy/DEPLOYMENT.md`
- [ ] Integration tests with recorded API responses
- [ ] `CanHandle()` only returns true for relevant countries/inputs
- [ ] `ProviderId` is lowercase, hyphenated (e.g., `"my-provider"`)
- [ ] Provider registered as `Transient` (not Singleton or Scoped)
