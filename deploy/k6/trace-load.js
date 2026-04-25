// trace-load.js — ramp-up load test for POST /api/trace (Standard depth).
//
// Purpose: hold Standard-depth trace latency within the <10 s target
// while 25 virtual users submit concurrently for 5 minutes.
//
// SLO:
//   - http_req_failed  < 1 %
//   - http_req_duration p(95) < 10000 ms  (Standard depth target)

import http from 'k6/http';
import { check } from 'k6';
import { baseUrl, headers, sampleTracePayload } from './helpers.js';

export const options = {
  stages: [
    { duration: '30s', target: 5 },
    { duration: '1m',  target: 15 },
    { duration: '3m',  target: 25 },
    { duration: '30s', target: 0 },
  ],
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<10000'],
  },
};

export default function () {
  const payload = sampleTracePayload(__VU * 1000 + __ITER);
  const res = http.post(
    `${baseUrl()}/api/trace`,
    JSON.stringify(payload),
    { headers: headers() },
  );

  check(res, {
    'status is 201': (r) => r.status === 201,
  });
}
