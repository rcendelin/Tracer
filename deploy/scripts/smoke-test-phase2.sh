#!/usr/bin/env bash
# Tracer — Phase 2 Smoke Test
# Tests: sync trace, batch, async Service Bus flow, SignalR hub connectivity
# Usage: ./smoke-test-phase2.sh <API_URL> <API_KEY>
# Example: ./smoke-test-phase2.sh https://tracer-test-api.azurewebsites.net my-api-key

set -euo pipefail

API_URL="${1:?Usage: $0 <API_URL> <API_KEY>}"
API_KEY="${2:?Usage: $0 <API_URL> <API_KEY>}"

# Trim trailing slash
API_URL="${API_URL%/}"

PASS=0
FAIL=0
ERRORS=()

# ────────────────────────────────────────────────────────────────
# Helpers
# ────────────────────────────────────────────────────────────────

green() { printf '\033[0;32m✓ %s\033[0m\n' "$*"; }
red()   { printf '\033[0;31m✗ %s\033[0m\n' "$*"; }
info()  { printf '\033[0;36m→ %s\033[0m\n' "$*"; }

pass() { ((PASS++)); green "$1"; }
fail() { ((FAIL++)); ERRORS+=("$1"); red "$1"; }

api_post() {
  local path="$1" body="$2"
  curl -s -w "\n%{http_code}" \
    -X POST "$API_URL$path" \
    -H "Content-Type: application/json" \
    -H "X-Api-Key: $API_KEY" \
    -d "$body" \
    --max-time 30
}

api_get() {
  local path="$1"
  curl -s -w "\n%{http_code}" \
    -X GET "$API_URL$path" \
    -H "X-Api-Key: $API_KEY" \
    --max-time 15
}

check_http() {
  local response="$1" expected_status="$2" test_name="$3"
  local status body
  status=$(echo "$response" | tail -1)
  body=$(echo "$response" | head -n -1)

  if [[ "$status" == "$expected_status" ]]; then
    pass "$test_name (HTTP $status)"
    echo "$body"
  else
    fail "$test_name — expected HTTP $expected_status, got $status"
    echo "$body"
    echo ""
  fi
}

# ────────────────────────────────────────────────────────────────
# 1. Health check
# ────────────────────────────────────────────────────────────────

info "=== 1. Health check ==="
response=$(api_get "/health" 2>/dev/null || true)
if echo "$response" | grep -q "200\|Healthy\|healthy"; then
  pass "GET /health — API is up"
else
  fail "GET /health — API not responding"
fi

# ────────────────────────────────────────────────────────────────
# 2. Single trace — 20 companies across 5 countries
# ────────────────────────────────────────────────────────────────

info "=== 2. Single trace — 20 companies (CZ, UK, AU, US, DE) ==="

trace_company() {
  local name="$1" reg_id="$2" country="$3" depth="${4:-Quick}"
  local body
  body=$(printf '{"companyName":"%s","registrationId":"%s","country":"%s","depth":"%s"}' \
    "$name" "$reg_id" "$country" "$depth")

  local response
  response=$(api_post "/api/trace" "$body" 2>/dev/null || echo "CONNECTION_FAILED\n000")
  local status body_only
  status=$(echo "$response" | tail -1)
  body_only=$(echo "$response" | head -n -1)

  if [[ "$status" == "201" ]]; then
    local trace_id
    trace_id=$(echo "$body_only" | grep -o '"traceId":"[^"]*"' | cut -d'"' -f4 || echo "")
    pass "POST /api/trace — $name ($country) → traceId: $trace_id"
    echo "$trace_id"
  else
    fail "POST /api/trace — $name ($country) → HTTP $status"
    echo ""
  fi
}

# CZ — ARES
trace_company "ŠKODA AUTO a.s." "00177041" "CZ" "Standard"
trace_company "Kofola ČeskoSlovensko a.s." "27605535" "CZ" "Quick"
trace_company "Česká spořitelna, a.s." "45244782" "CZ" "Quick"
trace_company "ČEZ, a. s." "45274649" "CZ" "Quick"

# UK — Companies House
trace_company "Tesco PLC" "" "GB" "Standard"
trace_company "HSBC Holdings plc" "" "GB" "Quick"
trace_company "BP p.l.c." "" "GB" "Quick"
trace_company "Rolls-Royce Holdings plc" "" "GB" "Quick"

# AU — ABN Lookup
trace_company "BHP Group Limited" "" "AU" "Standard"
trace_company "Commonwealth Bank of Australia" "" "AU" "Quick"
trace_company "Woolworths Group Limited" "" "AU" "Quick"
trace_company "Rio Tinto Limited" "" "AU" "Quick"

# US — SEC EDGAR
trace_company "Tesla, Inc." "" "US" "Standard"
trace_company "Apple Inc." "" "US" "Quick"
trace_company "Microsoft Corporation" "" "US" "Quick"
trace_company "Amazon.com, Inc." "" "US" "Quick"

# DE — GLEIF + Google Maps (no DE-specific registry yet)
trace_company "Siemens AG" "" "DE" "Standard"
trace_company "SAP SE" "" "DE" "Quick"
trace_company "BMW AG" "" "DE" "Quick"
trace_company "Volkswagen AG" "" "DE" "Quick"

# ────────────────────────────────────────────────────────────────
# 3. Batch endpoint
# ────────────────────────────────────────────────────────────────

info "=== 3. Batch endpoint ==="

batch_body='{"items":[
  {"companyName":"ŠKODA AUTO a.s.","registrationId":"00177041","country":"CZ","depth":"Quick","correlationId":"batch-cz-01"},
  {"companyName":"Tesco PLC","country":"GB","depth":"Quick","correlationId":"batch-uk-01"},
  {"companyName":"Apple Inc.","country":"US","depth":"Quick","correlationId":"batch-us-01"}
]}'

response=$(api_post "/api/trace/batch" "$batch_body" 2>/dev/null || echo "CONNECTION_FAILED\n000")
status=$(echo "$response" | tail -1)
body=$(echo "$response" | head -n -1)

if [[ "$status" == "202" ]]; then
  pass "POST /api/trace/batch — 3 items accepted (HTTP 202)"
  queued=$(echo "$body" | grep -o '"status":"Queued"' | wc -l || echo 0)
  info "Items with status Queued: $queued"
else
  fail "POST /api/trace/batch — expected HTTP 202, got $status"
fi

# ────────────────────────────────────────────────────────────────
# 4. Rate limiting — batch endpoint (6th request should 429)
# ────────────────────────────────────────────────────────────────

info "=== 4. Rate limiter — batch endpoint ==="

single_batch='{"items":[{"companyName":"Test","country":"CZ","depth":"Quick"}]}'
last_status=""
for i in {1..6}; do
  resp=$(api_post "/api/trace/batch" "$single_batch" 2>/dev/null || echo "000")
  last_status=$(echo "$resp" | tail -1)
  [[ "$last_status" == "429" ]] && break
done

if [[ "$last_status" == "429" ]]; then
  pass "Rate limiter returns HTTP 429 after 5 requests/minute"
else
  fail "Rate limiter did not trigger (last status: $last_status)"
fi

# ────────────────────────────────────────────────────────────────
# 5. Profile list (CKB)
# ────────────────────────────────────────────────────────────────

info "=== 5. CKB profile list ==="
response=$(api_get "/api/profiles?pageSize=10" 2>/dev/null || echo "CONNECTION_FAILED\n000")
check_http "$response" "200" "GET /api/profiles — CKB list endpoint"

# ────────────────────────────────────────────────────────────────
# 6. Change events
# ────────────────────────────────────────────────────────────────

info "=== 6. Change events ==="
response=$(api_get "/api/changes?pageSize=5" 2>/dev/null || echo "CONNECTION_FAILED\n000")
check_http "$response" "200" "GET /api/changes — Change feed endpoint"

response=$(api_get "/api/changes/stats" 2>/dev/null || echo "CONNECTION_FAILED\n000")
check_http "$response" "200" "GET /api/changes/stats — Change statistics"

# ────────────────────────────────────────────────────────────────
# 7. Validation
# ────────────────────────────────────────────────────────────────

info "=== 7. Validation endpoints ==="
response=$(api_get "/api/validation/stats" 2>/dev/null || echo "CONNECTION_FAILED\n000")
check_http "$response" "200" "GET /api/validation/stats — Validation statistics"

# ────────────────────────────────────────────────────────────────
# 8. SignalR hub negotiation
# ────────────────────────────────────────────────────────────────

info "=== 8. SignalR hub ==="
response=$(curl -s -w "\n%{http_code}" \
  -X POST "$API_URL/hubs/trace/negotiate?negotiateVersion=1" \
  -H "X-Api-Key: $API_KEY" \
  --max-time 10 2>/dev/null || echo "CONNECTION_FAILED\n000")
status=$(echo "$response" | tail -1)
body=$(echo "$response" | head -n -1)

if [[ "$status" == "200" ]]; then
  conn_token=$(echo "$body" | grep -o '"connectionToken":"[^"]*"' | cut -d'"' -f4 || echo "")
  if [[ -n "$conn_token" ]]; then
    pass "SignalR /hubs/trace/negotiate — connectionToken received"
  else
    fail "SignalR /hubs/trace/negotiate — no connectionToken in response"
  fi
else
  fail "SignalR /hubs/trace/negotiate — HTTP $status"
fi

# ────────────────────────────────────────────────────────────────
# 9. Auth — invalid key rejected
# ────────────────────────────────────────────────────────────────

info "=== 9. Auth middleware ==="
response=$(curl -s -w "\n%{http_code}" \
  -X GET "$API_URL/api/profiles" \
  -H "X-Api-Key: invalid-key-xxx" \
  --max-time 10 2>/dev/null || echo "CONNECTION_FAILED\n000")
status=$(echo "$response" | tail -1)
if [[ "$status" == "401" ]]; then
  pass "Auth middleware rejects invalid API key (HTTP 401)"
else
  fail "Auth middleware — expected 401, got $status"
fi

# ────────────────────────────────────────────────────────────────
# Summary
# ────────────────────────────────────────────────────────────────

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printf "Results: \033[0;32m%d passed\033[0m, \033[0;31m%d failed\033[0m\n" "$PASS" "$FAIL"

if [[ "${#ERRORS[@]}" -gt 0 ]]; then
  echo ""
  echo "Failed tests:"
  for err in "${ERRORS[@]}"; do
    red "  • $err"
  done
fi

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

if [[ "$FAIL" -gt 0 ]]; then
  exit 1
fi
