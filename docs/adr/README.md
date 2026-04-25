# Architecture Decision Records

Tracer uses lightweight ADRs in [Michael Nygard's
template](https://github.com/joelparkerhenderson/architecture-decision-record/tree/main/locales/en/templates/decision-record-template-by-michael-nygard).

## When to write a new ADR

- A non-trivial cross-cutting decision (architecture, dependency choice,
  data model invariant) that the next maintainer should understand
  *before* changing related code.
- The decision has a real alternative that was considered and rejected.
  If there's no alternative, you're documenting a fact, not a decision —
  put it in `docs/architecture.md` or `CLAUDE.md` instead.

## Conventions

- File name: `NNNN-short-slug.md` with zero-padded `NNNN`.
- Status: one of `Proposed`, `Accepted`, `Superseded by NNNN`, `Rejected`.
- Length cap: ~ 200 lines. If you need more, the decision is too coarse —
  split it.
- Cross-link in the **Related** section: ADRs that came before, blocks
  in the implementation plan that introduced the decision, and
  `CLAUDE.md` bullets that capture follow-up consequences.

## Index

| # | Title | Status |
|---|---|---|
| [0001](./0001-clean-architecture.md) | Clean Architecture, four projects, dependencies inward | Accepted |
| [0002](./0002-tracedfield.md) | `TracedField<T>` as the unit of enrichment data | Accepted |
| [0003](./0003-waterfall-orchestrator.md) | Waterfall orchestrator with parallel Tier 1 and depth budgets | Accepted |
| [0004](./0004-domain-events-via-mediatr.md) | Domain events via MediatR, dispatched in `TracerDbContext.SaveChangesAsync` | Accepted |
| [0005](./0005-no-mock-database.md) | Integration tests run against real SQL Server (Testcontainers) | Accepted |
