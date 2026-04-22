# B-84 — Trend analytics (implementation plan)

**Fáze:** 4 — Scale + polish
**Odhad:** 3 h
**Branch:** `claude/eloquent-babbage-mkuUt`
**Status:** 🚧 V realizaci (zahájeno 2026-04-22)

## Vstup (co už existuje)

- `ChangeEvent` agregát (`src/Tracer.Domain/Entities/ChangeEvent.cs:1-72`) s poli
  `DetectedAt` (UTC), `Severity` (`ChangeSeverity` enum Cosmetic/Minor/Major/Critical),
  `CompanyProfileId`. Persistence v `ChangeEventConfiguration.cs:29-31` má indexy na
  `DetectedAt DESC`, `Severity`, `CompanyProfileId`.
- `CompanyProfile` (`CompanyProfile.cs:1-324`) nese `Country` (ISO 3166-1 alpha-2),
  `OverallConfidence`, `LastEnrichedAt`, `LastValidatedAt`, `IsArchived`, `TraceCount`.
- Existující dashboard/stats: `GetDashboardStatsHandler.cs`, `GetChangeStatsHandler.cs`
  a endpoint `GET /api/stats` (`StatsEndpoints.cs`).
- `IChangeEventRepository` (`CountAsync`, `ListAsync`) a `ICompanyProfileRepository`
  (`CountAsync`, `ListAsync`, `ListByCountryAsync`). Ani jeden repozitář zatím
  nepoddržuje bucketing ani per-country agregaci.
- React Dashboard `src/Tracer.Web/src/pages/DashboardPage.tsx:1-147` načítá
  `statsApi.dashboard()` s 30 s refetch interval. **`recharts` není závislost** —
  bude potřeba přidat.

## Scope (co v tomto bloku vzniká)

### Backend (.NET 10, Tracer.Api + Tracer.Application + Tracer.Infrastructure)

1. **Repository rozšíření**
   - `IChangeEventRepository.GetMonthlyTrendAsync(DateTimeOffset fromInclusive, DateTimeOffset toExclusive, CancellationToken)`
     → `IReadOnlyList<ChangeTrendBucketRow>` (Year, Month, Severity, Count).
     EF Core LINQ `GroupBy(e => new { e.DetectedAt.Year, e.DetectedAt.Month, e.Severity })`
     na SQL Server se přeloží do `GROUP BY DATEPART(year,...), DATEPART(month,...)`.
   - `ICompanyProfileRepository.GetCoverageByCountryAsync(DateTimeOffset now, CancellationToken)`
     → `IReadOnlyList<CoverageByCountryRow>` (Country, ProfileCount, AvgConfidence,
     AvgDataAgeDays). Filtruje `IsArchived = false`. `AvgConfidence` přes EF Core
     `EF.Property<double?>("OverallConfidenceValue")`; `AvgDataAge` jako
     `AVG(DATEDIFF(day, LastEnrichedAt, @now))` — projektováno do DTO v handleru
     (EF Core neumí `EF.Functions.DateDiffDay` + `Avg` na nullable spolehlivě; proto
     vracíme `LastEnrichedAt` z DB a průměr počítáme v handleru nad již seskupenými
     řádky).
   - Rows jsou internal records v `Tracer.Domain.Interfaces/` vedle interface
     (přesnější: `Tracer.Application.DTOs.Analytics` — projekce, ne doména).
   - Oba dotazy mají per-country/per-bucket cap: maximální výstup 500 řádků
     (obrana proti velkým DB). Limit vynucen ve SQL (`Take(500)`).

2. **MediatR queries**
   - `GetChangeTrendQuery(TrendPeriod Period, int Months) : IRequest<ChangeTrendDto>`
     - `TrendPeriod` enum v `Tracer.Application.Queries.GetChangeTrend` (Monthly jen
       v MVP; ponechat prostor pro Weekly/Daily extension).
     - `Months` = rolling window (default 12, rozsah 1–36).
   - `GetCoverageQuery(CoverageGroupBy GroupBy) : IRequest<CoverageDto>`
     - `CoverageGroupBy` enum: `Country` (default). Scope bloku je jen `Country`;
       Industry/Severity jsou follow-up.
   - FluentValidation validators (viz Testing strategy níže).
   - Handler pro trend:
     1. Spočítá `fromInclusive` jako začátek měsíce `(now - months)` v UTC;
        `toExclusive` = začátek následujícího měsíce po now.
     2. Zavolá repo `GetMonthlyTrendAsync`.
     3. Vygeneruje kompletní řadu měsíců v rozsahu (nezdáluplné měsíce = 0)
        a namapuje bucket-rows do pivotu (Critical/Major/Minor/Cosmetic).
   - Handler pro coverage:
     1. Zavolá repo → rows (Country, profileCount, sumConfidence, rawLastEnriched).
     2. V handleru přepočte `AvgConfidence` a `AvgDataAgeDays` (server-side
        `DateDiffDay` by vyžadovalo dodatečný SQL mapping; přijatelné řešit
        v paměti — řádů ~200 zemí).
     3. Seřadí podle `ProfileCount DESC`.

3. **DTOs** (v `src/Tracer.Application/DTOs/`):
   - `ChangeTrendDto { TrendPeriod Period; int Months; IReadOnlyList<ChangeTrendBucketDto> Buckets }`
   - `ChangeTrendBucketDto { DateOnly PeriodStart; int Critical; int Major; int Minor; int Cosmetic; int Total }`
   - `CoverageDto { CoverageGroupBy GroupBy; IReadOnlyList<CoverageEntryDto> Entries }`
   - `CoverageEntryDto { string? Group; int ProfileCount; double AvgConfidence; double AvgDataAgeDays }` —
     `Group` je nullable kvůli profilům bez `Country`.

4. **API endpoints** (`AnalyticsEndpoints.cs`, nový soubor):
   - `GET /api/analytics/changes?period=monthly&months=12` → `ChangeTrendDto`
   - `GET /api/analytics/coverage?groupBy=country` → `CoverageDto`
   - Skupina `/api/analytics` s tagem `Analytics` a OpenAPI summary.
   - Registrace v `Program.cs` (`app.MapAnalyticsEndpoints();`) mezi Stats a Validation.

5. **Validation**
   - `GetChangeTrendValidator`: `Months` in [1..36]; `Period` enum `IsInEnum`.
   - `GetCoverageValidator`: `GroupBy` enum `IsInEnum`.
   - Oba parsují enumy case-insensitive přes `JsonStringEnumConverter`
     (už registrovaný v `Program.cs`).

### Frontend (Tracer.Web, React 19)

1. **Dependency:** přidat `recharts@^2.12` do `package.json`.
2. **API client:** rozšířit `src/Tracer.Web/src/api/statsApi.ts` (nebo nový
   `analyticsApi.ts`) o `analyticsApi.changes({ period, months })` a
   `analyticsApi.coverage({ groupBy })` — typy odpovídající backend DTO.
3. **Widget `ChangesTrendChart`** (`src/Tracer.Web/src/components/ChangesTrendChart.tsx`):
   - `recharts <LineChart>` s osou X = měsíc, osou Y = počet událostí, 4 `<Line>`
     (jedna per severity). Tailwind pro layout, barvy fixní přes
     severity → CSS var (reuse existing badge colors z ChangeFeed page).
   - Lazy loading není třeba v MVP; recharts ~100 kB gzip není kritický.
4. **Widget `CoverageByCountryTable`** (`src/Tracer.Web/src/components/CoverageByCountryTable.tsx`):
   - Tabulka se sloupci Country / # profilů / ø confidence / ø data age (dny).
   - Top 10 řádků + "Show all" toggle (client-side paginace).
5. **Dashboard integrace:** přidat dvě sekce pod existující stats grid v
   `DashboardPage.tsx` — každý widget má `useQuery` s 5min staleTime (data se
   nemění sekundově). Nezavěšovat na SignalR `ChangeDetected` — pouze
   `refetchOnWindowFocus` a manual invalidation přes tlačítko "Refresh".

## Ovlivněné komponenty/moduly

| Vrstva | Soubory (NEW = nové) |
|--------|----------------------|
| Domain | (beze změny) |
| Application | `DTOs/ChangeTrendDto.cs` NEW, `DTOs/CoverageDto.cs` NEW, `Queries/GetChangeTrend/*` NEW, `Queries/GetCoverage/*` NEW |
| Infrastructure | `Persistence/Repositories/ChangeEventRepository.cs` (extend), `Persistence/Repositories/CompanyProfileRepository.cs` (extend) |
| Domain interfaces | `Interfaces/IChangeEventRepository.cs`, `Interfaces/ICompanyProfileRepository.cs` — nové metody |
| API | `Endpoints/AnalyticsEndpoints.cs` NEW, `Program.cs` (registrace endpointu) |
| React | `package.json`, `src/api/analyticsApi.ts` NEW, `src/components/ChangesTrendChart.tsx` NEW, `src/components/CoverageByCountryTable.tsx` NEW, `src/pages/DashboardPage.tsx` (integrace) |
| Dokumentace | `CLAUDE.md` (sekce s novými patterny), tento plán |

## Datové modely a API kontrakty

### ChangeTrend

```
GET /api/analytics/changes?period=Monthly&months=12
200 OK
{
  "period": "Monthly",
  "months": 12,
  "buckets": [
    { "periodStart": "2025-05-01", "critical": 2, "major": 12, "minor": 8, "cosmetic": 3, "total": 25 },
    { "periodStart": "2025-06-01", "critical": 0, "major": 5,  "minor": 4, "cosmetic": 1, "total": 10 },
    ...
  ]
}
```

### Coverage

```
GET /api/analytics/coverage?groupBy=Country
200 OK
{
  "groupBy": "Country",
  "entries": [
    { "group": "CZ", "profileCount": 148, "avgConfidence": 0.83, "avgDataAgeDays": 42.1 },
    { "group": "DE", "profileCount": 67,  "avgConfidence": 0.77, "avgDataAgeDays": 61.8 },
    ...
  ]
}
```

Chybové odpovědi: ProblemDetails (RFC 7807) jako ostatní endpointy.

## Testovací strategie

### Unit (xUnit + NSubstitute + FluentAssertions)

1. **`GetChangeTrendHandlerTests`**
   - Happy path: stub repo vrací buckety pro 3 měsíce × 4 severity → DTO má 12 měsíců (chybějící měsíce jsou 0-buckety).
   - Months validation: <1 nebo >36 selže validator (testováno v `GetChangeTrendValidatorTests`).
   - UTC boundary: volání ke konci měsíce (`now = 2026-04-30T23:59:59Z`) → horní hranice `toExclusive = 2026-05-01T00:00:00Z` a spodní hranice o `months` zpět.
   - Cancellation: `CancellationToken` propagován do repo volání.
2. **`GetCoverageHandlerTests`**
   - Stub repo vrací 3 země → DTO seřazené descending podle `ProfileCount`.
   - AvgConfidence/AvgDataAgeDays výpočet: stub vrátí known values → assert na spočtený průměr.
   - GroupBy mimo enum range: validator selže.
3. **Validators**
   - `GetChangeTrendValidator` pokrývá `Months` range a `Period` IsInEnum.
   - `GetCoverageValidator` pokrývá `GroupBy` IsInEnum.

### Repository layer (Infrastructure)

- Integration testy pro nové repo metody **vyžadují DbContext harness** — ten dosud
  v `Tracer.Infrastructure.Tests` chybí (viz follow-up v B-71, B-83). Scope B-84:
  pokryjeme jen handlery přes NSubstitute; následný repo test je follow-up společný
  s ostatními bloky. Bude zapsán jako TODO v commit messagi.

### E2E / smoke

- Ruční curl test lokálně po spuštění API. Reálná data: pokud jsou v DB události,
  ověřit že počet řádků odpovídá `GetChangeStats` sumě. Smoke ukotvit do
  `deploy/scripts/smoke-test-phase2.sh` — dopsat 2 volání jako follow-up k B-78
  (Phase 3 deployment).

## Bezpečnost a PII

- Oba endpointy vrací **pouze agregáty**, nikoliv řádky ChangeEvent ani
  CompanyProfile. Žádná PII v payloadu.
- Auth jde standardně přes `ApiKeyAuthMiddleware` — endpointy pod
  `/api/analytics` nejsou v allowlistu, takže vyžadují API klíč.
- Rate limiting: použít globální `"read"` politiku (pokud existuje) nebo se
  spolehnout na default fallback.
- Coverage endpoint může vracet malá čísla pro země s velmi málo profilů —
  GDPR `k-anonymita` nebyla v projektu dosud řešena a pro agregáty zemí
  s ≥1 profilem není problematická (firemní data, ne osobní). Pokud by
  v budoucnu groupBy zahrnoval `Officers` → je potřeba minimální threshold
  (např. skrýt skupiny s <5 entitami).

## Akceptační kritéria

1. `GET /api/analytics/changes?period=Monthly&months=12` vrací 200 se 12 měsíčními
   buckety (explicitní nuly pro měsíce bez událostí), seřazenými vzestupně podle `periodStart`.
2. `GET /api/analytics/coverage?groupBy=Country` vrací 200, řádky seřazené
   descending podle `profileCount`.
3. Validace: `months=0`, `months=100`, `period=invalid`, `groupBy=invalid` → HTTP 400 s ProblemDetails.
4. Dashboard v prohlížeči zobrazí trend chart a coverage tabulku; při prázdné DB
   grafické widgety mají čistý "no data" stav.
5. Všechny nové unit testy procházejí; build a existing test suite zelené.
6. Žádná PII v response body, error messages ani logs.
7. CLAUDE.md obsahuje sekci o nových patterns (handler month-bucket projekce,
   server-side vs. client-side avg).

## Commitová strategie

1. `docs: add B-84 trend analytics implementation plan`
2. `feat: add analytics repository aggregates for changes and coverage`
3. `feat: add trend analytics queries and handlers (B-84)`
4. `feat: expose /api/analytics endpoints (B-84)`
5. `test: cover trend analytics queries and validators (B-84)`
6. `feat: add recharts trend chart and coverage table widgets (B-84)`
7. `docs: update CLAUDE.md with B-84 analytics patterns`

## Rizika a mitigace

| Riziko | Mitigace |
|--------|----------|
| EF Core nepřeloží GroupBy(year, month, severity) na SQL Serveru | Pokud selže na startu testu, přeneseme projekci do raw SQL (`FromSqlRaw`) nebo SELECT + `AsEnumerable().GroupBy()` s hard cap na datum rozsah. |
| Recharts přidá ~100 kB do bundlu | V MVP OK. Pokud nutné, lazy-load přes `React.lazy` v budoucnu (B-88 UI polish). |
| Nepřesná avg confidence u velmi malých zemí (1 profil → 100 %) | V UI zobrazit `profileCount` vedle průměru; future: threshold indikátor. |
| `ChangeEvent` index chybí pro `(DetectedAt, Severity)` | Jednorozměrné indexy na těchto sloupcích existují, SQL Server použije pravděpodobně index merge. Pokud perf test (B-86) ukáže pomalost, přidáme composite index jako follow-up. |

## Follow-up mimo scope B-84

- Repository-level integration test pro nové agregáty (až bude DbContext test harness).
- `period=Weekly`/`period=Daily` rozšíření.
- `groupBy=Industry`/`groupBy=Severity` rozšíření.
- Case study v `docs/` o výběru client-side průměru + zdůvodnění.
- Smoke script úprava v rámci B-78.
