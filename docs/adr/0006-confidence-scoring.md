# ADR 0006 — Confidence scoring: continuous freshness + N-tier verification

**Status.** Accepted (B-96).

## Context

`ConfidenceScorer` (Application layer) blends four dimensions into a per-field
confidence score `[0, 1]`:

| Dimension | Weight | Pre-B-96 implementation |
|---|---|---|
| Source quality | 30% | Provider-reported `Confidence.Value` (continuous, OK) |
| Freshness | 20% | **4-tier step function** by age of `EnrichedAt` |
| Cross-validation | 25% | 3-tier (1+/2/3+ distinct sources) |
| Verification | 25% | **3-tier** (registry / geo / other) |

Two of the four dimensions were discrete step functions. The Byznys spec
§8.2 describes the scoring as continuous in spirit ("freshness gradually
decays"), and the step boundaries produced visible jumps in the score for
fields that crossed a boundary day-over-day — a profile last enriched 29
days ago scored `0.8`, but at 30 days suddenly `0.5`.

The 3-tier `Verification` collapsed every Tier 2 registry scraper
(Handelsregister, LATAM, State SoS, BrasilAPI CNPJ) into the same
`0.4` bucket as the AI extractor — even though scrapers consume
public-record data and should rank above generic AI extraction.

## Decision

**Freshness becomes continuous.** Linear decay from `1.0` at 0 days to a
floor of `0.3` at 90 days, then flat at the floor:

```csharp
var linear = 1.0 - (ageDays / FreshnessFullDecayDays);
return Math.Max(FreshnessFloor, linear);
```

Same endpoints as the old step function (1.0 at fresh, 0.3 floor) but no
discontinuities. Endpoint constants captured as private statics
(`FreshnessFullDecayDays = 90.0`, `FreshnessFloor = 0.3`) so future tuning
is one-place.

**Verification gets four extra tiers** (manual override / Tier 1 global /
Tier 2 registry scraper / Tier 2 geo / Tier 3 AI / unknown):

| Source group | Score |
|---|---|
| Manual operator override (`manual-override:*`) | 1.00 |
| Tier 1 national registry (ARES, Companies House, ABN, SEC EDGAR) | 1.00 |
| Tier 1 global (GLEIF) | 0.95 |
| Tier 2 registry scraper (Handelsregister, State SoS, CNPJ, LATAM family) | 0.85 |
| Tier 2 geo (Google Maps, Azure Maps) | 0.70 |
| Tier 3 / unknown (AI extractor, web scraper, anything else) | 0.40 |

The buckets match `IEnrichmentProvider.Priority` tiers documented in
`docs/providers.md` and `CLAUDE.md`. Manual override (B-85) is recognised
explicitly: operator-set values are by definition authoritative for the
field they touch.

## Consequences

**Positive.**
- Day-over-day re-validation no longer surfaces apparent confidence drops
  caused only by step boundaries — the Validation Dashboard shows a
  smoother trend.
- Tier 2 registry scrapers (Handelsregister with its rate-limited German
  Data Usage Act compliance, LATAM family, State SoS) are correctly ranked
  above the AI extractor — the orchestrator's `SelectBestCandidate` falls
  back to AI only when registry data is genuinely missing.
- Manual operator overrides (B-85) feed in with `1.0` verification, making
  them the highest-confidence values for the touched field.

**Negative / risk.**
- Existing CKB profiles have `OverallConfidence` precomputed with the old
  formula. Until each profile is re-enriched (next manual or scheduled
  re-validation), persisted scores will reflect the old discrete formula
  while new traces emit the continuous formula. Acceptable: the dashboard
  reports a slow drift toward smoother values over `Revalidation:FieldTtl`.
- The four-tier verification table grows the source-string vocabulary
  Tracer cares about; new providers must be classified into one of the
  five groups. Not classifying = unknown = `0.40` — degrades gracefully.

**Neutral.**
- Weights themselves (`30/20/25/25`) are unchanged — this ADR is about the
  shape of two dimensions, not their relative importance.

## Verification

- `tests/Tracer.Benchmarks/Benchmarks/ConfidenceScorerBenchmarks.cs`
  (B-86) re-runs after this change. Old baselines in
  `docs/performance/baselines.md` will be regenerated; absolute output
  values will shift slightly downward for medium-aged data, slightly
  upward for Tier 2 scraper sources — both expected.
- Unit tests pending — sandbox lacks `dotnet`; CI is the gate. Tests
  should cover: continuous freshness at 0 / 30 / 60 / 90 / 365 days,
  N-tier verification for one provider per group, manual-override prefix
  recognition.

## Related

- ADR 0002 — `TracedField<T>` carries `EnrichedAt` (input to freshness).
- ADR 0003 — Provider priority tiers (input to verification grouping).
- B-85 — Manual override audit (the `manual-override:*` source convention).
- B-86 — BenchmarkDotNet harness used to gate regressions on this scorer.
- `docs/providers.md` — provider catalogue with the same Tier 1/2/3
  classification.
