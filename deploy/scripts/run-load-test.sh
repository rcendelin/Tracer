#!/usr/bin/env bash
#
# Tracer k6 load test wrapper (B-86).
#
# Usage:
#   ./run-load-test.sh <SCRIPT> <BASE_URL> <API_KEY>
#
# Example:
#   ./run-load-test.sh trace-smoke https://tracer-test-api.azurewebsites.net abc123
#
# Script name matches a file in deploy/k6/<script>.js. Exits non-zero
# when any SLO threshold in the script fails (k6's default behaviour).
#
set -euo pipefail

SCRIPT="${1:?Usage: $0 <SCRIPT> <BASE_URL> <API_KEY>}"
BASE_URL="${2:?Usage: $0 <SCRIPT> <BASE_URL> <API_KEY>}"
API_KEY="${3:?Usage: $0 <SCRIPT> <BASE_URL> <API_KEY>}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/k6"
SCRIPT_PATH="${SCRIPT_DIR}/${SCRIPT}.js"

if [ ! -f "$SCRIPT_PATH" ]; then
  echo "Error: script not found: $SCRIPT_PATH" >&2
  echo "Available scripts: $(ls "$SCRIPT_DIR"/*.js 2>/dev/null | xargs -n1 basename | sed 's/\.js$//' | tr '\n' ' ')" >&2
  exit 1
fi

if ! command -v k6 >/dev/null 2>&1; then
  echo "Error: k6 is not installed. See https://k6.io/docs/get-started/installation/" >&2
  exit 1
fi

echo "=== Tracer Load Test: $SCRIPT ==="
echo "Target: $BASE_URL"
echo ""

BASE_URL="$BASE_URL" API_KEY="$API_KEY" k6 run "$SCRIPT_PATH"
