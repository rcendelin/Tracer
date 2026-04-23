# Tracer performance baselines

Last updated: _initial template — numbers are `TBD` until the first
calibrated run on stable hardware._

Baselines are **advisory**. The regression policy (see
[`README.md`](README.md)) is triggered by deltas against these numbers,
not by absolute values. Re-baseline after any deliberate refactor of a
measured service; record the old number in the change log at the
bottom.

## Environment

- **Runtime:** .NET 10 (GA when available, preview as of commit time)
- **Host class:** document CPU, RAM, OS when the first baseline is captured
- **Build:** `Release`, `ServerGC=true`, Tiered compilation default

## Micro-benchmarks (BenchmarkDotNet)

Run: `./deploy/scripts/run-benchmarks.sh` (or the `benchmarks` job in
`.github/workflows/perf.yml`).

### `CompanyNameNormalizer.Normalize`

| Input                              | Mean    | Allocated | Gen0 |
|------------------------------------|---------|-----------|------|
| `Contoso International Corp.`      | TBD     | TBD       | TBD  |
| `Škoda Auto a.s.`                  | TBD     | TBD       | TBD  |
| `Företag AB`                       | TBD     | TBD       | TBD  |
| `Société Générale S.A.`            | TBD     | TBD       | TBD  |
| `Deutsche Bank AG`                 | TBD     | TBD       | TBD  |
| `ООО Рога и Копыта`                | TBD     | TBD       | TBD  |
| `BHP Group Limited`                | TBD     | TBD       | TBD  |
| `Acme Intl. Holdings Ltd.`         | TBD     | TBD       | TBD  |

### `FuzzyNameMatcher.Score` — 10 representative pairs

| Metric     | Mean    | Allocated |
|------------|---------|-----------|
| `ScoreAllPairs` | TBD     | TBD       |

### `ConfidenceScorer.ScoreFields` — 8 fields × 3 candidates

| Metric     | Mean    | Allocated | Gen0 |
|------------|---------|-----------|------|
| `ScoreFields` | TBD     | TBD       | TBD  |

### `GoldenRecordMerger.Merge` — 4 provider results

| Metric     | Mean    | Allocated | Gen0 |
|------------|---------|-----------|------|
| `Merge`    | TBD     | TBD       | TBD  |

## Load tests (k6)

Run per environment; record the most recent summary here. Thresholds
inside the scripts enforce SLO gates.

### `trace-smoke.js` — Quick depth, 1 VU, 30 s

| Metric             | Threshold  | Latest   |
|--------------------|------------|----------|
| p95 duration       | < 5000 ms  | TBD      |
| error rate         | < 1 %      | TBD      |

### `trace-load.js` — Standard depth, ramp 1→25 VU, 5 min

| Metric             | Threshold  | Latest   |
|--------------------|------------|----------|
| p95 duration       | < 10000 ms | TBD      |
| error rate         | < 1 %      | TBD      |

### `batch-load.js` — 50 items/batch, 1 VU, 5 iterations

| Metric             | Threshold  | Latest   |
|--------------------|------------|----------|
| p95 duration       | < 3000 ms  | TBD      |
| iteration count    | == 5       | TBD      |
| error rate         | < 1 %      | TBD      |

### `dashboard-load.js` — 50 VU, 2 min

| Metric             | Threshold  | Latest   |
|--------------------|------------|----------|
| p95 duration       | < 500 ms   | TBD      |
| error rate         | < 0.5 %    | TBD      |

## Change log

| Date       | Change                                              | Reference   |
|------------|-----------------------------------------------------|-------------|
| 2026-04-22 | Initial template committed (B-86)                   | This branch |
