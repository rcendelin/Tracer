# Phase 3 performance tuning guide (B-78)

Phase 3 introduces three workload characteristics that Phase 1/2 deployments
were not sized for:

1. **AI-extraction tail latency** (B-56/B-57) — single Deep trace can spend
   ~10 s in Azure OpenAI. Tier 3 budget is 20 s but P95 should be < 15 s.
2. **Sustained background work** — `RevalidationScheduler` (B-65, B-66, B-67)
   ticks hourly with up to 100 profiles per run; each may invoke the full
   waterfall.
3. **HTML scraping rate limits** — Handelsregister 60 req/h, LATAM 10 req/min
   shared, State SoS 20 req/min. Per-instance sliding-window enforced;
   horizontal scaling without distributed limiter (Redis) breaks compliance.

## App Service plan

| Environment | SKU | Workers | Always-On | Always-Ready |
|---|---|---|---|---|
| `test` | B1 | 1 | yes | 0 |
| `prod` | **P1V3** | **2** | yes | **≥ 1** |

Rationale: P1V3 has 8 GB RAM (Bicep default `appServicePlanSize` should be
adjusted) and dedicated CPU. Always-Ready avoids cold-starts on Phase 3
Deep traces (~6 s cold-start vs ~120 ms warm). Multi-worker requires a
distributed re-validation lock — not yet implemented; **keep `Workers = 1`
until B-92** introduces it. Override only after the distributed lock lands.

## .NET runtime

ASP.NET Core defaults are correct for Phase 3 — Server GC is on by default.
Document explicitly so nobody flips it:

```xml
<!-- Tracer.Api.csproj — already implicit, restated for clarity -->
<PropertyGroup>
  <ServerGarbageCollection>true</ServerGarbageCollection>
  <ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>
  <TieredCompilation>true</TieredCompilation>
</PropertyGroup>
```

Do **not** enable `ReadyToRun` or `NativeAOT` — providers use AngleSharp
+ MediatR reflection that AOT does not support.

## Distributed cache

Production must run with Redis (B-79):

```
Cache:Provider           = Redis
Cache:Warming:Enabled    = true
Cache:Warming:MaxProfiles = 1000
Cache:Warming:DelayOnStartup = 00:00:30
Cache:RedisInstanceName  = tracer:
```

The cache-warming service runs once on startup, capped at 16 parallel
writes. Skipping it leaves Phase 3 cold-start hitting the database for
every popular profile lookup.

## Provider resilience defaults (Phase 3)

`appsettings.json` `Resilience:Providers:<id>` overrides the global defaults
in `ProviderResilienceDefaults.BuildDefaults()`. Phase-3 specific values:

```jsonc
"Resilience": {
  "Providers": {
    "ai-extractor": {
      "AttemptTimeout": "00:00:20",
      "MaxRetryAttempts": 1,
      "TotalRequestTimeout": "00:00:25",
      "CircuitBreaker": {
        "FailureRatio": 0.5,
        "MinimumThroughput": 10,
        "SamplingDuration": "00:01:00",
        "BreakDuration": "00:00:30"
      },
      "RateLimit": {
        "PermitsPerSecond": 20,
        "QueueLimit": 0
      }
    },
    "ai-disambiguator": {
      "AttemptTimeout": "00:00:05",
      "MaxRetryAttempts": 1,
      "TotalRequestTimeout": "00:00:08"
    },
    "handelsregister": {
      "AttemptTimeout": "00:00:12",
      "MaxRetryAttempts": 1,
      "TotalRequestTimeout": "00:00:15",
      "RateLimit": {
        "PermitsPerSecond": 0.0166,
        "QueueLimit": 0
      }
    },
    "latam-afip": { "AttemptTimeout": "00:00:12" },
    "latam-sii": { "AttemptTimeout": "00:00:12" },
    "latam-rues": { "AttemptTimeout": "00:00:12" },
    "latam-sat": { "AttemptTimeout": "00:00:12" }
  }
}
```

`PermitsPerSecond = 0.0166` corresponds to 60 / 3600 — the legal
Handelsregister rate. Do **not** raise it.

## AOAI capacity sizing

Default deployment capacity (B-78 Bicep parameters):

| Deployment | TPM (×1000) | Notes |
|---|---|---|
| `extractor` | 50 | Used by `AiExtractor` (B-57). Per-call ~3K tokens. |
| `disambiguator` | 30 | Used by `LlmDisambiguator` (B-64). Per-call ~1.5K tokens. |

Budget envelope at 50K TPM extractor: ~16 Deep traces / minute ceiling.
Override via `extractorCapacity` / `disambiguatorCapacity` Bicep params
when production load forecasts exceed this.

## Service Bus

| Setting | Phase 2 default | **Phase 3** | Reason |
|---|---|---|---|
| `tracer-request` consumer `MaxConcurrentCalls` | 2 | 4 | Async waterfall path is fully async; Phase 3 batch surge tolerated. |
| `tracer-changes` subscription `MaxDeliveryCount` | 5 | 5 (`fieldforce-changes`) / 10 (`monitoring-changes`) | unchanged — see B-74. |
| `LockDuration` | 30 s | 60 s | AI extraction can extend per-message processing. |

## Rate limits (API ingress)

`Program.cs` `batch` policy stays at 5 req/min/IP; `export` at 10. Phase 3
adds:

- `manual-override` policy (planned follow-up): 30 req/min/IP — manual
  overrides shouldn't be a load vector but rate-limit is cheap insurance.
- WebSocket SignalR connections: rely on App Service plan max sockets,
  not application-level rate limits.

## Monitoring: SLO targets and alerts

| Metric | Target | Alert threshold |
|---|---|---|
| `tracer.trace.duration` (Quick) p95 | ≤ 5 s | > 7 s for 5 min |
| `tracer.trace.duration` (Standard) p95 | ≤ 15 s | > 18 s for 5 min |
| `tracer.trace.duration` (Deep) p95 | ≤ 30 s | > 35 s for 10 min |
| 5xx ratio | < 1% | > 5% for 5 min |
| `tracer.revalidation.failed` rate | < 5/h | > 10/h for 1 h |
| `tracer.cache.hit_ratio` | ≥ 0.6 | < 0.3 for 30 min |
| AOAI `ResourceUtilization` | < 80% | > 90% for 10 min |
| AOAI `ProcessedPromptTokens` | within capacity | sustained 95% capacity for 1 h |

Alerts wired in `deploy/bicep/modules/monitoring.bicep` (B-92);
this guide just sets the targets.

## Cold-start observations (B-86 baselines)

From `docs/performance/baselines.md`:

- App Service P1V3 cold-start: ~6 s (first request after idle).
- After Always-Ready instance is warm: ~120 ms first byte for a Quick trace
  with cache hit.
- AI extraction cold-start (first call after AOAI deployment idle for ~1 h):
  ~3 s warm-up cost, then steady ~7 s per call.

These numbers feed the SLO table above. **Do not regress** — `B-86`
benchmarks are gated by `.github/workflows/perf.yml` and tracked in
`docs/performance/baselines.md`.

## Scaling triggers (App Service plan auto-scale)

Configure on the App Service plan when production traffic justifies it:

- Scale-out: CPU > 70% for 10 min OR HTTP queue length > 100.
- Scale-in: CPU < 30% for 30 min AND HTTP queue length < 10.
- Min instances: 1 (test), 2 (prod).
- Max instances: 5 (cost guard).

**Distributed re-validation lock prerequisite** — multi-instance scaling
without B-92 distributed lock causes duplicate re-validation runs.
Phase 3 keeps `Workers = 1` until B-92 lands; auto-scale config goes
in B-92.
