# ADR 0003 — Waterfall orchestrator with parallel Tier 1 and depth budgets

**Status.** Accepted (B-17, refined in B-39).

## Context

Tracer pulls from 12+ providers with very different latency profiles
(ARES JSON: 200 ms, Handelsregister scrape: 5 s, AI extractor: 10 s).
A naive sequential pass would take 30 s+ per request. A naive parallel
pass would punish weaker registries (rate limits, transient failures)
without providing the user any benefit.

## Decision

Run providers in **tiers**, ordered by `IEnrichmentProvider.Priority`:

| Priority range | Tier | Execution | Per-provider timeout |
|---|---|---|---|
| ≤ 100 | 1 — Registry / Geo APIs | **Parallel** via `Task.WhenAll` | 8 s |
| 101 – 200 | 2 — Registry scrapers | **Sequential** | 12 s |
| > 200 | 3 — AI / LLM | **Sequential**, only when Depth = `Deep` | 20 s |

Each request carries a `TraceDepth` enum (`Quick` / `Standard` / `Deep`)
which gates which tiers run AND the **total budget**:

| Depth | Tier 1 | Tier 2 | Tier 3 | Total budget |
|---|---|---|---|---|
| `Quick` | yes | no | no | 5 s |
| `Standard` | yes | yes | no | 15 s |
| `Deep` | yes | yes | yes (LLM only) | 30 s |

When the budget expires mid-flight, the orchestrator cancels in-flight
work and returns whatever has accumulated. **No throw on partial
results** — `ProviderResult.Timeout` is treated as a benign signal.

## Consequences

**Positive.**
- Quick traces from ARES + GLEIF respond in < 1 s wall time on warm cache.
- Tier 2 scrapers don't penalise the API path on Quick — they don't run.
- Tier 3 is reserved for explicit Deep traces; users opt in with eyes open.

**Negative.**
- The `OperationCanceledException` discrimination in
  `ExecuteProviderWithTimeoutAsync` is subtle (per-provider timeout vs
  budget exhaustion vs caller cancellation). Documented in CLAUDE.md and
  enforced by tests.
- Tier 1 parallelism means providers MUST NOT touch the EF DbContext
  (Scoped, not thread-safe). Any CKB context is passed via
  `TraceContext.ExistingProfile` / `AccumulatedFields`.

**Neutral.**
- Adding a new source means picking a priority — pick `≤ 100` only if
  the source is genuinely registry-grade and stable. Scrapers default to
  150–200; LLM steps to 250+.
- Providers register `SourceQuality ∈ [0, 1]` independently of priority.
  Quality feeds `ConfidenceScorer`; priority feeds the orchestrator.
  Don't conflate them.

## Related

- ADR 0001 — Clean Architecture.
- ADR 0002 — `TracedField<T>` (carries `Source` + `Confidence`).
- B-58 Deep pipeline branch (introduced Tier 3 + Depth gating).
- CLAUDE.md "Waterfall orchestrator" bullet.
