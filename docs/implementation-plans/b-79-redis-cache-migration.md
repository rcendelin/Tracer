# B-79 — Redis cache migration

Phase 4 — Scale + polish (≈4 h)

## Goal

Make the `IDistributedCache` backing store swappable between an in-process
`MemoryDistributedCache` (the B-40 default, still the safe fallback for dev
and CI) and Azure Cache for Redis, controlled by a single configuration
toggle. Add a cache-warming BackgroundService that pre-populates the hottest
profiles on startup, a Redis health check, and the Bicep IaC module plus
Key Vault wiring needed to provision a Basic C0 Redis instance in Azure.

## Non-goals

- Migrating the cache **key format** or **serialisation layout** — the
  existing `profile:{normalizedKey}` JSON byte payload stays unchanged so
  the switch is transparent to `ProfileCacheService` and to existing tests.
- Actually running `az` / `azd` commands to provision Redis — forbidden by
  the "Azure — Přísný zákaz destruktivních operací" rule. Bicep IaC and
  Key Vault secret names are committed so a human operator can deploy with
  a single explicit action.
- Adding a Testcontainers-backed integration test — Docker daemon is not
  available in the current sandbox. An outline is documented as a
  follow-up; everything else is covered by unit tests with fakes.
- Re-warming the cache after failover or on a schedule — warming runs once
  on startup only. Periodic warming is a Phase-4 concern tracked outside
  this block (B-92 or later).

## Design overview

```
Program.cs
  └── builder.Services.AddInfrastructure(connectionString)          // unchanged surface
        └── services.AddTracerDistributedCache(configuration)      // NEW (called inside AddInfrastructure)
              ├── Cache:Provider = InMemory → services.AddDistributedMemoryCache()
              └── Cache:Provider = Redis    → services.AddStackExchangeRedisCache(opts)
        └── services.AddSingleton<IProfileCacheService, ProfileCacheService>()  // unchanged

  └── builder.Services.AddHealthChecks()
        .AddInfrastructureHealthChecks(builder.Configuration)       // NEW overload — adds "redis" check when Provider=Redis

  └── if Cache:Warming:Enabled → builder.Services.AddHostedService<CacheWarmingService>()
```

- `AddInfrastructure` still takes a single connection string so no test or
  consumer surface changes; it reads `IConfiguration` from the DI container
  via `services.AddOptions<CacheOptions>()` + `BindConfiguration` and
  exposes a new internal extension `AddTracerDistributedCache` that owns
  the branching logic. `AddInfrastructure` requires a non-null
  `IConfiguration` via a second parameter only when Redis is enabled — kept
  backward-compatible by adding an optional `IConfiguration?` overload.
  *Decision:* require `IConfiguration` always — the change is local to
  `Program.cs` and makes the contract explicit. Tests that don't care
  about caching pass `new ConfigurationBuilder().Build()`.
- `IProfileCacheService` and `ProfileCacheService` are **unchanged**. The
  migration is purely a swap of the backing `IDistributedCache`
  implementation, plus the ambient `InstanceName` prefix baked into
  StackExchangeRedisCache (`tracer:`) so multiple environments can share
  one cache without key collisions.
- `RedisHealthCheck` writes + reads a `tracer:health:{timestamp}` probe
  key with a 5 second TTL. Errors are mapped to `HealthCheckResult.Degraded`
  (the cache is an optimisation, its absence must not take the API down).
- `CacheWarmingService` is a Singleton `BackgroundService` that runs **once**
  after `ExecuteAsync` starts: creates an async scope per batch, calls
  `ICompanyProfileRepository.ListTopByTraceCountAsync(maxCount)` once,
  projects the `CompanyProfile` aggregates into `CompanyProfileDto` via
  the existing mapping extensions and calls
  `IProfileCacheService.SetAsync` per profile. Never throws out — warming
  must not break startup.
- `ListTopByTraceCountAsync` is a new repository method. Mirrors
  `ListByCountryAsync` (TraceCount DESC, filter out archived) but without
  the country filter.
- Bicep: new `modules/redis.bicep` (Basic C0, TLS 1.2 minimum, SSL-only),
  wired into `main.bicep`. The primary connection string is written to
  Key Vault as `ConnectionStrings--Redis` and App Service reads it via
  `@Microsoft.KeyVault(...)` reference — same pattern as the SQL and
  Service Bus secrets.

## Configuration contract

```jsonc
{
  "Cache": {
    "Provider": "InMemory",          // InMemory (default) | Redis
    "ProfileTtl": "7.00:00:00",       // existing
    "RedisInstanceName": "tracer:",   // NEW — Redis key prefix
    "Warming": {
      "Enabled": false,               // NEW — disabled by default
      "MaxProfiles": 1000,            // NEW — cap for ListTopByTraceCountAsync
      "DelayOnStartup": "00:00:05"     // NEW — tiny delay so app is responsive first
    }
  },
  "ConnectionStrings": {
    "Redis": "<host>:6380,password=...,ssl=True,abortConnect=False"   // required iff Provider=Redis
  }
}
```

Validation (boot-time, `ValidateOnStart`):

1. `Provider` must parse as `CacheProvider` enum (`InMemory`, `Redis`).
2. If `Provider == Redis`, `ConnectionStrings:Redis` must be non-empty.
3. `ProfileTtl` must be strictly positive.
4. `Warming.MaxProfiles` must be in `[1, 10000]`.
5. `Warming.DelayOnStartup` must be non-negative.

## Subtask decomposition

| # | Subtask | Complexity | Files touched |
|---|---------|------------|---------------|
| 1 | Central Package Management: add `Microsoft.Extensions.Caching.StackExchangeRedis` | S | `Directory.Packages.props`, `Tracer.Infrastructure.csproj` |
| 2 | Extend `CacheOptions` with `Provider`, `RedisInstanceName`, nested `Warming` record | S | `Caching/ProfileCacheService.cs` (CacheOptions) or new `Caching/CacheOptions.cs` |
| 3 | New `AddTracerDistributedCache` internal extension + options `ValidateOnStart` | M | `Caching/DistributedCacheRegistration.cs` (new) |
| 4 | Change `AddInfrastructure` to accept `IConfiguration` and delegate cache registration | M | `InfrastructureServiceRegistration.cs`, `Program.cs` |
| 5 | `RedisHealthCheck` (IDistributedCache-based probe, Degraded on failure) | M | `HealthChecks/RedisHealthCheck.cs` (new) |
| 6 | Overload `AddInfrastructureHealthChecks(IConfiguration)` — conditional "redis" check | S | `InfrastructureServiceRegistration.cs`, `Program.cs` |
| 7 | Add `ListTopByTraceCountAsync` to `ICompanyProfileRepository` + EF impl | S | Domain interface + Infrastructure repository |
| 8 | `CacheWarmingService` BackgroundService (Clock + DelayAsync seams) | M | `BackgroundJobs/CacheWarmingService.cs` (new) |
| 9 | Program.cs — conditional `AddHostedService<CacheWarmingService>` | S | `Program.cs` |
| 10 | New Bicep module `redis.bicep` + main.bicep wiring + Key Vault secret | M | `deploy/bicep/modules/redis.bicep` (new), `main.bicep`, `app-service.bicep` |
| 11 | Unit tests (DI branch, health check, warming, new repo method) | M | `Tracer.Infrastructure.Tests/` |
| 12 | `CLAUDE.md` — document cache provider toggle + warming + Bicep secret name | S | `CLAUDE.md` |

Total: ~4 h aligns with the Notion estimate.

## Affected components / modules

- `Tracer.Infrastructure` — DI registration, new cache sub-namespace files,
  health check, background service, repository impl change.
- `Tracer.Domain.Interfaces.ICompanyProfileRepository` — one new method.
- `Tracer.Api` — `Program.cs` wiring, optional warming registration,
  health-check overload call.
- `Tracer.Infrastructure.Tests` — new tests for the above.
- `deploy/bicep` — new `redis.bicep`, updates to `main.bicep` and
  `app-service.bicep`.

## Data models / API contracts

- **No new API endpoints.** Health endpoint `/health` already renders all
  registered checks generically; the `redis` check surfaces automatically.
- **Configuration** — new keys documented above.
- **Key Vault secret** — `ConnectionStrings--Redis` (double-dash → colon
  translation by App Service, per CLAUDE.md convention).
- **`IDistributedCache` keys** — unchanged (`profile:{normalizedKey}`) but
  prefixed with `tracer:` by `StackExchangeRedisCache.InstanceName` when
  Provider=Redis.
- **Repository**: `ICompanyProfileRepository.ListTopByTraceCountAsync(int maxCount, CancellationToken)` → `IReadOnlyCollection<CompanyProfile>`, non-archived, ordered `TraceCount DESC`.

## Testing strategy

### Unit tests (`Tracer.Infrastructure.Tests`)

1. **`DistributedCacheRegistrationTests`**
   - When `Cache:Provider` absent or `"InMemory"`, resolved `IDistributedCache` is `MemoryDistributedCache`.
   - When `Cache:Provider = Redis` but `ConnectionStrings:Redis` empty, `ValidateOnStart` throws `OptionsValidationException` at first resolve.
   - When `Cache:Provider = Redis` + valid connection string, resolved `IDistributedCache` is `RedisCache` (the StackExchange implementation). No network call — registration only.

2. **`RedisHealthCheckTests`**
   - Happy path: `IDistributedCache.SetAsync` + `GetAsync` on a probe key return the same bytes → `Healthy`.
   - Throw from `SetAsync` → `Degraded` (not `Unhealthy`) with generic message.
   - Null read-back (cache silently dropped write) → `Degraded`.
   - Uses NSubstitute on `IDistributedCache`; no Redis instance required.

3. **`CacheWarmingServiceTests`**
   - Runs once, calls `ListTopByTraceCountAsync(MaxProfiles)` exactly once.
   - For each profile, `IProfileCacheService.SetAsync(normalizedKey, dto, _)` is called.
   - When the repository throws, service logs and returns cleanly (no startup crash).
   - When warming disabled via `Cache:Warming:Enabled = false`, `AddHostedService` not registered (Program-level test via `WebApplicationFactory`).
   - Uses `Clock` / `DelayAsync` seams identical to `RevalidationScheduler` so tests stay deterministic and instantaneous.

4. **`ListTopByTraceCountAsyncTests`** (EF Core in-memory)
   - Seeds archived + non-archived profiles; asserts archived excluded, ordering is `TraceCount DESC`, respects `maxCount`.
   - Guard clauses: `maxCount <= 0` throws `ArgumentOutOfRangeException`.

### Integration / E2E

- **Not in scope.** Docker daemon is unavailable in this sandbox, so
  Testcontainers.Redis is a follow-up. A `[Trait("Redis", "Integration")]`
  fixture skeleton is documented as a follow-up TODO in CLAUDE.md but not
  committed as a running test.
- The smoke test script (`deploy/scripts/smoke-test-phase2.sh`) will cover
  the production `/health` check once the Bicep module is deployed.

### Acceptance criteria

1. With default configuration, `dotnet test` all pass and app behaviour is
   byte-identical to today (in-memory cache, no warming, no Redis health
   check).
2. Flipping `Cache:Provider = Redis` + a valid connection string at
   configuration time results in `IDistributedCache` resolving to the
   Redis-backed implementation; startup still succeeds.
3. Misconfiguring `Cache:Provider = Redis` without a connection string
   fails at boot with a clear `OptionsValidationException`.
4. `CacheWarmingService` registered only when `Cache:Warming:Enabled = true`;
   when registered, it performs a single sweep using the new repository
   method and calls `SetAsync` per profile.
5. `/health` response contains a `redis` entry when Redis is configured,
   Degraded-not-Unhealthy when Redis is down.
6. `deploy/bicep/main.bicep` includes the Redis module; `app-service.bicep`
   references `@Microsoft.KeyVault(...)` for `ConnectionStrings--Redis`.
7. No new secrets in the repository; no raw connection strings logged
   (only `ex.GetType().Name` — CWE-209 mitigation).
8. No changes to the existing cache key format or `ProfileCacheService`
   public surface; all existing cache unit tests still pass.
9. CLAUDE.md updated with: cache provider toggle, warming contract,
   Redis health-check semantics, Key Vault secret name.

## Security considerations (handled during implementation)

- No hardcoded connection strings — loaded from Key Vault only.
- Redis connection forced over SSL (`minimumTlsVersion: 1.2`,
  `enableNonSslPort: false` in Bicep).
- Cache failures never expose `ex.Message`; log `ex.GetType().Name` only.
- `InstanceName` prefix prevents cross-environment key collisions when
  sharing a Redis instance.
- No PII in warming logs (only profile GUID and count — `LegalName` is
  PII under GDPR, never logged).
- Warming disabled by default; operators must opt-in.

## Rollout plan (documented, not executed)

1. Deploy Bicep with `Cache:Provider = InMemory` set — Redis provisioned
   but not yet in the hot path. `/health` still green.
2. Flip App Service config `Cache:Provider = Redis`; restart instance.
   `/health` shows `redis: Healthy`; cache hits continue via Redis.
3. Enable warming if desired (`Cache:Warming:Enabled = true`) and monitor
   startup logs for `Cache warming complete: N profiles loaded`.
4. Rollback path: flip `Cache:Provider` back to `InMemory`; no data loss
   because Redis is a cache, not a source of truth.

## Follow-ups (out of scope — captured as TODOs in commit message)

- Testcontainers.Redis integration test once the CI image ships Docker.
- Periodic re-warming or LRU-style refresh (Phase 4 end-of-sprint).
- Redis key eviction policy tuning and memory-alert wiring.
