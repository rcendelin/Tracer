# B-78 — Deployment Phase 3 + performance

Branch: `feature/b-78-deployment-phase-3`
Fáze: 3 — AI + scraping
Odhad: 4 h

## 1. Cíl

Doplnit deployment + provozní materiály pro Phase 3 workload (web scraping
+ AI extraction + Deep depth + entity resolution + re-validation engine
+ Change Feed + Validation Dashboard + GDPR layer). Konkrétně:

1. **Azure OpenAI** Bicep modul, provisioning GPT-4o-mini deploymentu pro
   `AiExtractor` (B-57) i `LlmDisambiguator` (B-64).
2. **Smoke test script** pro Phase 3 — exercituje Deep flow, AI extraction
   path, Change Feed, Validation Dashboard, manual override.
3. **Performance tuning guide** pro Phase 3 — App Service tier, heap / GC,
   rate limits, scaling thresholds.
4. **`Resilience` defaults** v configu pro Phase 3 providery (Handelsregister,
   LATAM, AI extractor, LLM disambiguator).

Phase 1 deployment (B-29 / B-30) a Phase 2 deployment (B-52) jsou už
v `deploy/bicep/main.bicep` + `smoke-test.sh` / `smoke-test-phase2.sh`.
B-78 je ten finální patch před produkční go-live (B-92).

## 2. Dekompozice

| # | Subtask | Složitost | Výstup |
|---|---|---|---|
| 1 | `azure-openai.bicep` modul | M | AOAI account + 2 deploymenty (extractor + disambiguator) |
| 2 | `main.bicep` wiring + Key Vault secrets | S | `Providers--AzureOpenAI--Endpoint`, `--ApiKey`, deployment names |
| 3 | `app-service.bicep` env vars pro AOAI | S | App settings reference Key Vault |
| 4 | `smoke-test-phase3.sh` | M | Deep flow + Change Feed + Validation + manual override |
| 5 | `docs/performance/phase-3-tuning.md` | M | Tier, GC, rate limits, scaling |
| 6 | `appsettings.json` Phase 3 resilience defaults | S | per-provider Resilience settings |
| 7 | `deploy.yml` env validation pro AOAI | S | Fail fast pokud chybí AOAI parametry |
| 8 | CLAUDE.md update | S | Konvence + odkazy |

## 3. Komponenty / moduly

- `deploy/bicep/modules/azure-openai.bicep` — nový.
- `deploy/bicep/main.bicep` — přidat AOAI module + Key Vault secrets +
  předat parametry do app-service.
- `deploy/bicep/main.bicepparam` / `main-prod.bicepparam` — opt-in flag
  `enableAzureOpenAI` (default true v prod, false v test).
- `deploy/scripts/smoke-test-phase3.sh` — nový.
- `docs/performance/phase-3-tuning.md` — nový.
- `src/Tracer.Api/appsettings.json` — Resilience:Providers:* defaults.

## 4. Bicep AOAI modul — kontrakt

```bicep
@description('Azure OpenAI account + GPT-4o-mini deployments for Tracer Phase 3.')
param location string
param namePrefix string
param tags object

@description('Soft deletion retention in days (Cognitive Services).')
@minValue(7)
@maxValue(90)
param softDeleteRetentionDays int = 7

@description('GPT-4o-mini model name.')
param modelName string = 'gpt-4o-mini'

@description('GPT-4o-mini model version (Azure OpenAI catalogue).')
param modelVersion string = '2024-07-18'

@description('Capacity for the extractor deployment (TPM in thousands).')
@minValue(10)
@maxValue(1000)
param extractorCapacity int = 50

@description('Capacity for the disambiguator deployment.')
@minValue(10)
@maxValue(1000)
param disambiguatorCapacity int = 30
```

Outputs:

- `endpoint string` — `https://{name}.openai.azure.com/`
- `extractorDeploymentName string`
- `disambiguatorDeploymentName string`
- `@secure() apiKey string` — primary key (consumed by Key Vault module)

Public network access **disabled** by default; production ties identity
via App Service managed identity → `Cognitive Services OpenAI User` role.

## 5. Performance tuning summary

Captured in `docs/performance/phase-3-tuning.md`. Highlights:

- App Service plan **P1V3** for production (Phase 3 load: AI calls add
  ~10s tail latency; B-86 baselines confirm).
- App Service: **Always-On**, **Always-Ready instances ≥ 1** for cold-start
  control, **Health-check path** `/health` enabled.
- .NET: `<ServerGarbageCollection>true</ServerGarbageCollection>`
  (Server GC) — already default for ASP.NET Core but documented.
- App Service `Cache:Provider = Redis`, `Cache:Warming:Enabled = true`,
  `Cache:Warming:MaxProfiles = 1000` for steady-state.
- `Resilience:Providers:ai-extractor:AttemptTimeout = 20s` (matches
  Tier 3 budget). `ai-disambiguator` 5s.
- Provider rate limits set defensively below documented service quotas
  (e.g. AOAI default 50K TPM → set 30K to leave 40% headroom).
- Service Bus consumer concurrency: `MaxConcurrentCalls = 4` per instance
  (Phase 2 default 2 was conservative for sync-only workload).

## 6. Smoke test Phase 3 — flow

`deploy/scripts/smoke-test-phase3.sh https://<api> <API_KEY>`:

1. `GET /health` → 200, body has `database` and `redis` healthy.
2. `POST /api/trace` with `Depth = Deep`, expects `201 Created` + traceId.
3. Poll `GET /api/trace/{id}` for status `Completed` (timeout 60 s).
4. `GET /api/profiles?country=DE` returns ≥ 1 profile (verifies
   Tier 2 scraping path).
5. `GET /api/changes/stats` returns aggregate.
6. `GET /api/validation/stats` returns dashboard counts.
7. `GET /api/changes?since=<now-1h>` returns recent feed.
8. `PUT /api/profiles/{id}/fields/Phone` with body `{value, reason}` →
   204 (manual override audit).
9. `GET /api/profiles/{id}/history` shows the override with
   `changeType = ManualOverride` and `detectedBy` starting with
   `manual-override:apikey:`.

Exit non-zero on any failure. Designed to be the deploy gate after
`az deployment group create`.

## 7. Akceptační kritéria

1. `azure-openai.bicep` modul je v `deploy/bicep/modules/` a `main.bicep`
   ho volá podmíněně přes `enableAzureOpenAI` parametr.
2. Public network access na AOAI je `Disabled` v prod profile;
   `Cognitive Services OpenAI User` role je granted App Service managed identity.
3. `smoke-test-phase3.sh` projede happy path bez assertion error a vrátí
   exit 0; jinak ne-nulový exit s jasnou chybovou zprávou.
4. `docs/performance/phase-3-tuning.md` má sekce App Service plan / GC /
   cache / rate limits / Service Bus / monitoring; každá sekce má
   konkrétní hodnotu nebo command.
5. `appsettings.json` má Resilience:Providers defaults pro `ai-extractor`
   a `ai-disambiguator` (timeout / retry / circuit breaker).
6. `deploy.yml` `verify-prod` job validuje, že AOAI App Settings nejsou prázdné
   (early exit pokud Key Vault reference selže).

## 8. Konzervativní rozhodnutí

- **AOAI module je opt-in** — test prostředí neřeší AOAI quota; prod ho
  zapne. `enableAzureOpenAI` parametr default `false` v `main.bicepparam`,
  `true` v `main-prod.bicepparam`.
- **Žádné GPT-4 / GPT-4-turbo deployment** — držíme se GPT-4o-mini
  (B-56/B-64). Upgrade je další blok, ne tento.
- **No private endpoints in B-78** — VNet integration je `B-92` /
  production hardening follow-up. AOAI public access je `Disabled`,
  výchozí `NetworkAcls.DefaultAction = Deny`, App Service má managed
  identity přístup.
- **Performance "guide" not "runbook"** — konkrétní čísla SLO a alert
  thresholdy se kalibrují po prvním produkčním běhu (B-92 + B-86 baselines).
  B-78 dodává tunable knoby a vysvětluje, co znamenají.

## 9. Sandbox limitations

- `dotnet` ani `az` v sandboxu chybí; Bicep moduly se ověří až v CI
  via `bicep build` step v `deploy.yml`.
- `smoke-test-phase3.sh` je shell script (curl / jq) — bez CI runneru
  ho nelze spustit, ale syntakticky se validuje `shellcheck` v CI.
- Skutečný AOAI deployment vyžaduje quota approval na úrovni Azure
  subscription; tento blok dodává jen artefakty, samotný `az deployment`
  je v rukou operátora (per CLAUDE.md "Azure — Přísný zákaz destruktivních operací").

## 10. Follow-upy

- Private endpoint na AOAI (B-92 follow-up).
- Per-environment performance baselines do `docs/performance/baselines.md`
  (after first prod run + first 24h of monitoring data).
- AOAI cost guard — daily budget alert via Cost Management.
- AOAI key rotation runbook (mirroring B-87 API key rotation pattern).
