// Breakpoint test — TASK-402.
//
// Finds the point at which the staging stack stops keeping up: VUs ramp in
// stages against a representative authenticated dashboard-read endpoint
// until either the error rate or p95 latency threshold breaches, at which
// point k6 aborts the run (abortOnFail) and the last clean stage is the
// answer to "how many concurrent users can this handle."
//
// Deliberately NOT a login-storm (that's covered separately and would
// conflate the bcrypt latency floor with capacity headroom) — every VU
// shares one token from setup(), isolating this run to read-path capacity.
//
// Usage:
//   k6 run loadtests/breakpoint.js
//   BASE_URL=http://localhost:5101 k6 run loadtests/breakpoint.js
//   k6 run --summary-export=breakpoint-summary.json loadtests/breakpoint.js

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate, Trend } from 'k6/metrics';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5101';
const USER_EMAIL = 'manager@demo.local';
const USER_PASSWORD = 'password';
const STORE_ID = __ENV.STAGING_STORE_ID || '56cb968e-3ce2-4f13-a19b-83df2bee3c95';

// Same widget mix as analytics-concurrent-read.js — a realistic dashboard
// load, not a single cherry-picked cheap endpoint.
const ENDPOINTS = [
  `/api/analytics/expiry-summary?store_id=${STORE_ID}`,
  `/api/analytics/dashboard/weekly-kpi?store_id=${STORE_ID}`,
  `/api/analytics/by-category?store_id=${STORE_ID}`,
  `/api/stock/summary?store_id=${STORE_ID}`,
];

const errorRate = new Rate('breakpoint_error_rate');
const reqDuration = new Trend('breakpoint_duration_ms', true);

export const options = {
  scenarios: {
    ramp: {
      executor: 'ramping-vus',
      startVUs: 0,
      // Each stage's VU count is the "how many concurrent users" candidate
      // reported if the thresholds below breach during that stage.
      stages: [
        { duration: '20s', target: 20 },
        { duration: '20s', target: 50 },
        { duration: '20s', target: 100 },
        { duration: '20s', target: 200 },
        { duration: '20s', target: 400 },
        { duration: '20s', target: 800 },
        { duration: '30s', target: 800 }, // hold at the top to see if it's sustainable, not just a momentary spike
      ],
    },
  },
  thresholds: {
    // Abort as soon as the app is genuinely struggling — errors or latency
    // blown past a "would a real user tolerate this" bar — rather than
    // running the full ramp and reporting a misleadingly high VU number.
    breakpoint_error_rate: [{ threshold: 'rate<0.05', abortOnFail: true, delayAbortEval: '10s' }],
    breakpoint_duration_ms: [{ threshold: 'p(95)<3000', abortOnFail: true, delayAbortEval: '10s' }],
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
  const path = ENDPOINTS[Math.floor(Math.random() * ENDPOINTS.length)];
  const res = http.get(`${BASE_URL}${path}`, {
    headers: { Authorization: `Bearer ${data.token}` },
    tags: { name: 'dashboard_read' },
  });

  reqDuration.add(res.timings.duration);
  const failed = res.status === 0 || res.status >= 500;
  errorRate.add(failed ? 1 : 0);

  check(res, { 'status is 200': (r) => r.status === 200 });
  sleep(0.2);
}
