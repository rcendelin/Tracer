# Troubleshooting

Concrete fix-it postupy for the most common production / staging issues.
Each entry: **Symptom → Diagnose → Fix → Why this happened**.

---

## 1. Trace requests start returning 401 unauthorized

**Symptom.** Clients (FieldForce, UI) start getting `401 Unauthorized`
on `/api/*` endpoints. `/health` still works.

**Diagnose.**
```kusto
traces
| where message contains "ApiKey" and timestamp > ago(15m)
| project timestamp, message, customDimensions
```

**Fix.**
- API key likely **expired** — check `Auth:ApiKeys:N:ExpiresAt` in Key
  Vault. `ApiKeyAuthMiddleware` re-checks `IsActive(now)` per request
  and rejects expired keys.
- Add a new active key (see [handbook.md §2.2](./handbook.md#22-rotate-api-keys)),
  then restart the app to pick up the config change.

**Why.** API keys have ISO 8601 expiry. `ApiKeyOptionsValidator` rejects
already-expired keys at *startup*; runtime expiry is rejected per request.

---

## 2. App fails to boot with `OptionsValidationException`

**Symptom.** App Service health check fails right after deploy. Logs
show `OptionsValidationException` referencing one of: `Auth`, `Gdpr`,
`Revalidation:FieldTtl`, `OpenApi`, `Security:Headers`.

**Diagnose.** The exception message tells you which option section.
Common patterns:

| Section | Likely cause |
|---|---|
| `Auth:ApiKeys` | Key < 16 chars, duplicate, or `ExpiresAt` already past |
| `Revalidation:FieldTtl` | Unknown `FieldName`, non-positive duration, unparseable TimeSpan |
| `Gdpr:PersonalDataRetentionDays` | Set to ≤ 0 |
| `OpenApi:ServerUrls` | Non-absolute URI in the array |
| `Security:Headers` | `HstsMaxAgeSeconds < 0` |

**Fix.** Correct the offending entry in Key Vault / App Settings. Restart.

**Why.** All these sections use `.ValidateOnStart()` so misconfig fails
immediately rather than at first resolve. This is intentional.

---

## 3. Service Bus messages silently disappear

**Symptom.** `FieldForce` is not receiving change notifications.
`/api/changes` shows the changes are detected. Service Bus monitor shows
zero throughput.

**Diagnose.**
```kusto
AppServiceConsoleLogs
| where Log contains "ServiceBusPublisher" and TimeGenerated > ago(15m)
```
Then look at the subscription DLQ (`fieldforce-changes/$DeadLetterQueue`).

**Fix.**
- If DLQ has messages with reason `FilterEvaluationException`: the SQL
  filter on the subscription is malformed. Check `deploy/bicep/modules/service-bus.bicep`
  and re-deploy. The flag `deadLetteringOnFilterEvaluationExceptions = true`
  is what saves you here — without it, malformed filters silently drop messages.
- If publisher is failing: check Service Bus `ConnectionStrings:ServiceBus`
  and the namespace's RBAC role assignment to the App Service managed identity.

**Why.** Service Bus subscription filters can fail at runtime. Without DLQ
on filter exceptions, the failure mode is silent message loss.

---

## 4. Re-validation runs fail in waves

**Symptom.** `tracer.revalidation.failed` counter spikes. Profiles stuck
showing stale data.

**Diagnose.**
```kusto
customMetrics
| where name == "tracer.revalidation.failed"
| where timestamp > ago(1h)
| summarize sum(value) by bin(timestamp, 5m), trigger=tostring(customDimensions.trigger)
```
Then inspect logs for the failing scope:
```kusto
traces
| where customDimensions.EventId in ("4001","4002","4003")
| order by timestamp desc
```

**Fix (depends on root cause).**
- **Per-profile timeout (5 min)**: a single waterfall stalled. Likely a
  Tier 2 scraper hit a CAPTCHA — see provider-specific logs.
- **Provider quota / 429**: check `tracer.provider.*` counters. Resilience
  defaults pin per-provider rate limits in `ProviderResilienceDefaults`;
  override per environment via `Resilience:Providers:<id>:*`.
- **DB connection pool exhaustion**: `RevalidationScheduler` MUST create
  one `IServiceScopeFactory.CreateAsyncScope()` per profile (not for the
  whole sweep). Verify the code hasn't regressed.

**Why.** EF Core DbContext is Scoped, scheduler is Singleton — captive
dependency = cross-thread EF exceptions. The discriminating
`OperationCanceledException when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)`
catch in `RevalidationScheduler` separates per-profile timeout (logged,
continue) from host shutdown (re-throw).

---

## 5. Cache hit ratio drops to zero after switching to Redis

**Symptom.** Latency increases right after enabling
`Cache:Provider = Redis`. Redis monitor shows commands but
`tracer.cache.hit_ratio` stays at 0.

**Diagnose.**
```kusto
customMetrics
| where name == "tracer.cache.hits" or name == "tracer.cache.misses"
| where timestamp > ago(15m)
```

**Fix.**
- Check `Cache:RedisInstanceName` — it must match what the previous
  cache writes used. Default `"tracer:"`. If you changed environments,
  the prefix changed and existing keys are unreachable.
- Run `redis-cli KEYS 'tracer:*' | head` against the prod Redis to
  confirm the namespace.

**Why.** `RedisInstanceName` is a key-prefix, not a logical database;
it's there so multiple environments can share a single Redis instance
without collisions. Mis-prefixing makes lookups silently miss.

---

## 6. Web SPA shows "Connection lost" after a deploy

**Symptom.** UI banner says SignalR connection failed. Browser console
shows WebSocket upgrade returning 401.

**Diagnose.** WebSocket `Upgrade` request can't carry custom headers, so
the SPA passes the API key as `?access_token=...`. Check:

- The API host's `ApiKeyAuthMiddleware` allowlist includes
  `/hubs/trace/negotiate` and the WebSocket path with the access_token
  query string.
- `useSignalR.ts` `accessTokenFactory` returns the current API key.

**Fix.** If the API key was rotated, the SPA must reload to pick up the
new value (it's read from build-time config in the SPA). Force a hard
refresh.

**Why.** WebSocket upgrade can't carry custom headers, so SignalR uses
`access_token` query param. The API middleware accepts the key from
header **or** Bearer **or** the access_token query string in that order.

---

## 7. Tier 2 scraper provider returns "Provider error" en masse

**Symptom.** `Handelsregister`, `StateSos`, `LATAM-*` providers return
`ProviderResult.Error("generic message")` for every request.

**Diagnose.** The error message is intentionally generic (CWE-209
mitigation — raw exception messages can leak paths/credentials). Pull
the actual cause from logs:

```kusto
traces
| where customDimensions.ProviderId in ("handelsregister","state-sos","latam-afip","latam-sii","latam-rues","latam-sat")
| where customDimensions.EventId in ("9201","9202","9200")
| project timestamp, message, customDimensions.ExceptionType
| order by timestamp desc
```

**Fix.**
- **HTML structure changed**: scraper selectors are written against the
  page's specific markup. Inspect the live page, update the AngleSharp
  selectors in `Adapters/<X>Adapter.cs`, add a regression test.
- **Antibot / CAPTCHA wall**: adapters return `null` for CAPTCHA detection
  (treated as `NotFound`, not `Error`). Mexico SAT's `LooksLikeCaptchaWall`
  is the reference. If a new wall pattern emerges, extend that detector.
- **Rate limit exceeded**: shared rate limiter hit (Handelsregister 60/h,
  LATAM 10/min across-countries). Check the per-instance counter; this is
  the symptom of horizontal scaling without distributed limiter (Redis).

**Why.** Scraping is fragile. Generic error messages keep operational
surface clean while structured logs preserve full context for SREs.

---

## 8. CSV export shows formula execution warnings in Excel

**Symptom.** Users report Excel showing security warnings ("formula
in this file") when opening exported CSV / XLSX.

**Diagnose.** Some cell value started with `=`, `+`, `-`, `@`, TAB,
or CR — Excel interprets as formula.

**Fix.** Check `CsvInjectionSanitizer`:
- It must run for **every** cell (CSV and XLSX).
- New export columns added to `ExportMappingExtensions.ToExportRow`
  must route through the sanitiser; do not bypass.

**Why.** CWE-1236 (CSV / XLSX formula injection). The sanitiser prefixes
an apostrophe to suspect cells so spreadsheet apps treat them as text.

---

## When in doubt

- Check `CLAUDE.md` for the dense bullet on the area you're touching.
- Look at the matching `docs/implementation-plans/b-XX-*.md`.
- Re-read `docs/architecture.md` to refresh the data flow.
- Escalate via [handbook.md §7](./handbook.md#7-escalation-contacts).
