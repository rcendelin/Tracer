// trace-smoke.js — single-user smoke test for POST /api/trace (Quick depth).
//
// Purpose: prove the enrichment path returns 201 in under 5 s under zero
// concurrency. Run after every deployment as a sanity gate.
//
// SLO:
//   - http_req_failed  < 1 %
//   - http_req_duration p(95) < 5000 ms   (Quick depth target)

import http from 'k6/http';
import { check, sleep } from 'k6';
import { baseUrl, headers, quickTracePayload } from './helpers.js';

export const options = {
  vus: 1,
  duration: '30s',
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<5000'],
  },
};

export default function () {
  const res = http.post(
    `${baseUrl()}/api/trace`,
    JSON.stringify(quickTracePayload()),
    { headers: headers() },
  );

  check(res, {
    'status is 201': (r) => r.status === 201,
    'traceId returned': (r) => r.json('traceId') !== undefined,
  });

  sleep(1);
}
