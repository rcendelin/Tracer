# B-67 — Re-validation deep mode

**Fáze:** 3 — AI + scraping
**Odhad:** 3 h
**Branch:** `claude/eloquent-babbage-psTo6`
**Datum zahájení:** 2026-04-23

## 1. Cíl bloku

Nahradit `NoOpRevalidationRunner` reálným `DeepRevalidationRunner`, který pro profily s výrazně zastaralými poli spustí plnou waterfall re-enrichmentovou pipeline. Lightweight mode (B-66) je paralelní blok na jiné větvi; tato implementace ho **nevyžaduje** — deep runner aktivuje plný waterfall výhradně na základě počtu expirovaných polí, zbytek delegace na lightweight zůstává otevřen a runner v tom případě vrací `Deferred` dokud B-66 nedoplní druhou větev.

Konkrétně:

1. **Trigger:** `≥ DeepThreshold` expirovaných polí (default 3) spouští deep mode. Méně expirovaných polí → `Deferred` (B-66 lightweight je doplní; do té doby scheduler běží idempotentně bez re-enrichmentu, což je stejné chování jako dnes).
2. **Feasibility gate:** profil musí mít `RegistrationId + Country` (nebo dostatek identifikačních polí) aby se dal znovu protrasovat waterfallem. Jinak `Deferred` s WARN logem (ne `Failed` — chybí vstup, ne chyba).
3. **Full waterfall:** vytvoří syntetický `TraceRequest` s `Source = "revalidation"`, `Depth = Standard` a spustí stávající `IWaterfallOrchestrator`. Orchestrator interně upsertuje profil, detekuje změny a persistuje přes `CkbPersistenceService`.
4. **Audit:** zapíše `ValidationRecord` (`ValidationType.Deep`) po dokončení. `LastValidatedAt` na profilu se aktualizuje přes `CompanyProfile.MarkValidated()`.
5. **Telemetrie:** metrika `tracer.revalidation.duration` tag `trigger = "auto" | "manual"` už existuje v scheduleru; deep runner jen přidá strukturovaný log a počítá `FieldsChanged` přes `IChangeEventRepository.CountByProfileAsync` delta před/po orchestrátoru.

## 2. Kontext v codebase

- `IRevalidationRunner.RunAsync(CompanyProfile, CancellationToken)` je už zavedený kontrakt z B-65. Docstring říká "must NOT call SaveChangesAsync" — pro deep mode je tento pokyn architektonicky neudržitelný, protože `WaterfallOrchestrator → CkbPersistenceService` už `IUnitOfWork.SaveChangesAsync` volá. Deep runner na to bude navazovat a po orchestrátoru provede jeden dodatečný `SaveChangesAsync` pro `ValidationRecord + MarkValidated + TraceRequest.Complete` (atomická transakce v rámci scope scheduler-per-profile).
- `IFieldTtlPolicy` (B-68) umí `GetExpiredFields(profile, now)` — tohle je jediný zdroj pravdy pro trigger.
- `WaterfallOrchestrator.ExecuteAsync(TraceRequest, CancellationToken)` už zpracovává TracedField merge + change detection + persistence. Nemusí se duplikovat.
- `CkbPersistenceService.PersistEnrichmentAsync` persistuje profil, change eventy, mazaje cache a volá `IUnitOfWork.SaveChangesAsync` (takže `LastEnrichedAt` i změny se uloží). `TraceCount` inkrementuje — re-validace se započítá stejně jako normální trace (ok, `TraceCount` je i měřítko aktivity).
- `ValidationRecord(companyProfileId, validationType, fieldsChecked, fieldsChanged, providerId, durationMs)` — už existuje z Domain.
- `CompanyProfile.MarkValidated()` nastaví `LastValidatedAt = DateTimeOffset.UtcNow`.
- `CompanyProfile` má pro deep trasing dostatek polí pro obnovení: `LegalName?.Value`, `TaxId?.Value`, `RegistrationId`, `Country`, `RegisteredAddress?.Value`, `Phone?.Value`, `Email?.Value`, `Website?.Value`, `Industry?.Value`. Syntetický `TraceRequest` použije to, co je k dispozici.
- `RevalidationScheduler` scope: `IServiceScopeFactory.CreateAsyncScope()` per profil. `IRevalidationRunner` je Scoped, tedy v témže scope má přístup k Scoped `ITraceRequestRepository`, `IWaterfallOrchestrator`, `IValidationRecordRepository`, `IChangeEventRepository`, `IUnitOfWork`.
- `TraceRequest` konstruktor vyžaduje aspoň jedno identifikační pole; `Depth=Standard` vynechá Tier 3 (AI, drahé). Pro nepravidelnou situaci "profil má `LegalName` ale nemá `RegistrationId` ani `Country`" runner vrací `Deferred` — bez country se waterfall neumí nasměrovat na registr.

## 3. Dekompozice

| # | Úkol | Odhad | Soubory |
|---|------|-------|---------|
| 1 | `DeepRevalidationOptions` — práh (`DeepThreshold`, default 3), provider ID konstanta (`"revalidation"`). | 0.25 h | `Tracer.Application/Services/DeepRevalidationOptions.cs` (nový) |
| 2 | `DeepRevalidationRunner : IRevalidationRunner` — logika triggeru, syntéza TraceRequestu, orchestrace, audit, guards. | 1.25 h | `Tracer.Application/Services/DeepRevalidationRunner.cs` (nový) |
| 3 | DI přepojení — v `ApplicationServiceRegistration.cs` zaregistrovat `DeepRevalidationRunner` namísto `NoOpRevalidationRunner` (a z testů zachovat `NoOpRevalidationRunner` pro placeholder-style testy). | 0.1 h | `Tracer.Application/DependencyInjection.cs` |
| 4 | `appsettings.json` — `Revalidation:Deep:Threshold = 3` sekce, bind v `Program.cs` přes `AddOptions<DeepRevalidationOptions>().Bind(...)`. | 0.15 h | `Tracer.Api/Program.cs`, `appsettings.json` |
| 5 | Unit testy pro `DeepRevalidationRunner` — trigger práh, feasibility gate, šťastná cesta, cancellation, error handling. | 0.9 h | `tests/Tracer.Application.Tests/Services/DeepRevalidationRunnerTests.cs` |
| 6 | Aktualizace `CLAUDE.md` — konvence deep runneru a jeho interakce s `IRevalidationRunner` kontraktem. | 0.2 h | `CLAUDE.md` |
| 7 | Aktualizace Notion statusu + commit + push | 0.15 h | N/A |

**Celkem: ~3 h.**

## 4. Architektonická rozhodnutí

1. **Runner vs. scheduler SaveChanges.** B-65 doc říká "runner nemá volat `SaveChangesAsync`". V deep mode je to nesplnitelné — `IWaterfallOrchestrator.ExecuteAsync` interně ukládá profil i změny (přes `CkbPersistenceService`). Dosavadní pravidlo platí pouze pro lightweight (B-66), kde runner pouze mutuje profil a scheduler ukládá; pro deep mode runner explicitně provádí 2 save checkpointy: (a) `TraceRequest` v `InProgress` stavu, aby měl ID pro audit, (b) orchestrator → vlastní save, (c) `ValidationRecord + MarkValidated + TraceRequest.Complete` ve finálním save. Cena za konzistenci s existujícím flow.
2. **`Source = "revalidation"`.** TraceRequest.Source udává původ; `"revalidation"` je existující dokumentovaná hodnota. UI a Notion reporty ho pak mohou filtrovat.
3. **`Depth = Standard`.** Deep TraceDepth (B-58 waterfall) by spustil i AI extrakci, což je cenově nepříjemné a pro pouhou re-validaci nadbytečné. `Standard` pokryje Tier 1 (registry) + Tier 2 (scraping), což odpovídá ambici "full waterfall re-enrichment without AI".
4. **Práh `DeepThreshold = 3`.** Mapuje Notion specifikaci ("≥ 3 expired fields"). Konfigurovatelné — testovací prostředí může snížit na 1 pro deterministické fixtures, produkce ponechá výchozí hodnotu.
5. **Idempotence při `< DeepThreshold`.** Runner vrací `Deferred` → scheduler profil skipne → stav se neposouvá. Při dalším ticku se profil opět objeví v `GetRevalidationQueueAsync`, pokud TTL nadále expirovaný. Až B-66 lightweight bude na develop, doplní se druhá větev (return `Lightweight`). Do té doby je deferred běh idempotentní (podobně jako dnes s `NoOpRevalidationRunner`).
6. **`ProviderId = "revalidation-waterfall"` ve `ValidationRecord`.** Speciální ID — odlišuje revalidační provider ID od konkrétního enrichment provideru (AResse / Companies House / ...). Kompatibilní s existujícím field `ValidationRecord.ProviderId` (string). Budoucí detailnější audit může rozparsovat konkrétní providery z `SourceResults` orchestrátoru; pro Phase 3 stačí agregátní.
7. **`FieldsChanged` počítáno via `IChangeEventRepository.CountByProfileAsync`.** Před orchestrátorem zjistíme `countBefore`, po orchestrátoru `countAfter`; rozdíl = kolik změnových eventů toto deep re-enrichment vyprodukovalo. `FieldsChecked` = počet polí, která orchestrator reálně vytvořil + aktualizoval = odhad přes `expiredFields.Count` (tj. pole, která scheduler chtěl ověřit). Toto je přesné v praxi — orchestrator zkusí znovu všechna pole, ale pro audit je relevantní kolik jich bylo cíleně `expired`.
8. **Per-profile atomicity.** Všechny operace (TraceRequest životní cyklus, profile upsert + changes přes orchestrator, ValidationRecord) běží v jednom `IServiceScope` (vytvořeném ve scheduleru). EF Core se stará o transakční jednotku. Při výjimce uprostřed se TraceRequest přejde do `Failed` stavu via catch block, ValidationRecord se neuloží, profile změny od orchestrátoru zůstanou (už je saveloval). To je přijatelné: orchestrator sám persistuje nezávisle na úspěchu navazujících kroků (stejně jako produkční flow `SubmitTraceHandler`).
9. **Cancellation semantika.** Scheduler přes per-profile linked CTS (timeout 5 min) — deep runner ho jen propaguje do orchestratoru. Orchestrator sám má depth budget 15s (Standard) + Tier per-provider timeouty. Nepotřebujeme tedy v runneru další CTS.
10. **Security.** Re-validation běží bez client-facing vstupu — syntetický TraceRequest čerpá z **důvěryhodných** profilů v CKB. Žádné nové attack surface.
11. **PII.** Profil v CKB může obsahovat PII (officers se neukládá do TracedField, ale LegalName / Address / Phone / Email jsou osobní / firmoidní data). `ValidationRecord` nezapisuje PII — jen ID profilu a počty. Syntetický `TraceRequest` však **obsahuje** PII v `CompanyName`, `Phone`, `Email` atd. — tyto hodnoty jsou OK v databázi TraceRequests (už dnes se tam ukládají z REST API), ale **nesmí jít do logů**. Runner bude logovat pouze `profileId`, `expiredCount`, `traceRequestId`, duration.

## 5. Datové modely a API kontrakty

### Nový typ `DeepRevalidationOptions` (Application)

```csharp
public sealed class DeepRevalidationOptions
{
    public const string SectionName = "Revalidation:Deep";

    /// <summary>
    /// Minimální počet expirovaných polí, který spustí plný waterfall.
    /// Pod touto hodnotou runner vrací Deferred a předpokládá lightweight mode (B-66).
    /// </summary>
    public int Threshold { get; init; } = 3;
}
```

### Žádná změna veřejného API

- Endpoint `POST /api/profiles/{id}/revalidate` beze změny.
- Schéma `TraceRequest` / `CompanyProfile` beze změny.
- Service Bus message schéma beze změny.

### Konfigurace

```jsonc
"Revalidation": {
  "Enabled": true,
  "IntervalMinutes": 60,
  "MaxProfilesPerRun": 100,
  "Deep": {
    "Threshold": 3
  },
  "OffPeak": { ... },
  "FieldTtl": { ... }
}
```

## 6. Testovací strategie

### Unit tests (`tests/Tracer.Application.Tests/Services/DeepRevalidationRunnerTests.cs`)

| # | Scénář | Ověření |
|---|--------|---------|
| 1 | Profil bez expirovaných polí | `RevalidationOutcome.Deferred`, orchestrator nespuštěn |
| 2 | Profil s 1 expirovaným polem a `Threshold = 3` | `Deferred`, orchestrator nespuštěn |
| 3 | Profil s 3+ expirovanými poli, má `RegistrationId + Country` | `Deep`, orchestrator spuštěn 1×, ValidationRecord uložen s type=Deep, profile.LastValidatedAt nastaven |
| 4 | Profil s 3+ expirovanými poli, **bez** `RegistrationId` ani `Country` | `Deferred`, WARN log "not enough identifying fields" |
| 5 | Syntetický TraceRequest má Source=`"revalidation"`, Depth=`Standard` | Verifikace přes `.Received().ExecuteAsync(Arg.Is<TraceRequest>(r => r.Source == "revalidation" && r.Depth == TraceDepth.Standard))` |
| 6 | Orchestrator hodí výjimku | `RevalidationOutcome.Failed`, TraceRequest.Status = Failed, bez ValidationRecord |
| 7 | Předčasné zrušení (cancellation token) | Propagace `OperationCanceledException` |
| 8 | FieldsChanged = delta ChangeEventRepository.CountByProfileAsync (3 before → 7 after → 4 changed) | Ověření argumentu `ValidationRecord.FieldsChanged == 4` |
| 9 | Konfigurovatelný `Threshold = 1` (testovací fixture) | Profil s 1 expirovaným polem spustí deep mode |
| 10 | Null profile argument | `ArgumentNullException` |

### Integration (follow-up, mimo scope B-67)

Plné integration testy deep mode (mock orchestrator + in-memory DbContext) by ověřily interakci se scheduler + persistence vrstvou. Tento blok spoléhá na unit testy (substituovaný orchestrator) a existující `WaterfallOrchestratorTests` + `CkbPersistenceServiceTests`.

## 7. Akceptační kritéria

1. `DeepRevalidationRunner` je zaregistrován jako `IRevalidationRunner` v DI; `NoOpRevalidationRunner` zůstává jako fallback pro jednotkové testy.
2. Deep run se spustí pouze pokud `GetExpiredFields(profile, now).Count >= Threshold` **a** profil má dostatek identifikačních polí pro re-trace (`RegistrationId + Country`).
3. Při spuštění deep run vytvoří `TraceRequest` s `Source="revalidation"`, `Depth=Standard`, a zavolá `IWaterfallOrchestrator.ExecuteAsync`.
4. Po úspěšném orchestratoru runner:
   - volá `profile.MarkValidated()`,
   - vytvoří `ValidationRecord` s `ValidationType.Deep`, `FieldsChecked = expiredCount`, `FieldsChanged = Δ ChangeEvents`, `ProviderId = "revalidation-waterfall"`, `DurationMs = duration`,
   - volá `traceRequest.Complete(profile.Id, profile.OverallConfidence ?? Zero)`,
   - uloží vše přes `IUnitOfWork.SaveChangesAsync`.
5. Při výjimce nebo timeout orchestratoru runner volá `traceRequest.Fail(reason)` a vrací `RevalidationOutcome.Failed`.
6. Unit testy všechny zelené.
7. Žádné PII v logeru (pouze GUIDy, počty, timingy).
8. CLAUDE.md doplněn o deep-mode sekci.
9. Commity atomic + conventional.
10. `CompanyProfile.NeedsRevalidation()` zůstává v Domain nedotčen (pravidlo B-68: Application callers musí jít přes `IFieldTtlPolicy`).

## 8. Rizika a otevřené otázky

1. **B-66 lightweight mode není na develop.** Až se B-66 doplní, scheduler bude mít jen jeden `IRevalidationRunner` → potřeba kompozitního runneru (lightweight + deep). Řešení: v budoucnu přidat `CompositeRevalidationRunner` který rozhodne mezi lightweight/deep; pro tento blok stačí deep + deferred pro malý počet expirovaných.
2. **`TraceCount` se inkrementuje.** Re-validace se projeví jako aktivita profilu. Je to přijatelné — TraceCount má pouze prioritizační význam pro `GetRevalidationQueueAsync` a ten je v pořádku (často re-validovaný = častěji trasovaný v CKB logice).
3. **Cena Standard waterfallu.** Depth=Standard spustí Tier 1 (API registry calls) + Tier 2 (scraping). Respektuje se rate limit providerů (`Handelsregister 60/h`, `State SoS 20/min`, atd.). Default `MaxProfilesPerRun = 100` znamená max 100 × Standard runs / hodinu — v reálu je to dolní 10ky profilů s `≥ 3 expired fields`.
4. **`CkbPersistenceService` dvojité SaveChanges.** Orchestrator interně ukládá, runner pak ještě jednou. Jde o 2 transakce, ale ta druhá se týká pouze `ValidationRecord + TraceRequest + profile.MarkValidated` — nekonflikuje s orchestrátorovou transakcí (profile už je v DB).

## 9. Follow-upy

- **B-66 lightweight** (v jiné větvi) po mergi bude vyžadovat kompozitní runner nebo úpravu `DeepRevalidationRunner` na větvení (`if lightweight-applicable → lightweight`, `else if deep-applicable → deep`). Doporučení: oddělit `ILightweightRevalidator` + `IDeepRevalidator` a přidat `CompositeRevalidationRunner` při integraci.
- **DbContext integration test** pro end-to-end deep re-validation — lze přidat do `Tracer.Infrastructure.Tests/Integration/` až bude DbContext harness dostupný (sdílený follow-up s B-71, B-83, B-84).
- **Metrika deep-mode triggering:** dnes scheduler měří `tracer.revalidation.{processed,skipped,failed}` s tagem `trigger=auto|manual`. Deep vs. lightweight rozlišení přijde až s B-66, aby dashboard uměl separovat počty.
