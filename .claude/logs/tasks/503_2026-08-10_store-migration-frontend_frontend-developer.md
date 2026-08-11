# TASK-503: Store migration frontend section (RFM dashboard)

**Status:** done
**Agent:** frontend-developer

## What was built

New "Міграція покупців між закладами" section on `/marketing-analytics`, always rendered
below `SegmentDetailPanel`, driven by the page's existing `filters`/`enabled` state (no new
filter UI).

- `frontend/features/marketing-analytics/types.ts` — added `StoreMigrationOverviewDto`,
  `StoreMigrationFlowDto`, `StoreNetFlowDto`, `StoreMigrationCustomerRowDto`,
  `ExportStoreMigrationRequest`. Added to the root types file (not a sibling
  `store-migration/types.ts`) since this is a page-section, not a separate route like
  post-campaign/price-segments/audience-builder.
- `frontend/features/marketing-analytics/api/marketingAnalytics.ts` — `getStoreMigration`,
  `getStoreMigrationCustomers`, `exportStoreMigration` (blob download via existing
  `downloadFilePost`), following the exact `buildFilterQs`/timestamp conventions already used.
- `frontend/features/marketing-analytics/hooks/useMarketingAnalytics.ts` —
  `useStoreMigration(filters, enabled)`, `useStoreMigrationCustomers(filters, limit, enabled)`,
  `useExportStoreMigration()`. Query keys: `["marketing-analytics","store-migration",filters]`
  and `["marketing-analytics","store-migration-customers",filters,limit]`.
- New folder `frontend/features/marketing-analytics/components/StoreMigration/`:
  - `StoreMigrationSection.tsx` — KPI row (migrated count, % of active, best-gain store,
    worst-loss store) + matrix + customer table. Empty-state guard: `useStores().length <= 1`
    skips calling the new endpoints entirely and shows `t("singleStoreNotice")` instead.
  - `StoreMigrationMatrix.tsx` — from×to table with a DYNAMIC axis built from the stores that
    actually appear in `flows` (not the tenant's full store list, not RFM's fixed 12-axis).
  - `StoreMigrationCustomerTable.tsx` — drill-down list, PII always masked (matches the
    backend: no unmask param on this GET). Export button + unmask-PII checkbox (gated by
    `canExportMarketingAnalyticsPii`) live in this component's header, since the unmask
    capability only ever applies to the export, not the on-screen list.
- `frontend/app/(dashboard)/marketing-analytics/page.tsx` — wired
  `<StoreMigrationSection filters={filters} enabled={enabled} />` after `SegmentDetailPanel`.
- `frontend/messages/{uk,en}.json` — new keys under
  `Dashboard.marketingAnalytics.storeMigration.*` (section title, KPI labels, matrix/table
  headers, export button, empty/loading states).

## Deviation from the brief

`KpiCard` in `page.tsx` is a private, non-exported inline component — importing it across the
app/→feature boundary would invert this codebase's dependency direction (and several other
pages already duplicate a small local `KpiCard` rather than share one — `price-segments/page.tsx`,
`FrequencyKpiCards.tsx`, `AllTimeKpiCards.tsx`). Defined a local copy with the identical
label/value/sub/color shape inside `StoreMigrationSection.tsx` instead, matching that existing
convention.

## Verification

- `npx tsc --noEmit` in `frontend/` — clean, 0 errors.
- Manual check: ran backend (`dotnet run --project backend/ShelfGuard.Api`, CORS origins
  temporarily extended via `Cors__Origins` env var for this session only — no files changed)
  and frontend (`next dev -p 3100`) against the real local Postgres (port 5435). Confirmed:
  - All 3 new endpoints fire on page load with correct query params
    (`?period=6m`, `?period=6m&limit=100`) and return 200.
  - Overview response shape matches the TS types exactly (camelCase,
    `activeCustomerCount`/`migratedCustomerCount`/`flows`/`netFlowByStore`/etc.).
  - Switching period preset ("All time") atomically refetches all 3 endpoints with the new
    `period=all` — filters object is correctly threaded into the React Query keys.
  - Section renders correctly in both `en` and `uk` locale (switched via the `sg_locale`
    cookie) — natural Ukrainian copy, no missing-key fallback strings.
  - Export button → `POST /api/marketing-analytics/exports/store-migration` → 200 → blob
    download triggered via the shared `downloadFilePost`.
  - No console errors attributable to the new code.
- **Not verified: populated matrix/customer-table visuals.** The seeded local tenant (13
  active customers) has no actual cross-store purchase history in this period — every fetch
  returned `flows: []`, `netFlowByStore: []`, `migratedCustomerCount: 0`. Confirmed the
  "no migrations in this period" empty state renders cleanly in both the matrix and the
  customer table, and the KPI row shows `—` for best-gain/worst-loss. **Flagged for QA
  (TASK-504) as the #1 thing to check with real/seeded multi-store migration data**: matrix
  cell rendering/tooltip, customer table row rendering with masked PII, and the
  store-filter OR-semantics (one store selected shows both directions).
