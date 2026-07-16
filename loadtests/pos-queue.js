// POS-queue — audit Block 17.
//
// Simulates several cash registers ringing up sales at the same time against
// ONE shared shift (PosService.OpenShiftAsync enforces "only one open shift
// per tenant" — every cashier at every register shares the same shift, same
// as in a real store). This is exactly the scenario Block 6 found and fixed
// a real bug in: concurrent sales racing on the same product_stock row's
// Quantity. The fix (migration 20260715054917_AddProductStockXminConcurrencyToken)
// added Postgres `xmin` optimistic concurrency to ProductStock; PosService.cs
// catches ConcurrencyConflictException and returns 409 ("Stock was updated
// concurrently by another sale. Please retry.") instead of silently
// overselling. Block 6 verified this with ~2 sequential/paired requests —
// this script re-verifies it under 20-50 REAL parallel requests.
//
// What "correct" looks like here:
//   - Every successful sale (201) actually decremented stock by exactly its
//     quantity — no lost updates, no double-decrements. This script cannot
//     see the DB directly (k6 has no Postgres driver in this project's
//     toolchain), so it exposes a Counter of total units sold; the audit
//     process cross-checks that Counter's final value against
//     SUM(product_stock.Quantity) before/after via psql (see
//     loadtests/README.md).
//   - Conflicts (409) and stock depletion (400 "Insufficient stock") are
//     EXPECTED outcomes under this load, not errors — only 5xx / network
//     failures count as unexpected_error.
//
// Usage:
//   k6 run loadtests/pos-queue.js
//   BASE_URL=http://localhost:5101 k6 run loadtests/pos-queue.js

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Rate, Trend } from 'k6/metrics';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5101';
const CASHIER_EMAIL = 'manager@demo.local'; // store_manager — satisfies CanAccessPos
const CASHIER_PASSWORD = 'password';

// High-stock seeded barcodes (Block 0 DbSeeder), chosen so 350 iterations at
// qty=1 stay within total available stock (~405 units) most of the run,
// with headroom for a handful of legitimate "insufficient stock" 400s near
// the end on the tightest item (buckwheat, 60 units) as depletion nears.
const BARCODES = [
  '4820001234535', // Цукор білий УКРЦУКОР — 100 units
  '4820001234501', // Молоко 2,5% Галичина — 93 units
  '4820001234531', // Рис круглозернистий Чумак — 80 units
  '4820001234541', // Вода Моршинська 1,5л — 72 units
  '4820001234532', // Гречка ядриця Жменька — 60 units
];

const saleCreated = new Counter('pos_sale_created_count');
const unitsSold = new Counter('pos_units_sold_count');
const conflict409 = new Counter('pos_conflict_409_count');
const insufficientStock400 = new Counter('pos_insufficient_stock_400_count');
const unexpectedError = new Rate('pos_unexpected_error_rate');
const saleDuration = new Trend('pos_sale_duration_ms', true);

export const options = {
  scenarios: {
    concurrent_registers: {
      executor: 'shared-iterations',
      vus: 40, // 40 simulated concurrent cash registers
      iterations: 350,
      maxDuration: '60s',
    },
  },
  thresholds: {
    pos_unexpected_error_rate: ['rate<0.01'],
    // Record-write budget per CLAUDE.md audit brief: P95 < 1s.
    pos_sale_duration_ms: ['p(95)<1000', 'p(99)<2000'],
  },
};

export function setup() {
  const loginRes = http.post(
    `${BASE_URL}/api/auth/login`,
    JSON.stringify({ email: CASHIER_EMAIL, password: CASHIER_PASSWORD }),
    { headers: { 'Content-Type': 'application/json' } }
  );
  if (loginRes.status !== 200) {
    throw new Error(`setup: login failed with status ${loginRes.status}: ${loginRes.body}`);
  }
  const token = loginRes.json('accessToken');
  const authHeaders = { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' };

  // Reuse an already-open shift if one exists (idempotent setup — lets this
  // script be re-run without manually closing the previous shift first).
  const currentRes = http.get(`${BASE_URL}/api/pos/shifts/current`, { headers: authHeaders });
  let shiftId;
  if (currentRes.status === 200) {
    shiftId = currentRes.json('shiftId');
  } else {
    const storeId = resolveStoreId(authHeaders);
    const openRes = http.post(
      `${BASE_URL}/api/pos/shifts/open`,
      JSON.stringify({ storeId, openingCash: 1000 }),
      { headers: authHeaders }
    );
    if (openRes.status !== 200) {
      throw new Error(`setup: could not open shift, status ${openRes.status}: ${openRes.body}`);
    }
    shiftId = openRes.json('shiftId');
  }

  return { token, shiftId };
}

function resolveStoreId(authHeaders) {
  // GET /api/pos/shifts/current returning 404 tells us nothing about which
  // store to use, so fall back to the single seeded demo store directly
  // (Магазин №1 — Центральний). If this ever needs to generalize to
  // multi-store staging data, resolve via GET /api/locations instead.
  const storeId = __ENV.STAGING_STORE_ID || '56cb968e-3ce2-4f13-a19b-83df2bee3c95';
  return storeId;
}

export default function (data) {
  const barcode = BARCODES[Math.floor(Math.random() * BARCODES.length)];
  const authHeaders = {
    Authorization: `Bearer ${data.token}`,
    'Content-Type': 'application/json',
  };

  const res = http.post(
    `${BASE_URL}/api/pos/sales`,
    JSON.stringify({
      shiftId: data.shiftId,
      items: [{ barcode, quantity: 1 }],
      paymentType: 'Cash',
      paymentAmount: 10000,
    }),
    { headers: authHeaders }
  );

  saleDuration.add(res.timings.duration);
  const isUnexpected = res.status === 0 || res.status >= 500;
  unexpectedError.add(isUnexpected ? 1 : 0);

  if (res.status === 201) {
    saleCreated.add(1);
    unitsSold.add(1);
  } else if (res.status === 409) {
    conflict409.add(1);
  } else if (res.status === 400) {
    insufficientStock400.add(1);
  }

  check(res, {
    'status is 201, 400, or 409 (no crash)': (r) =>
      r.status === 201 || r.status === 400 || r.status === 409,
  });

  sleep(0.05);
}
