# B-74 — Service Bus topic setup

**Fáze:** 3 — AI + scraping
**Odhad:** 3 h
**Zahájeno:** 2026-04-21
**Branch:** `claude/upbeat-planck-Fx5mO`

## Cíl (ze zadání)

Finalizovat topic-based distribuci `ChangeEventMessage` přes Azure Service Bus tak, aby:

1. topic `tracer-changes` měl dvě subscriptions — **`fieldforce-changes`** (Critical/Major only) a **`monitoring-changes`** (vše kromě Cosmetic),
2. publisher publikoval i **Major** a **Minor** severity (ne pouze Critical jako dnes), aby filtry měly co routovat,
3. dead-letter chování bylo explicitně zapnuté (expirace i filter evaluation failures),
4. byl pokrytý unit + integration test na routing severity → subscription matching.

Akceptační kritéria z Notion: *Critical change → message na obou subscriptions, Minor change → jen monitoring.*

## Výchozí stav

| Komponenta | Stav | Soubor |
|---|---|---|
| Topic `tracer-changes` | ✅ existuje | `deploy/bicep/modules/service-bus.bicep` |
| Subscription `fieldforce-changes` | ✅ existuje s SQL filterem `Severity='Critical' OR Severity='Major'` | tamtéž |
| Subscription `monitoring-changes` | ❌ chybí | — |
| DLQ `deadLetteringOnMessageExpiration` | ✅ true na `fieldforce-changes` | tamtéž |
| `enableDeadLetteringOnFilterEvaluationExceptions` | ❌ neexplicitní | tamtéž |
| Publish Critical | ✅ `CriticalChangeNotificationHandler` | `src/Tracer.Application/EventHandlers/` |
| Publish Major | ❌ pouze SignalR | `FieldChangedNotificationHandler` |
| Publish Minor | ❌ nikam | tamtéž |
| Publish Cosmetic | ❌ jen log (správně) | tamtéž |
| Publisher routing | ✅ `ServiceBusPublisher.PublishChangeEventAsync` + ApplicationProperty `Severity` | `src/Tracer.Infrastructure/Messaging/ServiceBusPublisher.cs` |
| Unit testy Publisher | ✅ `ServiceBusPublisherTests` (pokrývá všechny severity) | `tests/Tracer.Infrastructure.Tests/Messaging/` |
| Unit testy Handler | ⚠️ pokrývá jen SignalR, ne SB publish | `FieldChangedNotificationHandlerTests` |

## Dekompozice

| # | Subtask | Odhad | Typ |
|---|---|---|---|
| 1 | Bicep: `monitoring-changes` subscription (1=1 implicit default rule, DLQ) | 20 min | infra |
| 2 | Bicep: `enableDeadLetteringOnFilterEvaluationExceptions=true` na `fieldforce-changes` | 10 min | infra |
| 3 | Bicep: explicitní `maxDeliveryCount` + DLQ flags na `monitoring-changes` (větší retry tolerance) | 10 min | infra |
| 4 | Rozšířit `FieldChangedNotificationHandler` o Service Bus publish pro Major + Minor | 45 min | code |
| 5 | DI: refaktor tak, aby handler získal `IServiceBusPublisher` + `ICompanyProfileRepository` (jako Critical handler) | — součástí #4 | code |
| 6 | Unit testy handleru — assertions per severity (publish vs. skip, SignalR vs. skip) | 30 min | test |
| 7 | Integration test: routing semantika (SQL filter predikát) — in-memory evaluator | 30 min | test |
| 8 | Guard: zabránit double-publish pro Critical (Critical handler + field handler) | — součástí #4 | code |
| 9 | CLAUDE.md: zdokumentovat monitoring subscription + severity publish matrix | 15 min | docs |

## Ovlivněné komponenty / moduly

### Backend (C#)
- `src/Tracer.Application/EventHandlers/FieldChangedNotificationHandler.cs` — změna kontraktu DI (+ Service Bus publisher + Company profile repository).
- `tests/Tracer.Application.Tests/EventHandlers/FieldChangedNotificationHandlerTests.cs` — rozšířené fakes a asserce.

### Infrastructure as Code
- `deploy/bicep/modules/service-bus.bicep` — `monitoring-changes` subscription, DLQ flags.

### Testy
- `tests/Tracer.Application.Tests/EventHandlers/FieldChangedNotificationHandlerTests.cs` (unit).
- `tests/Tracer.Application.Tests/EventHandlers/ChangeEventRoutingTests.cs` (nový — integration-style na level filter evaluator).

## Datové modely a API kontrakty

**Bez změn.** `ChangeEventMessage`, `ChangeEventContract`, `FieldChangedEvent`, `CriticalChangeDetectedEvent`, `IServiceBusPublisher` zůstávají identické. Routing je plně driven ApplicationProperties `Severity` (string) + SQL filter na subscription.

## Severity → publish matrix (po B-74)

| Severity | Service Bus publish? | SignalR push? | Log? | fieldforce-changes | monitoring-changes |
|---|---|---|---|---|---|
| Critical | ✅ (Critical handler) | ✅ (Critical handler) | ✅ | ✅ (match) | ✅ (match) |
| Major | ✅ (Field handler) | ✅ (Field handler) | ✅ | ✅ (match) | ✅ (match) |
| Minor | ✅ (Field handler) | ❌ | ✅ | ❌ (filter drops) | ✅ (match) |
| Cosmetic | ❌ | ❌ | ✅ | — | — |

Klíčové pravidlo: **Cosmetic není v topicu.** Cosmetic pokrývá confidence updates a formatting changes — objem je velký, přínos pro downstream konzumenty nulový, zápis do Service Bus by vedl k neúměrnému ingressu bez business hodnoty.

## Architektonické rozhodnutí: split handlers vs. merge

Zvažoval jsem sjednocení `CriticalChangeNotificationHandler` a `FieldChangedNotificationHandler` do jednoho severity-dispatch handleru (jako v B-75). Ponechávám je **oddělené**:

1. Critical raises dva domain events (`FieldChangedEvent` + `CriticalChangeDetectedEvent`). Merge by vyžadoval odstranění Critical duplicitního eventu — to by byla breaking change Domain layer a zbytečná.
2. Separate handlers = single responsibility (Critical má warning-level log, Field má info-level).
3. B-75 plánuje další rozšíření (retry, batch) — refaktor do jednoho handleru patří tam, ne sem.

Handlery si mezi sebou předávají hranici přes podmínku `if (notification.Severity == Critical) return` ve `FieldChangedNotificationHandler` — komentář vysvětlí důvod.

## Testovací strategie

### Unit tests (Application.Tests)

`FieldChangedNotificationHandlerTests` rozšířit:
- `Handle_Critical_SkipsBothPublishers` — ani SB publish, ani SignalR.
- `Handle_Major_PublishesToServiceBusAndSignalR` — obojí.
- `Handle_Minor_PublishesToServiceBusOnly` — SB ano, SignalR ne.
- `Handle_Cosmetic_SkipsBothPublishers`.
- `Handle_ProfileNotFound_LogsAndSkipsPublish` — edge case.
- `Handle_NullNotification_Throws`.

Fake `IServiceBusPublisher` + `ICompanyProfileRepository` + `ITraceNotificationService` přes NSubstitute.

### Integration-style unit test (routing semantika)

`ChangeEventRoutingTests`:
- Simuluje SQL filter `Severity='Critical' OR Severity='Major'` jako predikát `(string severity) => severity == "Critical" || severity == "Major"`.
- Pro každou severity zavolá publisher (přes in-memory stub zachycující `ApplicationProperties`) a aplikuje filter — ověří, že fieldforce subscription dostane jen Critical/Major, monitoring dostane všechny 4 severities.
- Bez reálného Service Bus — jen verifikuje, že publisher nastavuje `ApplicationProperties["Severity"]` konzistentně s filterem v Bicep.

### E2E (mimo scope B-74)

Skutečný E2E test proti Service Bus Emulator / Azure nespouštíme v tomto bloku. Patří do B-76 (Integration tests Phase 3) a B-77 (E2E Deep flow).

## Akceptační kritéria

1. ✅ `dotnet build` prochází bez warnings.
2. ✅ `dotnet test` — všechny testy zelené.
3. ✅ Bicep `what-if` (dry) projde validation; soubory lintují.
4. ✅ `FieldChangedNotificationHandlerTests` pokrývá 4 severity × 2 kanály = explicitně 8 assercí.
5. ✅ Unit test ověřuje, že publisher nastavuje `ApplicationProperties["Severity"]` přesně podle `enum.ToString()`, které odpovídá SQL filter v Bicep.
6. ✅ Routing matrix v Severity tabulce v CLAUDE.md odpovídá implementaci.
7. ✅ DLQ nastavení auditovatelné v Bicep diff.
8. ✅ Žádný regression v existujících testech (Publisher + Critical handler).

## Follow-upy

- **B-75**: detailní refactor notification handlerů (retry, batch); může mergovat oba handlery do jednoho severity-dispatch pipeline.
- **Observabilita**: metrika `tracer.change.published` s tagy `severity`, `subscription_matched` — patří do B-78 (Deployment Phase 3 + performance).
- **Service Bus Emulator**: lokální dev-time subscription matching — patří do B-76.
