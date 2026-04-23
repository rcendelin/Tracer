// batch-load.js — low-rate batch submission load test.
//
// Purpose: validate POST /api/trace/batch returns 202 within 3 s for
// realistic batch sizes (50 items) while respecting the "batch" rate
// limit policy (5 req/min per IP).
//
// The 12 s sleep between iterations keeps the script at exactly 5
// requests per minute — never triggers the rate limiter. If this ever
// returns 429, treat it as a regression in the limiter configuration
// (QueueLimit should still be 0; the check exists to surface that).
//
// SLO:
//   - http_req_failed  < 1 %
//   - http_req_duration p(95) < 3000 ms
//   - iterations ==  5 (1 VU × 60 s / 12 s sleep)

import http from 'k6/http';
import { check, sleep } from 'k6';
import { baseUrl, headers, sampleBatchPayload } from './helpers.js';

const BATCH_SIZE = 50;

export const options = {
  vus: 1,
  duration: '1m',
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<3000'],
    'iterations': ['count==5'],
  },
};

export default function () {
  const payload = sampleBatchPayload(BATCH_SIZE);
  const res = http.post(
    `${baseUrl()}/api/trace/batch`,
    JSON.stringify(payload),
    { headers: headers() },
  );

  check(res, {
    'status is 202': (r) => r.status === 202,
    'items array present': (r) => Array.isArray(r.json('items')),
  });

  sleep(12);
}
