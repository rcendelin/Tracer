# B-73 — React ChangeFeed enhanced

Branch: `claude/upbeat-planck-IlAXI`
Fáze: 3 — AI + scraping
Odhad: 3 h

## 1. Cíl

Rozšířit `ChangeFeedPage` (vzniklou v B-48) o funkce, které ji posouvají
od pasivního výpisu k operačnímu nástroji pro monitoring rizikových změn:

- Přehledové statistiky za poslední 7 dní (Critical, Major).
- Drill-down modal s barvicím před/po JSON pohledem místo inline diffu.
- Acknowledge akce pro Critical změny (`IsNotified = true`).
- Export filtrovaných změn do CSV.
- Vizuální zvýraznění nových událostí přicházejících přes SignalR.

## 2. Ovlivněné komponenty

Backend:

- `Tracer.Application/Queries/GetChangeStats/` — `GetChangeStatsQuery` rozšířit o `Since`.
- `Tracer.Domain/Interfaces/IChangeEventRepository.cs` — `CountAsync` doplnit o `since`.
- `Tracer.Infrastructure/Persistence/Repositories/ChangeEventRepository.cs` — přidat filtr podle `DetectedAt >= since`.
- `Tracer.Application/Commands/AcknowledgeChange/` — nový command + handler.
- `Tracer.Api/Endpoints/ChangesEndpoints.cs` — `POST /api/changes/{id}/acknowledge`, `GET /api/changes/export.csv`, rozšířený `stats`.

Frontend:

- `src/Tracer.Web/src/api/client.ts` — `stats(since)`, `acknowledge(id)`, `exportCsv(params)`.
- `src/Tracer.Web/src/hooks/useChanges.ts` — nové hooky `useChangeStatsSince`, `useAcknowledgeChange`, `useChangeHighlights`.
- `src/Tracer.Web/src/pages/ChangeFeedPage.tsx` — stats header, modal, tlačítka Acknowledge / Export.
- `src/Tracer.Web/src/components/ChangeDiffModal.tsx` — nový komponent.
- `src/Tracer.Web/src/types/index.ts` — nic nového (reuse `ChangeStats`, `ChangeEvent`).

## 3. Datové modely a API kontrakty

### 3.1 `GetChangeStats` query

```csharp
public sealed record GetChangeStatsQuery : IRequest<ChangeStatsDto>
{
    /// Filtr "od" na DetectedAt (≥ since). Null = bez filtru (historie).
    public DateTimeOffset? Since { get; init; }
}
```

`ChangeStatsDto` zůstává beze změny. Samostatná HTTP volání dávají „all-time"
i „this week" výsledek.

### 3.2 Acknowledge command

```csharp
POST /api/changes/{id}/acknowledge
→ 204 No Content (idempotentní)
→ 404 Not Found pokud event neexistuje
```

`AcknowledgeChangeCommand(Guid ChangeEventId) : IRequest<AcknowledgeResult>`.
Handler:

1. Načte `ChangeEvent` přes `IChangeEventRepository.GetByIdAsync`.
2. Pokud null → vrátí `AcknowledgeResult.NotFound`.
3. Zavolá `changeEvent.MarkNotified()` (idempotentní — `IsNotified = true`).
4. `SaveChangesAsync` přes `IUnitOfWork`.

Scope auditu: API-key middleware pokryje autentizaci. Nemění se severity ani hodnoty.

### 3.3 CSV export

```
GET /api/changes/export.csv?severity=Critical&profileId=…&since=…
```

- Stejné filtry jako `ListChanges`.
- Žádná paginace — limit `MaxExportRows = 10_000` tvrdě zastropen.
- `Content-Type: text/csv; charset=utf-8`, `Content-Disposition: attachment; filename="changes-YYYYMMDDTHHmm.csv"`.
- Sloupce: `Id,DetectedAt,Severity,ChangeType,Field,CompanyProfileId,DetectedBy,IsNotified,PreviousValue,NewValue`.
- JSON hodnoty jsou v buňkách CSV-escaped (uvozovky zdvojené, uzavřeno do `"..."`).
- CSV injekce — buňky začínající `=`, `+`, `-`, `@`, TAB, CR jsou prefixem `'` odblokovány (ASVS V5.3.3).
- Řádky se streamují přes `IAsyncEnumerable` → žádná buffer-all do paměti.

### 3.4 `IChangeEventRepository.CountAsync`

Rozšířit o volitelný `DateTimeOffset? since`:

```csharp
Task<int> CountAsync(
    ChangeSeverity? severity = null,
    Guid? profileId = null,
    DateTimeOffset? since = null,
    CancellationToken cancellationToken = default);
```

`ListAsync` také dostane `since` — používáno jak v ListChanges (nepovinné)
tak v CSV exportu.

## 4. Subtasky

| # | Popis | Složitost |
|---|-------|-----------|
| 1 | Rozšířit `GetChangeStatsQuery` o `Since`, repository `CountAsync` o `since`, endpoint `stats` o query param. Testy. | S |
| 2 | `AcknowledgeChangeCommand` + handler + validator, endpoint `POST /api/changes/{id}/acknowledge`. Testy. | S |
| 3 | CSV export endpoint + streaming writer s anti-injection helperem. Testy. | M |
| 4 | Frontend API client + hooks (`useChangeStatsThisWeek`, `useAcknowledgeChange`). | S |
| 5 | `ChangeDiffModal.tsx` s jednoduchým JSON highlighterem. | M |
| 6 | `ChangeFeedPage`: stats header (this week), tlačítka Acknowledge / Export, SignalR „new" badge s animací. | M |

## 5. Testovací strategie

### Unit (xUnit, NSubstitute, FluentAssertions)

- `GetChangeStatsHandlerTests` — nový test `Handle_WithSince_PropagatesToRepository`.
- `ListChangesHandlerTests` — ověřit, že `since` teče do repozitáře (pokud ho tam přidáme — viz rozhodnutí níže).
- `AcknowledgeChangeHandlerTests` — 3 testy: success (marks + saves), not found (returns NotFound, no save), already notified (idempotent, no exception).
- `AcknowledgeChangeValidatorTests` — odmítne prázdný Guid.
- `ExportChangesHandlerTests` — respektuje `MaxExportRows`, CSV injekční prefixy se escapují.

### Repository (EF Core InMemory nebo SQLite)

- `ChangeEventRepositoryTests` — nové testy: `CountAsync_WithSince_FiltersByDetectedAt`, `ListAsync_WithSince_Filters`.

### Integration (WebApplicationFactory — volitelné, follow-up)

Hlavní integration testy pokrývá B-76. Zde stačí unit pokrytí + ruční ověření
build + lint.

### Frontend

- `npm run build` + `npm run lint` musí projít.
- Ruční check v dev serveru dle možností (prostředí je headless Linux — pokud
  nelze spustit UI, dokumentujeme, že vizuální test proběhne v manuálním smoke
  testu po deployi).

## 6. Architektonická rozhodnutí

- **Samostatné HTTP volání pro „this week" místo kombinovaného DTO** — jednoduché
  kešování přes React Query, žádný breaking change na `ChangeStatsDto`, menší
  riziko regrese v existujících testech.
- **Idempotentní Acknowledge bez 409 na opakované volání** — operation-side
  effect je setování bool na true; 204 pokaždé je jednodušší pro frontend
  (SignalR deliveries mohou přijít duplicitně).
- **CSV export bez streamování velkých datových sad** — zastropeno na 10 000
  záznamů. Pokud v budoucnu potřeba víc, přidá se asynchronní Batch Export
  (blok B-81).
- **JSON highlighter in-line** — bez externí závislosti (žádný `react-syntax-highlighter`
  bundle +~250 kB). Jednoduchý regex na tokeny (klíče, stringy, čísla, booleany, null).
- **Formát CSV** — UTF-8 BOM pro Excel kompatibilitu, RFC 4180 escape.

## 7. Akceptační kritéria

1. Kliknutí na „Show diff" otevře modální okno s before/after JSON s barvením klíčů/hodnot.
2. Hlavička feedu zobrazuje dvě nové karty: „Critical this week" a „Major this week" s reálnými daty (nezávislé HTTP volání).
3. Na Critical řádku, který ještě není notifikován, je tlačítko „Acknowledge"; stisk zavolá endpoint, cache se invaliduje, badge zmizí.
4. Tlačítko „Export CSV" stáhne soubor obsahující aktuálně filtrované změny, maximálně 10 000 řádků; soubor otevře Excel bez injekce.
5. Příchozí `ChangeDetected` SignalR událost způsobí, že první řádek feedu dostane krátkou pulz/animaci (2 s) a case „new" badge.
6. `dotnet test` (Domain/Application/Infrastructure) i `npm run lint && npm run build` jsou zelené.
7. Bez nových CVE High/Critical v NPM audit.

## 8. Out of scope

- React tests (neexistují v projektu, dle B-48 ani nebyly dodány).
- Bulk Acknowledge (zvažuje se pro B-75).
- Export mimo CSV (PDF/Excel xlsx → plánováno v B-81).
