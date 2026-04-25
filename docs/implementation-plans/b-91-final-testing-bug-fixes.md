# B-91 — Final testing + bug fixes

**Fáze:** 4 — Scale + polish
**Odhad:** 4 h
**Zahájeno:** 2026-04-24
**Branch:** `claude/eloquent-babbage-FVSJE`

## Vstupy a prerekvizity

- Notion plan status: B-91 je dále vedena jako "Final testing + bug fixes" navazující
  na kompletní systém (B-01 … B-90).
- Strict pre-req je "kompletní systém". V této chvíli jsou B-66, B-73, B-78, B-85, B-90
  v *V realizaci* (code complete, awaiting merge). Blok tedy neprovádí full 50 × 10 × 3
  live regression matrix (ta patří na release candidate po mergech + deploy) — v rámci
  B-91 se zaměříme na statickou / test-only část, aby nedošlo k duplikaci práce s B-92
  (Production deployment + monitoring).
- Stale-table reconciliation: B-59 … B-64 jsou v Notionu stále *Nezahájeno*, přestože
  jejich code je dávno v `develop`u a má detailní pattern sekce v `CLAUDE.md`.
  Reconciliation je první subtask tohoto bloku.

## Cíle

1. Reconciliace stale Notion entries (B-59 … B-64) s realitou v kódu a `CLAUDE.md`.
2. Clean-up pass: TODO/FIXME/HACK komentáře, dead code, unused using direktivy.
3. Zjištění a oprava triviálních bugů (max diff ~100 LOC — nic co by odstartovalo
   nový feature blok).
4. Baseline dokument pokrývající stav testů (kde chybí coverage, kde jsou flaky
   kandidáti, kde chybí DbContext integration harness — následovník B-76).
5. Minimální output: commits na `claude/eloquent-babbage-FVSJE`, updated `CLAUDE.md`
   s patterny objevenými během cleanup passu.

## Co B-91 *nedělá* (explicitní out-of-scope)

- Live regression matrix 50 firem × 10 zemí × 3 depth levels → to proběhne až po
  deploy (B-92) s reálnými API klíči a stabilním hardwarem.
- Performance benchmarking (hotovo v B-86, `Tracer.Benchmarks` + k6 skripty).
- Security hardening (hotovo v B-87).
- Nové testy, které vyžadují chybějící `DbContext` integration harness — ten je
  evidovaný follow-up z B-71/B-83/B-84; B-91 ho neřeší, jen ho zdokumentuje.
- Změny rozhraní / nové endpointy — blok je čistě polishing.

## Dekompozice subtasků

| # | Subtask                                                    | Složitost | Commit kind |
|---|-----------------------------------------------------------|-----------|-------------|
| 1 | Add this implementation plan doc                          | S         | `docs:`     |
| 2 | Reconcile stale Notion entries B-59…B-64                   | S         | (notion)    |
| 3 | Static repo audit report (TODOs, dead code, unused usings) | M         | `docs:`     |
| 4 | Apply cleanup fixes (delete dead code, drop unused usings) | S–M       | `refactor:` |
| 5 | Triage & fix any obvious bugs                              | S         | `fix:`      |
| 6 | `docs/testing/coverage-baseline.md` s gap analysis         | S         | `docs:`     |
| 7 | `CLAUDE.md` update — zachycení patternů / konvencí z #3–#5 | S         | `docs:`     |
| 8 | Push → review → merge                                      | S         | —           |

## Ovlivněné komponenty

- `src/Tracer.Api`, `src/Tracer.Application`, `src/Tracer.Infrastructure`,
  `src/Tracer.Domain` — static sweep.
- `src/Tracer.Web` — pouze pokud se najde triviální lint / unused import.
- `tests/**` — pouze pokud je nutný drobný fix na flaky test (nepřidávat nové testy
  bez jasného triggeru z #5).
- `docs/testing/coverage-baseline.md` — nový dokument.
- `CLAUDE.md` — update s novými konvencemi / známými pastmi.

## Data model / API contracts

Žádné. B-91 je čistě polishing + dokumentační blok.

## Testovací strategie

- Build + test běží v CI (`.github/workflows/**`) — lokálně SDK chybí (platí
  `CLAUDE.md`: ".NET SDK není v sandboxu").
- Pokud subtask #5 (fix bugů) mění chování kódu, musí k němu vzniknout buď nový
  test, nebo být pokryto existujícím.
- Cleanup-only změny (deleted unused usings, renames in comments) nevyžadují nový
  test — stačí green CI.

## Akceptační kritéria

- [ ] Notion table reconcilována (B-59 … B-64 = Dokončeno s referencí).
- [ ] `docs/implementation-plans/b-91-final-testing-bug-fixes.md` existuje.
- [ ] `docs/testing/coverage-baseline.md` existuje, obsahuje:
  - seznam projektů + počet testovacích souborů,
  - kde chybí DbContext integration harness (follow-up evidovaný v B-71/B-83/B-84),
  - evidence flaky / timing-sensitive testů (pokud nalezeno),
  - action items pro B-92 / follow-up bloky.
- [ ] `CLAUDE.md` zachycuje libovolné nově objevené konvence nebo pasti z auditu.
- [ ] Žádný TODO/FIXME/HACK komentář v src/** bez issue linku nebo explicitního
  justification komentáře.
- [ ] CI green (build + test + lint) po mergi do `develop`.

## Rizika a mitigace

| Riziko                                                    | Mitigace |
|----------------------------------------------------------|----------|
| Cleanup smaže kód, který je používán reflexí / DI / migrací | Grep na referenced symbol names před deletem; při pochybách zachovat a evidovat v coverage baseline doc. |
| Race s běžícími `V realizaci` branchemi (B-66/73/78/85/90) | Dotýkat se jen stabilních částí kódu; vyhnout se souborům měněným v těchto branchích (ověřit `git log develop..origin/<branch>` pokud existuje). |
| Fix zasahující více bloků naráz                           | Pokud fix přesahuje "triviálně bezpečné", zapsat do coverage baseline jako follow-up a neprovádět ho zde. |
| Notion out-of-sync znovu                                  | Poslední krok bloku (completion update) musí B-91 přehodit na Dokončeno + doplnit souhrn. |

## Follow-upy

Po dokončení B-91 předáváme na:
- **B-92** — live regression matrix (50 firem × 10 zemí × 3 depth), App Insights alerts,
  production Bicep deploy.
- Sdílený follow-up **"DbContext integration harness"** (čeká na issue mimo tento blok):
  repo-level integrační testy pro `IChangeEventRepository.CountSinceAsync`,
  `IValidationRecordRepository.CountSinceAsync`, `ICompanyProfileRepository.ArchiveStaleAsync`,
  `GetCoverageByCountryAsync`, `GetRevalidationQueueAsync`.
