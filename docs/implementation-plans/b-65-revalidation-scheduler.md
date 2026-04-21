# B-65 — Re-validation scheduler

**Fáze:** 3 — AI + scraping
**Odhad:** 4 h
**Branch:** `claude/upbeat-planck-7TUcr`
**Datum zahájení:** 2026-04-21

## 1. Cíl bloku

Zprovoznit periodický background scheduler, který z CKB vybírá `CompanyProfile` záznamy s expirovanými TTL poli a zařazuje je do re-validační fronty. Samotný mechanismus re-validace (lightweight / deep) bude doplněn v blocích B-66 a B-67 — tento blok řeší:

1. výběr a prioritizaci kandidátů,
2. hodinový cyklus s off-peak oknem,
3. hook `IRevalidationRunner` pro budoucí implementaci,
4. observability (metriky + logy),
5. konfiguraci přes `Revalidation` sekci (už existuje v `appsettings.json`).

## 2. Kontext v codebase

- `CompanyProfile.NeedsRevalidation()` kontroluje TTL přes `FieldTtl.For(field)`.
- `ICompanyProfileRepository.GetRevalidationQueueAsync(maxCount, ct)` už dodává kandidáty — řadí `TraceCount DESC`, `LastValidatedAt ASC`.
- `ValidationRecord` entita + `IValidationRecordRepository` jsou hotové (B-65+ je bude používat pro audit, ale zápis zatím nebude prováděn — to je úkol B-66/B-67).
- `CompanyProfile.MarkValidated()` už existuje — budeme volat po úspěšném dokončení.
- `appsettings.json` obsahuje sekci `Revalidation` (`Enabled`, `IntervalMinutes`, `MaxProfilesPerRun`, `FieldTtl:*`). Tento blok ji rozšíří o `OffPeakEnabled`, `OffPeakStartHourUtc`, `OffPeakEndHourUtc`.
- `ITracerMetrics` se rozšíří o `RecordRevalidationRun(...)`.
- `ProfileEndpoints.RevalidateProfileAsync` dnes vrací jen 202 bez akce — po B-65 bude enqueuovat profil do jednorázového běhu scheduleru (fire-and-forget přes `IRevalidationQueue`).

## 3. Dekompozice

| # | Úkol | Odhad | Soubory |
|---|------|-------|---------|
| 1 | `RevalidationOptions` (bind z `Revalidation:` sekce) + validátor | 0.25 h | `Tracer.Application/Services/RevalidationOptions.cs` |
| 2 | `IRevalidationRunner` interface — stub, který přijímá profile + expired fields | 0.25 h | `Tracer.Application/Services/IRevalidationRunner.cs`, `NoOpRevalidationRunner.cs` |
| 3 | `IRevalidationQueue` (interní, singleton) pro manuální enqueue z `POST /revalidate` | 0.25 h | `Tracer.Application/Services/IRevalidationQueue.cs`, `RevalidationQueue.cs` |
| 4 | `ITracerMetrics.RecordRevalidationRun(...)` — histogramy + counter; impl v `TracerMetrics` | 0.25 h | `ITracerMetrics.cs`, `TracerMetrics.cs` |
| 5 | `RevalidationScheduler : BackgroundService` — hodinový loop, off-peak gate, selekce, metrika, logy | 1.25 h | `Tracer.Infrastructure/BackgroundJobs/RevalidationScheduler.cs` |
| 6 | Off-peak utility + testování (injekce Clock) | 0.25 h | tamtéž, `internal Func<DateTimeOffset> Clock { get; init; }` |
| 7 | DI registrace v `InfrastructureServiceRegistration` + podmínka `Revalidation:Enabled` | 0.25 h | `InfrastructureServiceRegistration.cs` |
| 8 | `POST /api/profiles/{id}/revalidate` — napojení na `IRevalidationQueue` | 0.25 h | `ProfileEndpoints.cs` |
| 9 | Unit testy `RevalidationScheduler` (off-peak, budget, repo interakce) | 0.75 h | `tests/Tracer.Infrastructure.Tests/BackgroundJobs/` |
| 10 | Unit testy `RevalidationQueue` | 0.25 h | `tests/Tracer.Application.Tests/Services/` |

**Celkem: ~4 h.**

## 4. Architektonická rozhodnutí

1. **Scheduler v Infrastructure, kontrakt v Application.** `IRevalidationRunner` a `IRevalidationQueue` žijí v `Application` (bez EF závislostí), implementace noop v Application. Samotný `BackgroundService` sedí v `Infrastructure` kvůli přímé práci s `IServiceScopeFactory` a DI scope managementu (EF `DbContext` je Scoped, scheduler je Singleton-like). Stejný vzor jako `ServiceBusConsumer`.
2. **`IServiceScopeFactory` místo přímé injekce repository.** `BackgroundService` je singleton; `ICompanyProfileRepository` je scoped → bez scope factory by šlo o captive dependency (už popsané v `CLAUDE.md` u health checků).
3. **`IRevalidationRunner` placeholder vrací `RevalidationOutcome.Deferred`.** Lightweight/deep mód přijde v B-66/B-67 — do té doby scheduler pouze zaloguje, které profily by dnes vybral + zavolá runner, který nic nepodnikne. `CompanyProfile.MarkValidated()` volat **nebudeme**, dokud runner nereportuje úspěch (jinak bychom resetovali `LastValidatedAt` a ztratili informaci o expiraci).
4. **Off-peak okno je měkké.** Pokud je aktuální hodina UTC mimo off-peak a `OffPeakEnabled=true`, scheduler **log + skip** (nebloukuje, nečeká). Další tick (za `IntervalMinutes`) znovu zvažuje podmínku. To je jednodušší než počítat delay do okna a robustnější vůči změnám konfigurace za běhu.
5. **Manuální revalidační fronta** (`IRevalidationQueue`) je in-memory `Channel<Guid>` (bounded, 100 položek). Produkuje ji `POST /revalidate`, konzumuje scheduler na startu každého ticku (v libovolnou hodinu, nezávisle na off-peak — manuální požadavky mají prioritu). Persistentní fronta přijde až ve Phase 4 s Redis.
6. **Jednoduchá paralelizace.** Scheduler zpracovává profily sekvenčně. EF Core `DbContext` není thread-safe a různé scopy v paralelu by mohly přetížit SQL Serverless bez benefitu. Pokud bude budget tlačit, uživatel může buď snížit `MaxProfilesPerRun`, nebo počkat na Phase 4 horizontal scaling.
7. **Idempotence.** `GetRevalidationQueueAsync` nevylučuje nedávno validované — pokud runner nic neudělá (`Deferred`), ve stejné minutě tick znovu vybere stejné profily. Tomu bráníme tím, že `IntervalMinutes` je defaultně 60 min — při Deferred runner ani tak nic nezmění, takže opakovaný výběr je bezpečný.

## 5. Datové modely a API kontrakty

### Nová konfigurace

```jsonc
"Revalidation": {
  "Enabled": true,
  "IntervalMinutes": 60,
  "MaxProfilesPerRun": 100,
  "OffPeak": {
    "Enabled": false,         // false = běh 24/7 (default v dev / Phase 3)
    "StartHourUtc": 22,       // inkluzivní, 0–23
    "EndHourUtc": 6,          // exkluzivní, 0–23 (wrap-around přes půlnoc OK)
  },
  "FieldTtl": { ... }         // beze změny
}
```

### Nové typy (Application)

```csharp
public sealed class RevalidationOptions
{
    public const string SectionName = "Revalidation";
    public bool Enabled { get; init; } = true;
    public int IntervalMinutes { get; init; } = 60;
    public int MaxProfilesPerRun { get; init; } = 100;
    public OffPeakWindow OffPeak { get; init; } = new();
}

public sealed class OffPeakWindow
{
    public bool Enabled { get; init; }
    public int StartHourUtc { get; init; } = 22;
    public int EndHourUtc { get; init; } = 6;
    public bool IsWithin(DateTimeOffset now);
}

internal interface IRevalidationRunner
{
    Task<RevalidationOutcome> RunAsync(CompanyProfile profile, CancellationToken ct);
}

internal enum RevalidationOutcome { Deferred, Lightweight, Deep, Failed }

public interface IRevalidationQueue
{
    ValueTask<bool> TryEnqueueAsync(Guid profileId, CancellationToken ct);
    IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct);
}
```

### API kontrakt

`POST /api/profiles/{id}/revalidate`

- 202 Accepted s textem `"Re-validation for profile {id} queued"`
- 404 Not Found pokud profil neexistuje
- 429 Too Many Requests pokud je manuální fronta plná (Channel bounded 100)

## 6. Testovací strategie

### Unit (Tracer.Application.Tests)

- `OffPeakWindow_IsWithin` — in/out/wrap-around/midnight edge cases.
- `RevalidationOptions` default hodnoty.
- `RevalidationQueue` — enqueue/dequeue, `TryEnqueueAsync` vrátí false při plné frontě.
- `NoOpRevalidationRunner` — vrací Deferred.

### Unit (Tracer.Infrastructure.Tests)

- `RevalidationScheduler_RespectsEnabledFlag` — `Enabled=false` → scheduler se nespustí / loop nedělá nic.
- `RevalidationScheduler_SkipsOutsideOffPeak` — off-peak enabled, aktuální hodina mimo → žádné volání repo.
- `RevalidationScheduler_RunsManualQueueFirst` — manuální enqueue → runner volán i mimo off-peak.
- `RevalidationScheduler_CallsRepositoryWithBudget` — ověřuje, že `GetRevalidationQueueAsync(maxCount)` je volán s config hodnotou.
- `RevalidationScheduler_SkipsProfilesWithoutExpiredFields` — mock repo vrátí profil, který `NeedsRevalidation()==false` → runner nevolán.
- `RevalidationScheduler_LogsRun` — ověřujeme log + metriku.

Scheduler je testovatelný díky injektovanému `Clock` a `IServiceScopeFactory` (mock). Loop je triggerovaný přes `Func<CancellationToken, Task>` delay hook (stejný pattern jako Polly test seams v `CLAUDE.md`).

### Integration (odloženo do B-66/67)

Skutečná E2E validace s providery přijde s lightweight módem (B-66). V B-65 stačí, že scheduler korektně volá `NoOpRevalidationRunner` a metriky jsou vidět v testu.

## 7. Akceptační kritéria

1. ✅ `RevalidationScheduler` je `BackgroundService` registrovaný v DI (pouze při `Revalidation:Enabled=true`).
2. ✅ Každých `IntervalMinutes` (default 60) scheduler:
   - přečte manuální frontu (`IRevalidationQueue`) — vždy,
   - pokud je off-peak enabled a jsme mimo okno → přeskočí auto výběr (manuální frontu ale zpracuje),
   - jinak zavolá `GetRevalidationQueueAsync(MaxProfilesPerRun)`, profily s `NeedsRevalidation()==true` pošle do `IRevalidationRunner`.
3. ✅ Výsledek každého runu je zaznamenaný v `ITracerMetrics.RecordRevalidationRun(processed, skipped, failed, durationMs)` + strukturovaný log.
4. ✅ `POST /api/profiles/{id}/revalidate` vkládá profil do `IRevalidationQueue`, vrátí 202 / 404 / 429.
5. ✅ Build + test prochází: `dotnet build`, `dotnet test` zelené.
6. ✅ Žádný blokující CA warning, žádný new `ArgumentNullException` na veřejném API, žádný captive dependency log.
7. ✅ Security: manuální fronta bounded, žádná hardcoded credentials, logs neobsahují PII.

## 8. Follow-up (nedokončeno v tomto bloku — bude v B-66+)

- Lightweight validation (`LightweightRevalidationRunner`) — nahrazení `NoOpRevalidationRunner`.
- Deep validation (B-67).
- Uložení `ValidationRecord` do DB po každém runu.
- `GET /api/validation/stats` a `GET /api/validation/queue` endpointy.
- Distribuovaná persistentní fronta (Redis, Phase 4).

## 9. Rizika

| Riziko | Mitigace |
|--------|----------|
| Scheduler blokuje app při velkém CKB | `MaxProfilesPerRun` default 100; sekvenční zpracování s per-profile CTS (5 min timeout). |
| Captive dependency (Scoped v Singleton BackgroundService) | `IServiceScopeFactory.CreateAsyncScope()` per tick + per profile. |
| Manuální fronta zaroste v paměti | Bounded Channel(100), `TryEnqueueAsync` vrací false + 429. |
| Off-peak wraparound (22→6 UTC) chybně | Unit testy na hranicích, dokumentované semantiky (`StartHourUtc <= hour < 24 OR 0 <= hour < EndHourUtc`). |
| `Clock` property v testech bez seamu | Stejný pattern jako `HandelsregisterClient.Clock`. |
