# B-83 — CKB archival

**Fáze:** 4 — Scale + polish
**Odhad:** 3 h
**Branch:** `claude/eloquent-babbage-KOJvD`
**Datum zahájení:** 2026-04-22

## 1. Cíl bloku

Zprovoznit mechanismus, který udržuje CKB "štíhlý" v horizontu měsíců až let:

1. **Archivace** — pravidelný (měsíční) background job, který označí `IsArchived = true` na profilech, které (a) mají `TraceCount == 1` a zároveň (b) `LastEnrichedAt` starší než 12 měsíců. Jde o "one-shot" profily bez dalšího obchodního zájmu.
2. **Vyloučení z re-validace** — archivované profily nesmí konzumovat re-validační budget (už částečně vynuceno v `ICompanyProfileRepository.GetRevalidationQueueAsync`; blok to explicitně otestuje a doplní tam, kde to chybí).
3. **Přístup přes API** — list endpoint už má `includeArchived` parametr. Dovyplnit chybějící kousky:
   - pagination meta (`totalCount`) respektuje filtr,
   - `GET /api/profiles/{id}` vrátí archivovaný profil bez speciální logiky (audit trail zůstane dohledatelný).
4. **Auto-unarchive při novém trace** — pokud enrichment pipeline zasáhne archivovaný profil (resolvovaný přes registration-id nebo normalized key), profil se automaticky odarchivuje ještě před `IncrementTraceCount()`.
5. **Observability** — metriky `tracer.ckb.archived` a `tracer.ckb.unarchived` (counter).
6. **UI** — `ProfilesPage` už má checkbox "Include archived" i Archived badge. Blok jen ověří, že funguje, a přidá infobox "Archived profiles are excluded from revalidation and fuzzy matching".

## 2. Kontext v codebase

- `CompanyProfile.Archive()` a `Unarchive()` existují a pouze přepínají flag. Domain event pro archival **úmyslně neraisingujeme** — archival je interní CKB operace, ne business change, a neposílá se přes Service Bus / SignalR.
- `ICompanyProfileRepository.GetRevalidationQueueAsync` už filtruje `!p.IsArchived`.
- `ICompanyProfileRepository.ListByCountryAsync` už filtruje `!p.IsArchived` → archivované profily jsou neviditelné pro fuzzy matching (`EntityResolver.ResolveAsync`). Tzn. nový trace dorazí skrz `FindByRegistrationIdAsync` nebo `FindByKeyAsync` (ty archivovaný profil vrátí) → unarchive se volá v `CkbPersistenceService.PersistEnrichmentAsync`.
- `ICompanyProfileRepository.ListAsync` a `CountAsync` už mají parametr `includeArchived`.
- `CompanyProfileDto.IsArchived` a `ProfilesPage.tsx` "Include archived" checkbox + Archived badge jsou hotové.
- `CompanyProfileConfiguration.HasFilter("[IsArchived] = 0")` je filtrovaný index `LastValidatedAt` — zajistí, že re-validace nikdy neskenuje archived rows.
- `RevalidationScheduler` (B-65) je Singleton `BackgroundService` v `Tracer.Infrastructure/BackgroundJobs/`, používá `IServiceScopeFactory.CreateAsyncScope()` per unit of work. Stejný pattern aplikujeme na `ArchivalService`.

## 3. Dekompozice

| # | Úkol | Odhad | Soubory |
|---|------|-------|---------|
| 1 | `ArchivalOptions` — `Enabled`, `IntervalHours` (default 24), `MinAgeDays` (365), `MaxTraceCountForArchival` (1), `BatchSize` (500) | 0.2 h | `Tracer.Application/Services/ArchivalOptions.cs` |
| 2 | `ICompanyProfileRepository.ArchiveStaleAsync(DateTimeOffset cutoff, int maxTraceCount, int batchSize, ct)` — bulk update `IsArchived = true`; vrací počet archivovaných. | 0.4 h | `ICompanyProfileRepository.cs`, `CompanyProfileRepository.cs` |
| 3 | `ITracerMetrics.RecordCkbArchived(int count)` + `RecordCkbUnarchived()` | 0.15 h | `ITracerMetrics.cs`, `TracerMetrics.cs` |
| 4 | `ArchivalService : BackgroundService` — denní tick, testovací seamy (`Clock`, `DelayAsync`), per-tick timeout, LoggerMessage partials | 0.75 h | `Tracer.Infrastructure/BackgroundJobs/ArchivalService.cs` |
| 5 | `CkbPersistenceService.PersistEnrichmentAsync` — unarchive hook (pokud `profile.IsArchived` → `Unarchive()` + metric, před `UpsertAsync`) | 0.2 h | `CkbPersistenceService.cs` |
| 6 | DI registrace + bind sekce `Archival` v `Program.cs` (podmíněně `Enabled`) | 0.15 h | `Program.cs`, `appsettings.json` |
| 7 | Unit testy `ArchivalService` (off-tick, budget, idempotence, timeout, chyba) | 0.6 h | `tests/Tracer.Infrastructure.Tests/BackgroundJobs/ArchivalServiceTests.cs` |
| 8 | Unit/integration testy repo metody `ArchiveStaleAsync` (SQLite in-memory nebo stub) | 0.2 h | `CompanyProfileRepositoryTests` / InMemoryCompanyProfileRepository helper |
| 9 | Unit test `CkbPersistenceService` — archived → unarchived | 0.2 h | `CkbPersistenceServiceTests.cs` |
| 10 | Integration test přes `WebApplicationFactory` (jednoduchý end-to-end „archived z GET se zobrazí s includeArchived=true") | 0.15 h | existující integration harness |
| 11 | `CLAUDE.md` + Notion update | 0.15 h | — |

**Celkem: ~3 h.**

## 4. Architektonická rozhodnutí

1. **Denní tick místo měsíčního.** Požadavek mluví o "monthly archive", ale průběžné denní skenování lépe rozkládá zátěž (malé batche, žádný náhlý pick). Kritéria (age ≥ 365d AND traceCount ≤ 1) jsou monotónní, takže denní běh je idempotentní. `IntervalHours = 24` default; user může zvýšit bez změny kritéria.
2. **Bulk update v repozitáři, ne per-row.** `ExecuteUpdateAsync` (EF Core 7+) — nevolá `SaveChanges`, jeden SQL UPDATE. Žádná domain entity instance, žádný domain event. `BatchSize` chrání SQL Serverless proti dlouhému transaction logu.
3. **Žádný `ArchiveEvent` domain event.** Archival je interní údržba. Pokud by v budoucnu nějaká komponenta potřebovala vědět o archivaci (např. analytics), přidá se samostatný signal. Teď bychom jen navýšili spam v ChangeFeedu.
4. **Auto-unarchive v `CkbPersistenceService`.** Je to jediný bod, kde se potvrzuje "profil je znovu použit". `EntityResolver` pouze rozhoduje identitu — archived i active se liší jen v zobrazení, pro resolve jde o stejnou entitu. Unarchive před `UpsertAsync`, aby se změna fyzicky zapsala jednou transakcí spolu s `TraceCount++` a `OverallConfidence`.
5. **Cutoff počítat v `Clock` seamu.** Testovatelné (stejně jako `RevalidationScheduler`). `Clock` = `() => DateTimeOffset.UtcNow` default; testy overridují.
6. **Logování PII-free.** Stejný pattern jako `RevalidationScheduler`: jen počty, `ex.GetType().Name`, žádný `LegalName` / `NormalizedKey`. GUID profilů je OK (stabilní, bez PII).
7. **Odolnost vůči retry:** jelikož `IsArchived` je idempotentní (re-aplikování `= true` je no-op), tick může proběhnout dvakrát bez efektu.
8. **Interakce s re-validation queue:** archivované profily jsou filtrovány v `GetRevalidationQueueAsync`. Manuální fronta (`IRevalidationQueue`) v principu dovoluje enqueue archivovaného ID (endpoint jen kontroluje `ExistsAsync`). To je záměr — pokud admin chce manuálně re-validovat archivovaný profil, runner to zkusí (a `NeedsRevalidation()` může vrátit false → Skipped; nic neselže). Pokud se v budoucnu ukáže, že chceme explicitní 404 na archived, doplníme; pro B-83 zachovávám současné chování.

## 5. Datové modely a API kontrakty

### Nová konfigurace (`Archival` sekce v `appsettings.json`)

```jsonc
"Archival": {
  "Enabled": true,
  "IntervalHours": 24,
  "MinAgeDays": 365,
  "MaxTraceCount": 1,
  "BatchSize": 500
}
```

### Nové signatury (Domain)

```csharp
// ICompanyProfileRepository
Task<int> ArchiveStaleAsync(
    DateTimeOffset enrichedBefore,
    int maxTraceCount,
    int batchSize,
    CancellationToken cancellationToken = default);
```

### Nové metriky

- `tracer.ckb.archived` (Counter<long>, tag `trigger = "auto"`)
- `tracer.ckb.unarchived` (Counter<long>, tag `trigger = "trace"`)

### API / DTO změny

Žádné nové endpointy ani breaking changes v DTO. `CompanyProfileDto.IsArchived` už existuje, `ListProfilesQuery.IncludeArchived` už existuje.

## 6. Testovací strategie

### Unit

- `ArchivalServiceTests`:
  - disable → service nerunuje tick,
  - happy path → volá repo s cutoff = Clock() − MinAge, zapíše metriku,
  - repo throw → zachytí, zaloguje, nespadne,
  - cancellation → vystoupí bez metriky.
- `CkbPersistenceServiceTests`:
  - archived profile → po `PersistEnrichmentAsync` je `IsArchived == false`, metric `RecordCkbUnarchived` volán jednou,
  - active profile → unarchive metric NENÍ volán.
- `CompanyProfileRepositoryTests` (InMemory / SQLite):
  - `ArchiveStaleAsync` — cutoff hranice (před / po),
  - nezasáhne `TraceCount > 1`,
  - nezasáhne `LastEnrichedAt == null` (bez enrichmentu neznáme stáří),
  - už archivované ignoruje v počtu (vrací jen nově změněné).

### Integration

- `GET /api/profiles?includeArchived=true` vrátí archived profil (smoke přes `WebApplicationFactory`).
- `GET /api/profiles/{id}/revalidate` (B-65 queue) na archived id → dostane `Accepted` (žádná regrese).

## 7. Akceptační kritéria

- [ ] `dotnet build` + `dotnet test` zelené (všechny existující testy + nové).
- [ ] Background job archivuje testovací profil s `TraceCount==1` a `LastEnrichedAt == Now - 400d`.
- [ ] Po novém trace na archivovaný profil je `IsArchived == false` a `TraceCount == 2`.
- [ ] `GET /api/profiles` (default) nevrátí archived; `?includeArchived=true` vrátí.
- [ ] Re-validation sweep přeskočí archived (ověřeno v `RevalidationSchedulerTests` — triviální, `GetRevalidationQueueAsync` to už dělá).
- [ ] Metriky `tracer.ckb.archived` a `tracer.ckb.unarchived` exportovány (ověření přes `MeterListener` v testech).
- [ ] Žádné nové CVE High/Critical, žádný hardcoded secret, žádný PII v lozích.

## 8. Rizika a mitigace

| Riziko | Mitigace |
|--------|----------|
| `ExecuteUpdateAsync` nespustí domain event / concurrency check | Žádný domain event není navržen; optimistic concurrency na `CompanyProfile` nepoužíváme, takže není co zlomit. |
| Dlouhá transakce při velkém batch | `BatchSize = 500` + `TOP 500` klauzule (přes `.Where(...).Take(500).ExecuteUpdateAsync(...)`); opakovat dokud vrací > 0. |
| Unarchive race (dva paralelní trace na stejný archived profil) | `CkbPersistenceService` je Scoped + `DbContext` je Scoped, `Unarchive()` je idempotentní, outer transakce vyřeší konflikt přes last-wins. |
| Hodně profilů splňuje kritérium při prvním běhu | První tick archivuje dávky po 500 dokud nedojdou; měřitelná doba v observability. |
