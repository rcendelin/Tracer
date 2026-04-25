# Tracer documentation

Welcome. Tracer is a .NET 10 micro-service that enriches partial company
information into comprehensive profiles using free public data sources, web
scraping and LLM extraction. It maintains its own **Company Knowledge Base
(CKB)** and integrates with FieldForce CRM via REST + Service Bus.

If this is your first time in the repo, start with **Architecture** below,
then skim the **Provider catalogue**. On-call engineers should bookmark
**Operations**.

## Sections

| Topic | Doc | Audience |
|---|---|---|
| High-level architecture, layers, data flow | [architecture.md](./architecture.md) | New devs, reviewers |
| Configuration reference | [configuration.md](./configuration.md) | Devs, ops |
| Provider catalogue (registries, scrapers, AI) | [providers.md](./providers.md) | Devs |
| Operations handbook | [operations/handbook.md](./operations/handbook.md) | On-call |
| Troubleshooting guide | [operations/troubleshooting.md](./operations/troubleshooting.md) | On-call |
| Architecture Decision Records | [adr/](./adr/) | Reviewers, architects |
| Performance benchmarks & load tests | [performance/README.md](./performance/README.md) | Performance owners |
| Test coverage baseline | [testing/coverage-baseline.md](./testing/coverage-baseline.md) | Devs, reviewers |
| Implementation plans (per-block) | [implementation-plans/](./implementation-plans/) | Anyone tracing back why |
| FieldForce integration example | [integration/FieldForce-Consumer-Example.cs](./integration/FieldForce-Consumer-Example.cs) | FieldForce devs |

## Companion files at the repo root

- **`CLAUDE.md`** — LLM-targeted, dense bullet list of conventions, quirks,
  and historical decisions. Treat as a working notebook; read top-to-bottom
  when you need to understand why a piece of code looks the way it does.
- **`CONTRIBUTING.md`** — How to run, test, branch, and commit.
- **`README.md`** — Repo-level pitch + 5-minute quickstart.

## Conventions

- Architecture decisions use Michael Nygard's [ADR template](https://github.com/joelparkerhenderson/architecture-decision-record/tree/main/locales/en/templates/decision-record-template-by-michael-nygard) — see [adr/](./adr/).
- Operational procedures are written as a sequence of concrete commands with
  their expected outputs, not abstract advice.
- When you change a hot-path pattern, update both `CLAUDE.md` (LLM bullet)
  AND the relevant `docs/` page (human prose).
