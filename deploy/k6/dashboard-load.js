// dashboard-load.js — read-path load test for dashboard endpoints.
//
// Purpose: verify the cached read path stays below 500 ms at 50 VUs.
// Exercises the two endpoints that the Tracer UI hits on every dashboard
// render: /api/stats and /api/profiles (first page).
//
// SLO:
//   - http_req_failed  < 0.5 %
//   - http_req_duration p(95) < 500 ms
//
// Tracer caches profile reads in IDistributedCache; B-79 moves this to
// Redis so the first request warms the cache and subsequent requests
// validate the hit path.

import http from 'k6/http';
import { check } from 'k6';
import { baseUrl, headers } from './helpers.js';

export const options = {
  vus: 50,
  duration: '2m',
  thresholds: {
    http_req_failed: ['rate<0.005'],
    http_req_duration: ['p(95)<500'],
  },
};

export default function () {
  const h = headers();

  const stats = http.get(`${baseUrl()}/api/stats`, { headers: h });
  check(stats, {
    'stats 200': (r) => r.status === 200,
  });

  const profiles = http.get(`${baseUrl()}/api/profiles?page=0&pageSize=20`, { headers: h });
  check(profiles, {
    'profiles 200': (r) => r.status === 200,
    'profiles paged': (r) => Array.isArray(r.json('items')),
  });
}
