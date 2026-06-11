# BUG-003 Resolution — GET /api/analytics/summary → 404
**Agent:** qa-tester
**Date:** 2026-06-11
**Verdict:** NOT A BUG — closed as smoke-test false positive

## Investigation
The smoke test (2026-06-10) probed `GET /api/analytics/summary` and reported 404.
That route never existed in any contract or client:

- **AnalyticsController** exposes 6 routes: `expiry-summary`, `write-offs`,
  `movements`, `by-zone`, `by-category`, `losses`
- **Frontend** (`features/analytics/api/analytics.ts`) calls exactly those 6 — no `/summary`
- **Mobile** (`features/dashboard/api/dashboardApi.ts`) calls `/analytics/expiry-summary`
- **api-contracts.md** listed a stale planned route `/api/analytics/dashboard`
  (never built, never called) — now corrected to `expiry-summary`

The smoke tester guessed the route name; the real "summary" endpoint is
`/api/analytics/expiry-summary`.

## Production verification (2026-06-11, ea@demo.local)

| Endpoint | Status |
|---|---|
| GET /api/analytics/expiry-summary | 200 ✅ |
| GET /api/analytics/write-offs | 200 ✅ |
| GET /api/analytics/movements | 200 ✅ |
| GET /api/analytics/by-zone | 200 ✅ |
| GET /api/analytics/by-category | 200 ✅ |
| GET /api/analytics/losses | 200 ✅ |

`expiry-summary` returns correct tenant-wide counts (25 batches:
11 safe / 7 warning / 5 critical / 2 expired) plus per-store breakdown
for both demo stores.

## Changes
- `.claude/docs/api-contracts.md` — replaced stale `/api/analytics/dashboard`
  row with the real `expiry-summary` route (no code changes needed)
