# Handoff: TASK-503 (frontend) → TASK-504 (qa-tester)

Store-migration section is live on `/marketing-analytics`, always rendered below the RFM
segment detail panel, driven by the page's existing period/store filter bar (no new filter
UI). Full frontend detail in `.claude/logs/tasks/503_2026-08-10_store-migration-frontend_frontend-developer.md`.

## #1 thing to check: populated data, not just empty states

I could only verify against the local seeded tenant, which has 13 active customers but **zero
cross-store purchase history** in any period tested — every response came back with
`flows: []`, `netFlowByStore: []`, `migratedCustomerCount: 0`. I confirmed the empty states
render cleanly (matrix shows "У цьому періоді міграцій між закладами не виявлено", customer
table shows the same, KPI row shows `—` for best-gain/worst-loss), but **never saw an actual
non-empty matrix cell, a populated customer row, or a real net-flow KPI on screen.**

With real/seeded multi-store migration data, please specifically check:
- **Matrix** (`StoreMigrationMatrix.tsx`): axis only includes stores that actually appear in
  `flows` (not the tenant's full store list) — verify with 3+ stores where only 2 have
  cross-traffic that the matrix doesn't show a 3rd empty row/column. Cell hover tooltip shows
  customer count + revenue.
- **Customer table** (`StoreMigrationCustomerTable.tsx`): phone/email always masked (no
  toggle on the table itself — by design, matches the backend: `/store-migration/customers`
  has no unmask param). Row count vs. the "showing first 100" truncation note when
  migrated customers exceed the on-screen limit (100).
- **Store filter OR-semantics**: selecting a single store in the page's existing store
  multi-select should surface customers who EITHER left OR arrived at that store (not AND) —
  confirm the matrix/KPI/table all update consistently together (same `filters` object drives
  all 3 new queries).
- **Export**: unmask-PII checkbox only appears next to the export button for a role that
  passes `canExportMarketingAnalyticsPii` (store_manager+ or the `marketing_analytics.export_pii`
  capability) — check it's hidden for a lower role, and that the downloaded `.xlsx` actually
  contains unmasked phone/email only when checked + role permits (`UnmaskPii` is silently
  downgraded server-side otherwise, per the TASK-502 handoff).
- **KPI row math**: best-gain/worst-loss store selection — verify against `netFlowByStore`
  from the raw API response (`GET /api/marketing-analytics/store-migration`) rather than just
  eyeballing the UI, since ties/negative-only/positive-only cases are easy to get subtly wrong.

## Everything else already verified (frontend-developer, TASK-503)

- `npx tsc --noEmit` clean.
- All 3 endpoints fire with correct query params on page load and on filter change (period
  switch confirmed via network tab — atomic refetch of overview + store-migration +
  store-migration/customers together).
- Both `uk` and `en` locales render natural translated copy (no missing-key fallback text).
- Export button fires the POST and triggers a browser download (200 response confirmed:
  couldn't inspect the actual `.xlsx` bytes/content from the browser tooling used).
- Single-store guard code path (`useStores().length <= 1`) is implemented but **not exercised
  live** — the test tenant has multiple stores, so I never actually saw the "Ця аналітика
  доступна для мереж із кількома закладами" notice on screen. Worth a quick look with a
  single-store tenant account if one exists.

## Files touched

- `frontend/features/marketing-analytics/types.ts`
- `frontend/features/marketing-analytics/api/marketingAnalytics.ts`
- `frontend/features/marketing-analytics/hooks/useMarketingAnalytics.ts`
- `frontend/features/marketing-analytics/components/StoreMigration/StoreMigrationSection.tsx`
- `frontend/features/marketing-analytics/components/StoreMigration/StoreMigrationMatrix.tsx`
- `frontend/features/marketing-analytics/components/StoreMigration/StoreMigrationCustomerTable.tsx`
- `frontend/app/(dashboard)/marketing-analytics/page.tsx`
- `frontend/messages/uk.json`, `frontend/messages/en.json`
