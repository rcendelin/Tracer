# Tracer performance testing (B-86)

Tracer ships two complementary perf harnesses:

1. **Micro-benchmarks** (`tests/Tracer.Benchmarks/`) — BenchmarkDotNet,
   measures hot-path services in isolation. Run locally or via the
   `Performance` GitHub workflow.
2. **Load tests** (`deploy/k6/`) — k6 scripts that exercise HTTP endpoints
   under realistic concurrency. Thresholds encode the SLOs from `CLAUDE.md`;
   k6 exits non-zero when any SLO fails.

Neither suite runs on the default CI pipeline. Both are opt-in:
`workflow_dispatch` on `Performance`, or manual invocation via the helper
scripts under `deploy/scripts/`.

## SLO targets

| Endpoint / service                 | Metric                      | Target  |
|------------------------------------|-----------------------------|---------|
| `POST /api/trace` (Quick depth)    | p95 latency                 | < 5 s   |
| `POST /api/trace` (Standard depth) | p95 latency @ 25 VU         | < 10 s  |
| `POST /api/trace/batch` (50 items) | p95 latency                 | < 3 s   |
| `GET /api/stats` + `/api/profiles` | p95 latency @ 50 VU         | < 500 ms|
| `ConfidenceScorer.ScoreFields`     | mean latency (8×3 input)    | < 50 µs |
| `CompanyNameNormalizer.Normalize`  | mean latency                | < 20 µs |
| `FuzzyNameMatcher.Score` (10 pairs)| mean latency                | < 100 µs|
| `GoldenRecordMerger.Merge` (4×)    | mean latency                | < 100 µs|

The micro-benchmark targets are **initial estimates**. First real runs
on representative hardware should replace them in
[`baselines.md`](baselines.md); any regression larger than 20 % in mean
or 10 % in allocated bytes is investigated before the PR lands.

## Running locally

### Benchmarks

```bash
# All benchmarks
./deploy/scripts/run-benchmarks.sh

# Single benchmark class
./deploy/scripts/run-benchmarks.sh "*FuzzyMatcher*"
```

Outputs land in `./BenchmarkDotNet.Artifacts/`. Commit nothing from that
folder — only the summary numbers in `baselines.md`.

### Load tests

```bash
./deploy/scripts/run-load-test.sh trace-smoke https://tracer-test-api.azurewebsites.net "$API_KEY"
```

The wrapper fails fast when `BASE_URL` or `API_KEY` is missing, and it
surfaces the list of available scenarios when the name is misspelled.
Thresholds inside each script cause k6 to exit non-zero when an SLO
fails, so the command can be used as a deployment gate.

## Running in CI

`.github/workflows/perf.yml` exposes three jobs via `workflow_dispatch`:

- `benchmarks` — installs .NET and runs BenchmarkDotNet with JSON +
  Markdown exporters. Artifacts retained 90 days.
- `load-test` — installs k6 and runs the chosen scenario against the
  provided `base_url` using the `PERF_API_KEY` repository secret.
- `both` — runs both in parallel.

The `base_url` input is required when `job` includes `load-test`. The
job explicitly fails if `PERF_API_KEY` is unset so the maintainer gets
a clear error rather than an authenticated 401 stampede against the
target environment.

## Interpreting results

### BenchmarkDotNet

Columns to watch:

- **Mean** — the primary comparison metric.
- **Allocated** — any increase > 10 % without corresponding Mean decrease
  is a regression.
- **Gen0 / Gen1** — non-zero GC collections in a hot path are a smell;
  investigate before merging.

Compare against `baselines.md`. Paste the BenchmarkDotNet summary table
into the PR description for any code touching the benchmarked services.

### k6

Key thresholds in every scenario:

- `http_req_failed` — must stay below the configured error budget.
- `http_req_duration p(95)` — the SLO gate.

k6 produces a summary table and a JSON stream (when `--out json=…` is
used, as in the CI job). Ingest into Grafana or Datadog for trend
analysis. A single failing threshold is enough for k6 to exit non-zero.

## Regression policy

| Source             | Trigger                                              | Action                          |
|--------------------|------------------------------------------------------|---------------------------------|
| BenchmarkDotNet    | > 20 % Mean OR > 10 % Allocated regression           | Investigate, document, or roll back. |
| BenchmarkDotNet    | Non-zero Gen0/Gen1 in a benchmark previously clean   | Investigate allocation source.  |
| k6                 | Any threshold fails in CI job                        | Job exits non-zero, blocks the deploy. |
| k6                 | p95 creep ≥ 20 % across three consecutive runs       | File follow-up issue.           |

Both harnesses are advisory, not CI-gating — they will not block PR
merges. They **do** block deployments when invoked as part of the
release workflow (future B-78 step).
