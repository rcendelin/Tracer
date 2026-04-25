# B-90 — Documentation

Branch: `feature/b-90-documentation`
Fáze: 4 — Scale + polish
Odhad: 3 h

## 1. Cíl

Konsolidovat existující rozptýlené dokumenty (`CLAUDE.md`, `deploy/DEPLOYMENT.md`,
`deploy/RUNBOOK.md`, `docs/performance/`, `docs/testing/`) do dohledatelného
adresáře `docs/` se zastřešujícím rozcestníkem (`docs/README.md`), přidat
chybějící operační handbook a troubleshooting guide, a vytvořit první sadu
ADR (Architecture Decision Records) zachycujících klíčová rozhodnutí, která
v `CLAUDE.md` zatím existují jen jako odrážky.

Cílový čtenář: nový developer / on-call inženýr, který se za 30 minut musí
zorientovat v repu, porozumět hot-path datovému toku, a najít vstupní bod
pro běžné operační úlohy.

## 2. Dekompozice

| # | Subtask | Složitost | Výstup |
|---|---|---|---|
| 1 | `docs/README.md` rozcestník | S | Index + odkazy + 30sekundový přehled |
| 2 | `docs/architecture.md` | M | Top-level architektura, vrstvy, datový tok |
| 3 | `docs/configuration.md` | M | Centrální config reference |
| 4 | `docs/providers.md` | M | Katalog providerů + jejich quirks |
| 5 | `docs/operations/handbook.md` | M | On-call denní reference |
| 6 | `docs/operations/troubleshooting.md` | M | Časté problémy a fix-it postupy |
| 7 | `docs/adr/0001-clean-architecture.md` | S | ADR: Clean Architecture |
| 8 | `docs/adr/0002-tracedfield.md` | S | ADR: TracedField<T> jako základní datová jednotka |
| 9 | `docs/adr/0003-waterfall-orchestrator.md` | S | ADR: Waterfall + tier scheduling |
| 10 | `docs/adr/0004-domain-events-via-mediatr.md` | S | ADR: Domain events v MediatR |
| 11 | `docs/adr/0005-no-mock-database.md` | S | ADR: Integrační testy proti reálnému SQL |
| 12 | `CLAUDE.md` doplnit o odkaz na `docs/README.md` | S | One-line link |

## 3. Ovlivněné komponenty

Pouze dokumentace — žádné kódové změny. Nemění žádné API kontrakty,
datové modely ani dependencies.

## 4. Konvence dokumentů

- **ADR formát:** Lehký Michael Nygard styl — Title, Status (Accepted /
  Superseded by …), Context, Decision, Consequences. ≤ 200 řádků.
- **Operations docs:** Step-by-step postup s konkrétními příkazy. Žádné
  obecné "fíčkové" povídání.
- **README rozcestník:** Maximálně 1 odstavec úvodu + tabulka odkazů.
- **Markdown:** Standard CommonMark + GFM tabulky. Žádné HTML.
- **Tone:** Češtinou tam kde je in `CLAUDE.md` česky (operační poznámky),
  jinak anglicky (architektura, ADR).

## 5. Testovací strategie

- Manuální review každého souboru (markdown rendering, broken links).
- Linkcheck: `find docs -name '*.md' -exec grep -nH 'http://\|https://' {} \;`
  → spot-check nových odkazů.
- Žádné automatické testy — dokumentace nemá unit-test surface.

## 6. Akceptační kritéria

1. `docs/README.md` existuje a uvádí ≥ 6 sekcí (architecture, providers,
   configuration, operations, ADR, testing, performance).
2. ≥ 5 ADR souborů v `docs/adr/`, každý ve formátu Nygard, ≤ 200 řádků.
3. `docs/operations/handbook.md` obsahuje sekce: deployment, monitoring,
   common runbook items, escalation contacts (placeholder).
4. `docs/operations/troubleshooting.md` obsahuje ≥ 5 časté problémy
   s konkrétním fix-it postupem.
5. `docs/configuration.md` zrcadlí všechny config klíče z `CLAUDE.md`
   sekce "Environment variables / configuration".
6. `docs/providers.md` má jednu sekci na každý registrovaný provider
   s odkazem na jeho zdrojový kód, identifikační formát, rate limit
   a status normalizaci.
7. `CLAUDE.md` obsahuje odkaz na `docs/README.md` jako vstupní bod.
8. `git diff develop` nemění žádný `.cs`, `.ts`, `.tsx`, `.bicep`, `.json`
   soubor (čisté docs-only změny).

## 7. Konzervativní rozhodnutí

- **Neduplikovat `CLAUDE.md`** — `CLAUDE.md` zůstává `prompt-targeted`
  (LLM-friendly bullety s konkrétními pasti); `docs/` cílí na lidi.
  Cross-link, ale žádný copy-paste.
- **ADR jen pro již-rozhodnutá témata** — ne pro otevřené architektonické
  otázky. Open questions patří do follow-up issue trackingu, ne do ADR.
- **Operations docs jako "first draft"** — explicitní `TODO: doplnit` tagy
  pro pole, která potřebují produkční zkušenost (eskalační kontakty,
  konkrétní alert thresholdy v provozu). Lepší než dlouho čekat na finální
  verzi.
