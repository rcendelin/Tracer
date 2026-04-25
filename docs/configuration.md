# Tracer — configuration reference

All secrets in production come from **Azure Key Vault** via App Service
configuration references (`@Microsoft.KeyVault(SecretUri=…)`). Locally use
.NET user-secrets or `appsettings.Development.json` (excluded from git for
secrets).

> **Key Vault secret name rule.** Azure Key Vault disallows `:` in secret
> names. Use `--` instead — App Service translates `--` → `:` automatically.
> So `ConnectionStrings--TracerDb` in Key Vault → `ConnectionStrings:TracerDb`
> in `IConfiguration`.

## 1. Connection strings

| Key | Required | Notes |
|---|---|---|
| `ConnectionStrings:TracerDb` | always | Azure SQL serverless |
| `ConnectionStrings:ServiceBus` | Phase 2+ | Azure Service Bus namespace |
| `ConnectionStrings:Redis` | when `Cache:Provider = Redis` | Sourced from Key Vault `ConnectionStrings--Redis` |

## 2. Authentication & API keys

| Key | Default | Notes |
|---|---|---|
| `Auth:ApiKeys:0` *(flat)* | none | First key. Backwards-compatible string form. |
| `Auth:ApiKeys:0:Key` *(structured)* | none | First key. |
| `Auth:ApiKeys:0:Label` | none | Human label (e.g. `"fieldforce-prod"`). |
| `Auth:ApiKeys:0:ExpiresAt` | none | ISO 8601 datetime; rejected at startup if past. |

`ApiKeyOptionsValidator` rejects keys < 16 chars, duplicates, and already-expired
keys at boot — fail fast over silent acceptance. `ApiKeyAuthMiddleware` re-checks
`IsActive(now)` on every request via `TimeProvider`, allowing rotation without
redeploys.

## 3. Provider configuration

Every provider is registered as Transient with a typed `HttpClient`. Missing
keys produce no-ops or registry-only behaviour where possible.

| Section | Required for | Notes |
|---|---|---|
| `Providers:CompaniesHouse:ApiKey` | UK companies | 600 req/5 min global rate limit |
| `Providers:GoogleMaps:ApiKey` | geo enrichment | $200 / month free tier |
| `Providers:AzureMaps:SubscriptionKey` | bulk geocoding | 5K req/day free |
| `Providers:AbnLookup:Guid` | Australian companies | optional |
| `Providers:AzureOpenAI:Endpoint` | LLM extraction & disambiguation | falls back to DefaultAzureCredential when key absent |
| `Providers:AzureOpenAI:ApiKey` | LLM extraction & disambiguation | optional with managed identity |
| `Providers:AzureOpenAI:DisambiguatorDeploymentName` | LLM disambiguator | optional override |
| `Providers:AzureOpenAI:DisambiguatorMaxTokens` | LLM disambiguator | optional override |

## 4. Re-validation engine

| Key | Default | Notes |
|---|---|---|
| `Revalidation:Enabled` | `true` | Master toggle |
| `Revalidation:IntervalMinutes` | `60` | Outer-loop tick |
| `Revalidation:MaxProfilesPerRun` | `100` | Daily budget per tick |
| `Revalidation:OffPeakWindow:Enabled` | `false` | If true, auto sweep only inside `[StartHourUtc, EndHourUtc)` |
| `Revalidation:OffPeakWindow:StartHourUtc` | `22` | Wrap-around supported (22→6) |
| `Revalidation:OffPeakWindow:EndHourUtc` | `6` | |
| `Revalidation:Deep:Threshold` | `3` | Min number of expired fields to escalate to deep waterfall |
| `Revalidation:FieldTtl:<FieldName>` | `null` | Override Domain default TTL. Value = `"d.HH:mm:ss"`. Unknown keys → `OptionsValidationException`. |

## 5. Cache (B-79)

| Key | Default | Notes |
|---|---|---|
| `Cache:Provider` | `InMemory` | `InMemory` or `Redis` |
| `Cache:ProfileTtl` | `00:05:00` | Cache TTL for profile reads |
| `Cache:RedisInstanceName` | `"tracer:"` | Key prefix for Redis namespacing |
| `Cache:Warming:Enabled` | `false` | Singleton `BackgroundService` opt-in |
| `Cache:Warming:MaxProfiles` | `1000` | Range `[1, 10_000]` |
| `Cache:Warming:DelayOnStartup` | `00:00:30` | Wait before first warm |

## 6. Archival (B-83)

| Key | Default | Notes |
|---|---|---|
| `Archival:Enabled` | `false` | Master toggle (dev = off) |
| `Archival:IntervalHours` | `24` | Daily tick |
| `Archival:MinAgeDays` | `365` | `LastEnrichedAt < now - MinAgeDays` |
| `Archival:MaxTraceCount` | `1` | Inclusive ceiling |
| `Archival:BatchSize` | `1000` | One SQL UPDATE per batch |

## 7. GDPR (B-69 / B-70)

| Key | Default | Notes |
|---|---|---|
| `Gdpr:PersonalDataRetentionDays` | `1095` | ≈36 months — `PersonalDataRetentionService` erases beyond this |
| `Gdpr:AuditPersonalDataAccess` | `true` | `IPersonalDataAccessAudit` toggle (production must keep on) |

## 8. OpenAPI (B-82)

| Key | Default | Notes |
|---|---|---|
| `OpenApi:Title` | `"Tracer API"` | |
| `OpenApi:Version` | `"v1"` | |
| `OpenApi:Description` / `ContactName` / `ContactEmail` / `LicenseName` / `LicenseUrl` / `TermsOfService` | none | Populate the `info` block |
| `OpenApi:ServerUrls` | `[]` | Absolute URIs in `servers[]` array |
| `OpenApi:EnableUi` | `false` | Mount Scalar UI at `/scalar/{documentName}`. Development overrides → `true`. |

## 9. Security headers (B-87)

`Security:Headers` controls `SecurityHeadersMiddleware`. Defaults are
production-ready; override per environment.

| Key | Default | Notes |
|---|---|---|
| `Security:Headers:Enabled` | `true` | Master toggle |
| `Security:Headers:HstsMaxAgeSeconds` | `63_072_000` | 2 years; `UseHsts()` reads this in production |
| `Security:Headers:HstsIncludeSubDomains` | `true` | |
| `Security:Headers:HstsPreload` | `false` | Opt-in |
| `Security:Headers:ContentSecurityPolicy` | `"default-src 'none'; frame-ancestors 'none'; base-uri 'none'"` | API never returns HTML |

## 10. Rate limiting (B-80 / B-81)

Two named policies in `Program.cs`:

| Policy | Permit | Window | Used by |
|---|---|---|---|
| `batch` | 5 | 1 min / IP | `POST /api/trace/batch` |
| `export` | 10 | 1 min / IP | `GET /api/profiles/export`, `GET /api/changes/export` |

`QueueLimit = 0` on both — immediate 429, no queueing. Polly per-provider
resilience defaults are coded in `ProviderResilienceDefaults.BuildDefaults()`
and overridable per environment via `Resilience:Providers:<id>:*`.

## 11. CORS (production)

`Cors:AllowedOrigins` — array of origins served by the SPA Static Web App.
Production fails at startup if empty.

## 12. Observability

| Key | Default | Notes |
|---|---|---|
| `APPLICATIONINSIGHTS_CONNECTION_STRING` (env var) | none | When unset, `UseAzureMonitor()` is skipped; OpenTelemetry tracing/metrics still register |
| `AzureMonitor:ConnectionString` | none | Alternative location |
| `Serilog:*` | see `appsettings.json` | Serilog standard config |

---

For a side-by-side view of "what does this section mean for me as an
operator", see [operations/handbook.md](./operations/handbook.md). For
why the configuration shape looks the way it does, see
[adr/](./adr/).
