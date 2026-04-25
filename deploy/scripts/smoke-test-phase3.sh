#!/usr/bin/env bash
# B-78 — Phase 3 smoke test. Exercises Deep flow, AI extraction path,
# Change Feed, Validation Dashboard, manual override audit. Designed to
# run as the post-deploy gate after `az deployment group create` in
# `.github/workflows/deploy.yml`.
#
# Usage: ./smoke-test-phase3.sh https://<api-host> <API_KEY>
#
# Exits non-zero on the first failure with a clear message; designed to
# be a true SLO gate, not an advisory check.

set -euo pipefail

if [[ "$#" -ne 2 ]]; then
    echo "usage: $0 <base-url> <api-key>" >&2
    exit 64
fi

BASE_URL="${1%/}"
API_KEY="$2"

# -- helpers ---------------------------------------------------------------

step() {
    echo
    echo "== $1 =="
}

require_status() {
    local expected="$1" actual="$2" label="$3"
    if [[ "$expected" != "$actual" ]]; then
        echo "FAIL ($label): expected HTTP $expected, got $actual" >&2
        exit 1
    fi
}

curl_json() {
    # POST/PUT helpers accept @body inline or via -d.
    curl --silent --show-error --max-time 30 \
         --header "X-Api-Key: $API_KEY" \
         --header "Content-Type: application/json" \
         "$@"
}

# -- 1. health -------------------------------------------------------------

step "1. /health probe"
HEALTH_BODY="$(curl --silent --show-error --max-time 5 "$BASE_URL/health")"
echo "$HEALTH_BODY" | jq -e '.status == "Healthy" or .status == "Degraded"' >/dev/null \
    || { echo "FAIL: /health body unexpected — $HEALTH_BODY" >&2; exit 1; }

# -- 2. submit Deep trace --------------------------------------------------

step "2. POST /api/trace (Depth=Deep)"
TRACE_RESPONSE="$(curl_json --request POST --data '{
    "companyName": "Contoso International Testing Ltd.",
    "country": "DE",
    "depth": "Deep",
    "source": "smoke-test-phase3",
    "includeOfficers": false
}' "$BASE_URL/api/trace")"
TRACE_ID="$(echo "$TRACE_RESPONSE" | jq -r '.traceId // .id // empty')"
[[ -n "$TRACE_ID" ]] || { echo "FAIL: no traceId in $TRACE_RESPONSE" >&2; exit 1; }
echo "TraceId: $TRACE_ID"

# -- 3. poll until completed ----------------------------------------------

step "3. poll trace status"
deadline=$(( $(date +%s) + 60 ))
while :; do
    STATUS="$(curl_json "$BASE_URL/api/trace/$TRACE_ID" | jq -r '.status // empty')"
    case "$STATUS" in
        Completed|Failed) break ;;
    esac
    (( $(date +%s) > deadline )) && { echo "FAIL: timeout waiting for status (last: $STATUS)" >&2; exit 1; }
    sleep 2
done
[[ "$STATUS" == "Completed" ]] || { echo "FAIL: trace ended in $STATUS" >&2; exit 1; }
echo "Trace completed."

# -- 4. profile lookup -----------------------------------------------------

step "4. GET /api/profiles?country=DE"
PROFILE_PAGE="$(curl_json "$BASE_URL/api/profiles?country=DE&pageSize=10")"
PROFILE_COUNT="$(echo "$PROFILE_PAGE" | jq '.items | length')"
(( PROFILE_COUNT >= 1 )) || { echo "FAIL: no DE profiles returned ($PROFILE_PAGE)" >&2; exit 1; }
PROFILE_ID="$(echo "$PROFILE_PAGE" | jq -r '.items[0].id')"
echo "Picked profile $PROFILE_ID for downstream checks."

# -- 5. change stats -------------------------------------------------------

step "5. GET /api/changes/stats"
curl_json "$BASE_URL/api/changes/stats" | jq -e '.totalCount >= 0' >/dev/null \
    || { echo "FAIL: change stats malformed" >&2; exit 1; }

# -- 6. validation stats ---------------------------------------------------

step "6. GET /api/validation/stats"
curl_json "$BASE_URL/api/validation/stats" | jq -e '.pendingCount >= 0' >/dev/null \
    || { echo "FAIL: validation stats malformed" >&2; exit 1; }

# -- 7. recent change feed -------------------------------------------------

step "7. GET /api/changes?since=<now-1h>"
SINCE="$(date -u -d '1 hour ago' +'%Y-%m-%dT%H:%M:%SZ' 2>/dev/null \
        || date -u -v-1H +'%Y-%m-%dT%H:%M:%SZ')"
curl_json "$BASE_URL/api/changes?since=$SINCE&pageSize=5" \
    | jq -e '.items | type == "array"' >/dev/null \
    || { echo "FAIL: change feed since-filter malformed" >&2; exit 1; }

# -- 8. manual override (B-85) --------------------------------------------

step "8. PUT /api/profiles/{id}/fields/Phone (manual override)"
HTTP_CODE="$(curl_json --output /dev/null --write-out '%{http_code}' \
    --request PUT \
    --data '{"value": "+49 30 12345678", "reason": "B-78 phase 3 smoke test"}' \
    "$BASE_URL/api/profiles/$PROFILE_ID/fields/Phone")"
[[ "$HTTP_CODE" == "204" ]] \
    || { echo "FAIL: manual override expected 204, got $HTTP_CODE" >&2; exit 1; }

# -- 9. verify override appears in history --------------------------------

step "9. GET /api/profiles/{id}/history (verify ManualOverride entry)"
HISTORY="$(curl_json "$BASE_URL/api/profiles/$PROFILE_ID/history?pageSize=5")"
echo "$HISTORY" | jq -e '
    .changeEvents
    | map(select(.changeType == "ManualOverride" and (.detectedBy | startswith("manual-override:apikey:"))))
    | length >= 1' >/dev/null \
    || { echo "FAIL: ManualOverride entry not found in history" >&2; exit 1; }

step "✅ Phase 3 smoke test passed."
