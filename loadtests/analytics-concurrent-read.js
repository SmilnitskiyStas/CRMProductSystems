// Analytics/dashboard under concurrent reads — audit Block 17.
//
// Hammers the read-heavy analytics/stock endpoints the dashboard renders,
// designed to be run AT THE SAME TIME as pos-queue.js (the one scenario in
// this audit that actually writes rows), so the reads and writes contend for
// the same Postgres connection pool and the same product_stock rows — the
// concurrent-read-vs-write case the audit brief calls out explicitly.
// Run standalone it's still a useful pure-read latency/N+1 check on its own.
//
// Usage:
//   k6 run loadtests/analytics-concurrent-read.js
//   BASE_URL=http://localhost:5101 k6 run loadtests/analytics-concurrent-read.js
//
// To reproduce the concurrent read+write case, start this in one terminal
// and loadtests/pos-queue.js in another at roughly the same time (see
// loadtests/README.md).

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate, Trend } from 'k6/metrics';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5101';
const USER_EMAIL = 'manager@demo.local';
const USER_PASSWORD = 'password';
const STORE_ID = __ENV.STAGING_STORE_ID || '56cb968e-3ce2-4f13-a19b-83df2bee3c95';

// One entry per dashboard widget these correspond to; each is tagged
// separately so the k6 summary breaks down latency per endpoint, not just
// as one blended number.
const ENDPOINTS = [
  { name: 'expiry_summary', path: `/api/analytics/expiry-summary?store_id=${STORE_ID}` },
  { name: 'weekly_kpi', path: `/api/analytics/dashboard/weekly-kpi?store_id=${STORE_ID}` },
  { name: 'by_category', path: `/api/analytics/by-category?store_id=${STORE_ID}` },
  { name: 'movements', path: `/api/analytics/movements?store_id=${STORE_ID}` },
  { name: 'pos_summary', path: `/api/analytics/pos/summary?store_id=${STORE_ID}` },
  { name: 'stock_summary', path: `/api/stock/summary?store_id=${STORE_ID}` },
];

const readSuccess = new Rate('analytics_read_success_rate');
const readUnexpectedError = new Rate('analytics_read_unexpected_error_rate');
const readDuration = new Trend('analytics_read_duration_ms', true);

export const options = {
  scenarios: {
    concurrent_dashboard_reads: {
      executor: 'constant-vus',
      vus: 30,
      duration: '25s', // slightly longer than pos-queue.js's 20s window so
                        // the overlap covers that whole write burst when run
                        // side by side.
    },
  },
  thresholds: {
    analytics_read_unexpected_error_rate: ['rate<0.01'],
    analytics_read_success_rate: ['rate>0.99'],
    // Plain read budget per the audit brief: P95 < 500ms.
    analytics_read_duration_ms: ['p(95)<500', 'p(99)<1000'],
  },
};

export function setup() {
  const loginRes = http.post(
    `${BASE_URL}/api/auth/login`,
    JSON.stringify({ email: USER_EMAIL, password: USER_PASSWORD }),
    { headers: { 'Content-Type': 'application/json' } }
  );
  if (loginRes.status !== 200) {
    throw new Error(`setup: login failed with status ${loginRes.status}: ${loginRes.body}`);
  }
  return { token: loginRes.json('accessToken') };
}

export default function (data) {
  const endpoint = ENDPOINTS[Math.floor(Math.random() * ENDPOINTS.length)];
  const res = http.get(`${BASE_URL}${endpoint.path}`, {
    headers: { Authorization: `Bearer ${data.token}` },
    tags: { endpoint: endpoint.name },
  });

  readDuration.add(res.timings.duration, { endpoint: endpoint.name });
  const isUnexpected = res.status === 0 || res.status >= 500;
  readUnexpectedError.add(isUnexpected ? 1 : 0);
  readSuccess.add(res.status === 200 ? 1 : 0);

  check(res, {
    'status is 200': (r) => r.status === 200,
  });

  sleep(0.1);
}
