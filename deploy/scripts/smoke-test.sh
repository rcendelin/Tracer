#!/usr/bin/env bash
set -euo pipefail

# Tracer Smoke Test — validates a deployed environment
# Usage: ./smoke-test.sh <BASE_URL> [API_KEY]
# Example: ./smoke-test.sh https://tracer-test-api.azurewebsites.net my-api-key

BASE_URL="${1:?Usage: $0 <BASE_URL> [API_KEY]}"
API_KEY="${2:-}"
PASS=0
FAIL=0

header=""
if [ -n "$API_KEY" ]; then
  header="-H X-Api-Key:${API_KEY}"
fi

echo "=== Tracer Smoke Test ==="
echo "Target: $BASE_URL"
echo ""

# Helper function
check() {
  local name="$1"
  local expected_status="$2"
  local method="$3"
  local url="$4"
  local body="${5:-}"

  local args=(-s -o /tmp/tracer-smoke-response.json -w "%{http_code}" $header)
  if [ "$method" = "POST" ]; then
    args+=(-X POST -H "Content-Type: application/json" -d "$body")
  fi

  local status
  status=$(curl "${args[@]}" "$url")

  if [ "$status" = "$expected_status" ]; then
    echo "  PASS: $name (HTTP $status)"
    PASS=$((PASS + 1))
  else
    echo "  FAIL: $name (expected $expected_status, got $status)"
    cat /tmp/tracer-smoke-response.json 2>/dev/null || true
    echo ""
    FAIL=$((FAIL + 1))
  fi
}

# 1. Health check
echo "--- Health ---"
check "Health check" "200" "GET" "$BASE_URL/health"

# 2. Stats endpoint
echo "--- Stats ---"
check "Dashboard stats" "200" "GET" "$BASE_URL/api/stats"

# 3. List traces (empty is OK)
echo "--- Traces ---"
check "List traces" "200" "GET" "$BASE_URL/api/trace?page=0&pageSize=5"

# 4. Submit trace — Czech company (ARES)
echo "--- Submit Traces ---"
check "Trace: Skoda Auto CZ" "201" "POST" "$BASE_URL/api/trace" \
  '{"companyName":"Skoda Auto","country":"CZ","registrationId":"00027006","depth":"Quick"}'

check "Trace: CEZ Group CZ" "201" "POST" "$BASE_URL/api/trace" \
  '{"companyName":"CEZ","country":"CZ","registrationId":"45274649","depth":"Quick"}'

check "Trace: Komerční banka CZ" "201" "POST" "$BASE_URL/api/trace" \
  '{"companyName":"Komercni banka","country":"CZ","depth":"Quick"}'

# 5. Submit trace — international (GLEIF + Google Maps)
check "Trace: Volkswagen DE" "201" "POST" "$BASE_URL/api/trace" \
  '{"companyName":"Volkswagen AG","country":"DE","depth":"Quick"}'

check "Trace: Apple US" "201" "POST" "$BASE_URL/api/trace" \
  '{"companyName":"Apple Inc","country":"US","depth":"Quick"}'

# 6. List traces after submissions
echo "--- Verify ---"
check "List traces (after submit)" "200" "GET" "$BASE_URL/api/trace?page=0&pageSize=10"

# 7. List profiles
check "List profiles" "200" "GET" "$BASE_URL/api/profiles?page=0&pageSize=10"

# 8. Non-existent trace → 404
check "Non-existent trace" "404" "GET" "$BASE_URL/api/trace/00000000-0000-0000-0000-000000000000"

# 9. Non-existent profile → 404
check "Non-existent profile" "404" "GET" "$BASE_URL/api/profiles/00000000-0000-0000-0000-000000000000"

# Summary
echo ""
echo "=== Results ==="
echo "  Passed: $PASS"
echo "  Failed: $FAIL"
echo "  Total:  $((PASS + FAIL))"

if [ "$FAIL" -gt 0 ]; then
  echo ""
  echo "SMOKE TEST FAILED"
  exit 1
else
  echo ""
  echo "SMOKE TEST PASSED"
fi
