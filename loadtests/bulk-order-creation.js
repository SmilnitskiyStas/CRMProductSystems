// Bulk order creation — audit Block 17.
//
// Task brief allowed either "AI-замовлення чи звичайні Orders". This script
// deliberately targets POST /api/orders/calculate (OrdersController ->
// OrderCalcService.CalculateAsync — the non-AI order-formula computation:
// ADU + CDA buffer aggregation, no Claude API call, no persistence) instead
// of POST /api/ai-orders/generate.
//
// Why not the AI endpoint: /api/ai-orders/generate calls the real Claude API
// per request (ShelfGuard.Infrastructure/AI/ClaudeOrderAdvisor) — firing 20-50
// of those concurrently would burn real Anthropic budget for no additional
// audit signal, since Block 7 already reviewed that flow's error handling and
// call-volume hygiene end to end. /api/orders/calculate exercises the same
// "compute a bulk order recommendation" code path's DB-aggregation cost
// (joins across product_adu / product_buffer / product_stock) under
// concurrency, which is the actual thing Block 17 is checking for
// (connection-pool pressure, N+1 surfacing only under load) — without the
// AI cost.
//
// Usage:
//   k6 run loadtests/bulk-order-creation.js
//   BASE_URL=http://localhost:5101 k6 run loadtests/bulk-order-creation.js

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate, Trend } from 'k6/metrics';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5101';
const USER_EMAIL = 'manager@demo.local';
const USER_PASSWORD = 'password';
const STORE_ID = __ENV.STAGING_STORE_ID || '56cb968e-3ce2-4f13-a19b-83df2bee3c95';

const calcSuccess = new Rate('order_calc_success_rate');
const calcUnexpectedError = new Rate('order_calc_unexpected_error_rate');
const calcDuration = new Trend('order_calc_duration_ms', true);

export const options = {
  scenarios: {
    bulk_calculate: {
      executor: 'constant-vus',
      vus: 30,
      duration: '20s',
    },
  },
  thresholds: {
    order_calc_unexpected_error_rate: ['rate<0.01'],
    order_calc_success_rate: ['rate>0.99'],
    // Aggregation query, not a plain GET — documented as a middle-ground
    // budget between the brief's 500ms read / 1s write bars.
    order_calc_duration_ms: ['p(95)<800', 'p(99)<1500'],
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
  const authHeaders = {
    Authorization: `Bearer ${data.token}`,
    'Content-Type': 'application/json',
  };

  const res = http.post(
    `${BASE_URL}/api/orders/calculate`,
    JSON.stringify({ storeId: STORE_ID }),
    { headers: authHeaders }
  );

  calcDuration.add(res.timings.duration);
  const isUnexpected = res.status === 0 || res.status >= 500;
  calcUnexpectedError.add(isUnexpected ? 1 : 0);
  calcSuccess.add(res.status === 200 ? 1 : 0);

  check(res, {
    'status is 200': (r) => r.status === 200,
  });

  sleep(0.2);
}
