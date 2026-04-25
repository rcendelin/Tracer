// Shared helpers for Tracer k6 load tests (B-86).
//
// All scripts import BASE_URL / API_KEY from the environment via __ENV.
// The wrapper in deploy/scripts/run-load-test.sh fails fast when either
// is missing, so scripts can assume non-empty values.

export function baseUrl() {
  const url = __ENV.BASE_URL;
  if (!url) {
    throw new Error('BASE_URL env var is required (e.g. https://tracer-test-api.azurewebsites.net).');
  }
  return url.replace(/\/$/, '');
}

export function apiKey() {
  const key = __ENV.API_KEY;
  if (!key) {
    throw new Error('API_KEY env var is required.');
  }
  return key;
}

export function headers() {
  return {
    'Content-Type': 'application/json',
    'X-Api-Key': apiKey(),
  };
}

// Fictitious payloads — no PII, no real company names, no real phone numbers.
// Country code is CZ so the waterfall hits the ARES provider path (Tier 1 JSON API).
export function sampleTracePayload(index = 0) {
  return {
    companyName: `Load Test Company ${String(index).padStart(3, '0')}`,
    country: 'CZ',
    industryHint: 'manufacturing',
    depth: 'Standard',
  };
}

export function quickTracePayload() {
  return {
    companyName: 'Contoso International Testing Ltd.',
    country: 'GB',
    depth: 'Quick',
  };
}

// Batch endpoint accepts a JSON array of TraceRequestDto directly (no wrapper).
export function sampleBatchPayload(size) {
  const items = [];
  for (let i = 0; i < size; i++) {
    items.push(sampleTracePayload(i));
  }
  return items;
}
