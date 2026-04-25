# Tracer — operations handbook

Audience: on-call engineer, deployer, SRE.

> **TODO:** populate `Escalation contacts` and `Production environment URLs`
> sections below with concrete values before first production go-live.

## 1. At a glance

```
prod-rg               rg-tracer-prod
prod-app              tracer-prod-api.azurewebsites.net          (TODO)
prod-spa              tracer-prod-web.azurestaticapps.net        (TODO)
prod-sql              tracer-prod-sql.database.windows.net       (TODO)
prod-redis            tracer-prod-redis.redis.cache.windows.net  (TODO)
prod-ai               tracer-prod-aoai.openai.azure.com          (TODO)
ops dashboard         Tracer v1.0 Health (Azure Monitor)
log workspace         tracer-prod-logs                           (TODO)
```

## 2. Common operations

### 2.1 Deploy a new version

1. Merge to `develop`. CI runs `dotnet test`, `npm test`, `dotnet list package --vulnerable`,
   and `npm audit --audit-level=high`. All must pass.
2. Tag a release: `git tag -a v1.x.y -m "..." && git push --tags`.
3. From the **Actions** tab, run **Deploy** workflow with environment `prod`,
   ref `main` (or a `release/*` branch), and `dry_run = false`.
4. The job runs `az deployment group what-if` first; review the diff.
5. After deploy, the `verify-prod` job hits `/health`, runs the
   `trace-smoke` k6 (low-load) script, and confirms metrics flow.

See `deploy/RUNBOOK-PROD.md` for the per-step commands. Bicep modules
live in `deploy/bicep/`.

### 2.2 Rotate API keys

1. In Key Vault, create a new secret `Auth--ApiKeys--{n}--Key`. Set
   `Auth--ApiKeys--{n}--ExpiresAt` on the **old** key to a date 7 days out.
2. Restart the App Service (config refresh). `ApiKeyOptionsValidator`
   accepts both keys; `ApiKeyAuthMiddleware` checks `IsActive(now)` per
   request.
3. After the deadline, remove the old key entry.

No redeploy needed — `IOptionsMonitor` picks up the change after restart.

### 2.3 Pause re-validation

```
az webapp config appsettings set \
  -g rg-tracer-prod -n tracer-prod-api \
  --settings Revalidation__Enabled=false
```

Restart the app. The `RevalidationScheduler` will not register on next boot.
To resume: set `Revalidation__Enabled=true` and restart.

### 2.4 Force-cache-warm on startup

Set `Cache:Warming:Enabled=true` and restart. `CacheWarmingService` will
load `Cache:Warming:MaxProfiles` (default 1000) of the highest-`TraceCount`
profiles into the distributed cache before serving traffic.

### 2.5 Run a load test

```bash
./deploy/scripts/run-load-test.sh trace-smoke "$BASE_URL" "$API_KEY"
./deploy/scripts/run-load-test.sh trace-load  "$BASE_URL" "$API_KEY"
./deploy/scripts/run-load-test.sh batch-load  "$BASE_URL" "$API_KEY"
```

`trace-smoke` is the deploy gate. `batch-load` self-paces 12 s between
iterations to honour the `batch` rate-limit (5 req/min/IP).

### 2.6 Force-run archival now

```
az webapp config appsettings set \
  -g rg-tracer-prod -n tracer-prod-api \
  --settings Archival__Enabled=true Archival__IntervalHours=1
```

(Wait for one tick, then revert to `IntervalHours=24`.) Operates via
`ICompanyProfileRepository.ArchiveStaleAsync` which issues bounded-batch
`UPDATE` statements — no domain events emitted.

## 3. Monitoring

### 3.1 Active alerts

- **Response time warning**: P95 over 1 min ≥ 1 s for 5 min.
- **Error rate critical**: 5xx rate ≥ 5% for 5 min.
- **Re-validation failures warning**: failed runs ≥ 5 / hour.

Configured in `deploy/bicep/modules/monitoring.bicep`. Recipients via
`ALERT_EMAIL` GitHub environment variable.

### 3.2 Dashboards

- **Tracer v1.0 Health** (Azure Monitor) — request rate, P50/P95 latency,
  5xx ratio, DB DTU, Redis ops/s, Service Bus active messages, re-validation
  KPI.
- **Provider catalog** — per-provider success / timeout / quota metrics
  via `tracer.provider.*` counters.

### 3.3 Logs

Application Insights → Logs:
```kusto
traces
| where timestamp > ago(1h)
| where severityLevel >= 2
| project timestamp, message, customDimensions
| order by timestamp desc
```

Every log line carries `TraceId` / `SpanId` from the active OpenTelemetry
Activity (Serilog enricher).

## 4. SignalR

Hub at `/hubs/trace`. Group-targeted events:

- `Clients.Group(traceId)`: `SourceCompleted`, `TraceCompleted`
- `Clients.All`: `ChangeDetected`, `ValidationProgress`

Clients must call `SubscribeToTrace(traceId)` to join the group before
receiving trace-specific events. WebSocket auth is via `access_token`
query string (browser cannot set custom headers on the WS upgrade).

## 5. Service Bus

Topic `tracer-changes` with two subscriptions:

| Subscription | Filter | Max delivery |
|---|---|---|
| `fieldforce-changes` | `Severity='Critical' OR Severity='Major'` | 5 |
| `monitoring-changes` | `1=1` (TrueFilter) | 10 |

Both have `deadLetteringOnMessageExpiration=true` and
`deadLetteringOnFilterEvaluationExceptions=true`. **Always set both flags**
on new subscriptions; the filter-exception flag is the non-obvious one.

`Cosmetic` severity is never published (log-only). `Minor` is published to
`monitoring-changes` only; UI polls `/api/changes` for it.

## 6. CKB hygiene

- **Archival**: profiles with `TraceCount ≤ 1` AND
  `LastEnrichedAt < now - 365d` get bulk-archived nightly. Auto-unarchive
  on incoming trace via `CkbPersistenceService`.
- **GDPR retention**: personal-data fields (currently only `Officers`)
  are erased after `Gdpr:PersonalDataRetentionDays` (default 1095). The
  `PersonalDataRetentionService` runs daily, idempotent.
- **Re-validation**: stale fields trigger `DeepRevalidationRunner` once
  ≥ `Revalidation:Deep:Threshold` (default 3) fields are expired.

## 7. Escalation contacts

> **TODO:** Fill in before go-live.

| Role | Name | Channel | Hours |
|---|---|---|---|
| Primary on-call | TODO | TODO | TODO |
| Backup on-call | TODO | TODO | TODO |
| FieldForce integration | TODO | TODO | TODO |
| Azure subscription owner | TODO | TODO | TODO |
| Security / GDPR | TODO | TODO | TODO |

## 8. Production deployment runbook (cross-reference)

For first-time production go-live, follow `deploy/RUNBOOK-PROD.md`
(operator-only steps including resource group creation, OIDC federated
service principal setup, Key Vault seeding, and the first
`az deployment group create`).

This handbook covers steady-state operations; the runbook covers
"how to spin everything up the first time".
