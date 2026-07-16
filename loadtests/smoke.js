// k6 smoke test — proves the load-testing tool works end-to-end against the
// API. Not a real load scenario (see loadtests/README.md); full scenarios are
// audit Block 17.
//
// The API has no dedicated /health endpoint, so this hits the least-privileged
// public GET available: /api/marketplace/item-categories (MarketplaceController,
// [AllowAnonymous], no DB call, no tenant context) — see README.md for why.
//
// Usage:
//   k6 run loadtests/smoke.js
//   BASE_URL=http://localhost:5000 k6 run loadtests/smoke.js

import http from 'k6/http';
import { check, sleep } from 'k6';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5101';

export const options = {
  vus: 1,
  iterations: 5,
  thresholds: {
    http_req_failed: ['rate==0'],
    http_req_duration: ['p(95)<1000'],
  },
};

export default function () {
  const res = http.get(`${BASE_URL}/api/marketplace/item-categories`);

  check(res, {
    'status is 200': (r) => r.status === 200,
    'body is a JSON array': (r) => {
      try {
        return Array.isArray(JSON.parse(r.body));
      } catch {
        return false;
      }
    },
  });

  sleep(1);
}
