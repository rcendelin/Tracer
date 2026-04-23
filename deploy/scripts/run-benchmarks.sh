#!/usr/bin/env bash
#
# Tracer BenchmarkDotNet runner (B-86).
#
# Usage:
#   ./run-benchmarks.sh [BDN_FILTER]
#
# Example:
#   ./run-benchmarks.sh                    # run all benchmarks
#   ./run-benchmarks.sh "*NameNormalizer*" # run only the normalizer benchmark
#
# BenchmarkDotNet writes artifacts to ./BenchmarkDotNet.Artifacts/ in the
# current working directory. Requires a Release build — the script enforces
# it. Exits non-zero on build or run failure.
#
set -euo pipefail

FILTER="${1:-*}"

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PROJECT="${REPO_ROOT}/tests/Tracer.Benchmarks/Tracer.Benchmarks.csproj"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "Error: .NET SDK not found on PATH." >&2
  exit 1
fi

echo "=== Tracer Benchmarks ==="
echo "Project: $PROJECT"
echo "Filter:  $FILTER"
echo ""

cd "$REPO_ROOT"
dotnet run --project "$PROJECT" --configuration Release -- --filter "$FILTER"
