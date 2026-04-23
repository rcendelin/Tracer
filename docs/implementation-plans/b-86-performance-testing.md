# B-86 — Performance testing

**Status:** V realizaci
**Branch:** `claude/eloquent-babbage-Bwx7i`
**Fáze:** 4 — Scale + polish
**Odhad:** 4 h
**Datum zahájení:** 2026-04-22

## Kontext a cíl

Tracer má plně vybavený enrichment pipeline (Phase 1–3) i hotovou většinu
Phase 4 hardening bloků (Redis cache B-79, rate limiting / circuit breakers
B-80, CKB archival B-83, trend analytics B-84). Chybí ale **měřitelné
výkonnostní baseline** a reprodukovatelný způsob, jak zjistit regresi
ještě před deployem. Existují pouze SLO targety zakotvené v `CLAUDE.md`
(Quick <5 s, Standard <10 s, Deep <30 s, batch ≤200 items, CKB-cache
target <500 ms) bez jakéhokoli harness, který by je ověřil.

B-86 zavádí dvě vrstvy měření:

1. **Micro-benchmarks (BenchmarkDotNet)** — hot-path služby, které běží
   tisíckrát za request (`CompanyNameNormalizer`, `FuzzyNameMatcher`,
   `ConfidenceScorer`, `GoldenRecordMerger`). Měří se throughput
   a alokace; baseline se ukládá do `docs/performance/baselines.md`.
2. **Load testing (k6)** — HTTP/SignalR scénáře nad deployed API, které
   ověří SLO pro `POST /api/trace`, `GET /api/profiles`, batch submission
   a dashboard read-path. Skripty jsou idempotentní, autentizované přes
   `X-Api-Key` a respektují rate-limity (`batch` policy 5 req/min).

Oba harnessy jsou **opt-in** — BenchmarkDotNet se spouští ručně /
workflow\_dispatch, k6 stejně tak. Nikdy nepřibývá do hot CI path, aby se
nezdržoval běžný `develop` merge.

### Proč teď

- Phase 4 scale/polish blok — předchůdci (B-79 Redis, B-80 rate
  limiting, B-83 archival, B-84 trend analytics) už v develop jsou
  a přinášejí hot-path změny, které je fér změřit.
- B-78 (Deployment Phase 3 + performance) je pořád Nezahájeno — jakmile
  se dostane na řadu, skripty z B-86 budou jeho smoke-test step.
- Sourozenecké bloky v Notionu "V realizaci" (B-66 lightweight
  revalidation, B-73 ChangeFeed enhanced, B-85 manual override audit)
  sahají do zcela jiných komponent, takže B-86 s nimi nebude konfliktit.

### Proč ne teď

- **Nic** — blok je čistě additivní, nemění business logiku, žádnou
  migraci, žádný kontrakt. V nejhorším případě PR neprojde code review,
  ale nic netriviálního nerozbije.

## Dekompozice subtasků

| # | Subtask | Odhad | Komplexita |
|---|---------|-------|-----------|
| 1 | `Tracer.Benchmarks` projekt (net10.0, BenchmarkDotNet) + registrace do `Tracer.slnx` | 20 min | low |
| 2 | Benchmark třídy: `NameNormalizerBenchmarks`, `FuzzyMatcherBenchmarks`, `ConfidenceScorerBenchmarks`, `GoldenRecordMergerBenchmarks` | 60 min | medium |
| 3 | `run-benchmarks.sh` / `.ps1` helper + `docs/performance/baselines.md` (template + initial numbers placeholders) | 20 min | low |
| 4 | k6 scripts: `trace-smoke.js`, `trace-load.js`, `batch-load.js`, `dashboard-load.js` — se shared `helpers.js` (auth + SLO thresholds) | 70 min | medium |
| 5 | `deploy/scripts/run-load-test.sh` wrapper pro k6 | 15 min | low |
| 6 | GitHub workflow `perf.yml` (manual dispatch; benchmarks job + k6 job) — artifacts upload | 30 min | low |
| 7 | `docs/performance/README.md` — runbook, SLO, interpretace výsledků | 20 min | low |
| 8 | `CLAUDE.md` patterns pro benchmark / k6 konvence | 15 min | trivial |

**Celkem:** ~ 4 h (odpovídá 4 h odhadu).

## Ovlivněné komponenty / moduly

**Nové:**

- `tests/Tracer.Benchmarks/Tracer.Benchmarks.csproj` — samostatný
  konzolový projekt (`Microsoft.NET.Sdk`, net10.0, `OutputType=Exe`).
  Není v `tests/` uvnitř `dotnet test` sady — benchmarky se nepouští
  testovacím runnerem.
- `tests/Tracer.Benchmarks/Program.cs` — `BenchmarkSwitcher` entrypoint.
- `tests/Tracer.Benchmarks/Benchmarks/*.cs` — jednotlivé `[MemoryDiagnoser]`
  třídy.
- `deploy/scripts/run-benchmarks.sh` — Linux/macOS helper (release build +
  BenchmarkDotNet runner).
- `deploy/scripts/run-load-test.sh` — k6 wrapper (validuje env vars,
  spouští kontejnerové k6 nebo lokální binárku).
- `deploy/k6/helpers.js` — sdílené thresholds, auth, base URL config.
- `deploy/k6/trace-smoke.js` — jedno-uživatelský smoke test.
- `deploy/k6/trace-load.js` — ramp-up profil (1 → 25 VU, 5 min).
- `deploy/k6/batch-load.js` — batch endpoint s respektem k 5 req/min
  rate limit (`iterations = 5`, sleep mezi iteracemi).
- `deploy/k6/dashboard-load.js` — read-only `GET /api/stats` + `GET
  /api/profiles` pod 50 VU.
- `docs/performance/README.md` — runbook.
- `docs/performance/baselines.md` — přehled změřených čísel (baselines
  + historie).
- `.github/workflows/perf.yml` — manual-dispatch workflow.
- `CLAUDE.md` — patterns pro benchmark / load test.

**Úpravy:**

- `Tracer.slnx` — přidat `tests/Tracer.Benchmarks/Tracer.Benchmarks.csproj`
  (Folder `/tests/`).
- `Directory.Packages.props` — přidat `BenchmarkDotNet` 0.14.0 do
  `Testing` ItemGroup.

**Úmyslně nedotknuto:**

- Application / Domain / Infrastructure / Api kódy — žádné "tunable"
  knoby, žádné `InternalsVisibleTo` pro benchmark projekt. Benchmarky
  volají pouze **veřejné** API služeb. Tím se zajistí, že benchmarkem
  měříme skutečnou production path a nevzniká závislost production kódu
  na BenchmarkDotNet atributech.
- `.github/workflows/ci.yml` — nemění se; benchmarky nejsou blocking
  na PR.

## Datový model a API kontrakty

**Nezmění se.** B-86 je čistě měřicí harness — nepřidá žádný endpoint,
entitu, DTO, migrace ani konfigurační sekci.

## Testovací strategie

### Benchmarky (BenchmarkDotNet)

| Služba | Vstup | Iterace | Metriky |
|--------|-------|---------|---------|
| `CompanyNameNormalizer.Normalize` | 8 reprezentativních jmen (ASCII, diakritika, cyrilice, legal forms) | default | mean ns / alloc B / Gen0 |
| `FuzzyNameMatcher.CombinedScore` | 10 páru (high/mid/low similarity) | default | mean ns / alloc B |
| `ConfidenceScorer.ScoreFields` | 8 fieldů × 3 kandidáti | default | mean ns / alloc B / Gen0 |
| `GoldenRecordMerger.Merge` | existing profile + 4 provider výstupy | default | mean ns / alloc B / Gen0 |

Baseline se ukládá jako **textový přehled** v `docs/performance/baselines.md`
(MD tabulka), ne jako strojově čitelný artefakt — cílem je mít lidský
audit trail, ne automatickou gate.

**Akceptační kritérium:** každý benchmark běží v release módu do 60 s
a produkuje validní BenchmarkDotNet report (stdout + `BenchmarkDotNet.Artifacts/`).

### k6 skripty

| Scénář | VUs | Duration | SLO threshold |
|--------|-----|----------|--------------|
| `trace-smoke.js` | 1 | 30 s | `http_req_duration p(95) < 5000` (Quick depth) |
| `trace-load.js` | ramp 1→25 | 5 min | `http_req_failed < 0.01`, `p(95) < 10000` (Standard) |
| `batch-load.js` | 1 | 1 min | `http_req_duration p(95) < 3000`, `iterations == 5` |
| `dashboard-load.js` | 50 | 2 min | `http_req_duration p(95) < 500`, `http_req_failed < 0.005` |

Thresholds jsou zakotvené uvnitř skriptu (`options.thresholds`) — k6
vrací non-zero exit code, pokud některý SLO padne. Workflow tak funguje
jako gate.

**Akceptační kritérium:** všechny 4 skripty validují přes `k6 inspect`
a projdou dry-run (`k6 run --vus 1 --duration 10s`). Plný běh je
manuální proti reálnému staging endpointu.

### Unit testy (doplňkově)

Žádné nové xUnit testy — benchmarky **nejsou** unit testy a neměly by
běžet v `dotnet test`. Správnost benchmarkovaných služeb je pokryta
existujícími testy (`CompanyNameNormalizerTests`, `FuzzyNameMatcherTests`,
`ConfidenceScorerTests`, `GoldenRecordMergerTests`).

## Akceptační kritéria

1. `Tracer.Benchmarks` projekt se buildí z `dotnet build --configuration Release`.
2. `./deploy/scripts/run-benchmarks.sh` vyprodukuje BenchmarkDotNet
   výstup pro všechny 4 benchmark třídy.
3. Všechny k6 skripty projdou `k6 inspect` (syntax valid) i `k6 run`
   dry-run proti localhostu (mock 200 OK).
4. `.github/workflows/perf.yml` má `workflow_dispatch` trigger s volbou
   `job: benchmarks|load-test|both`.
5. `docs/performance/README.md` popisuje: spuštění, interpretaci,
   kriteria regrese (>20 % mean, >10 % alloc / failed p(95) threshold).
6. `docs/performance/baselines.md` obsahuje tabulku pro všechny 4
   benchmarky (čísla jako `TBD` — plní se po prvním reálném běhu).
7. `CLAUDE.md` má sekci "Performance testing" s konvencemi.
8. Žádný existující test / endpoint / konfigurace se nezmění (čistě
   additivní commit).
9. `npm audit`, `dotnet list package --vulnerable` ekvivalent: nové
   závislosti (BenchmarkDotNet) jsou vypublikované stable verze bez
   known CVE (check přes NuGet dashboard).

## Bezpečnostní úvahy

- **Žádné secrets v repu.** k6 skripty čtou `BASE_URL`, `API_KEY` z env
  vars; `deploy/scripts/run-load-test.sh` v případě chybějících proměnných
  fail-fast vypíše usage a exit 1. Žádné defaultní production URL.
- **Rate limiting respect.** `batch-load.js` je záměrně nízkofrekvenční
  (iteration sleep ≥ 12 s), aby neblokoval `batch` policy (5 req/min).
- **Žádná PII.** Trace payloady v k6 skriptech používají fiktivní data
  (`"Contoso International Testing Ltd."`, `+44 000 000 0000`) —
  nic, co by reprezentovalo reálnou firmu. Batch-load používá
  deterministické index-based názvy (`Load Test Company NNN`).
- **Benchmark projekt je console exe.** Výstupy jsou čistě lokální
  artefakty (`BenchmarkDotNet.Artifacts/`); GitHub workflow je uploads
  jako non-public artifact (výchozí GH retention 90 dní).

## Rollback plán

Blok je čistě additivní; rollback = jeden revert commit (`git revert
<merge-commit>`). Nic z production hot-path se nemění.

## Navazující bloky

- **B-78 Deployment Phase 3 + performance** — použije `deploy/k6/trace-smoke.js`
  v smoke-test step po deployi.
- **B-86-follow-up (pokud vznikne):** integrace baseline diff s GitHub
  Checks (failing build při >20 % regresi). Mimo scope současného bloku.
