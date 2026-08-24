# TASK-609: Multi-store selection fix — Dashboard + Analytics (frontend)

**Agent:** frontend-developer
**Status:** done
**Pairs with:** TASK-608 (backend-developer, `store_id` → `Guid[]? storeIds` widening on the same 5 endpoints)

## Bug
Selecting 2+ stores in the header `StoreSelector` only showed data for the first one on
Dashboard/Analytics, because both consumed the shared `selectedStoreIds` array via the
`usePrimaryStoreId()` shim, which drops everything but `[0]`.

## Changed
- `frontend/features/dashboard/api/dashboard.ts` — `withStore(path, storeId)` → `withStores(path, storeIds[])`, appends one repeated `storeIds=<id>` per entry. All 5 functions (`getDashboardStats`, `getAttentionItems`, `getStoreZones`, `getExpirySummaryCompare`, `getWeeklyKpi`) now take `storeIds: string[]`.
- `frontend/features/dashboard/hooks/useDashboard.ts` — all 5 hooks now read `useStoreContext((s) => s.selectedStoreIds)` instead of `usePrimaryStoreId()`, pass the full array through, array included in query keys.
- `frontend/features/shelf/api/stock.ts` — `stockApi.getAll` now sends the `store_id` param under the wire key `storeIds` (mechanical rename, still zero-or-one id — TS field name kept as `store_id` so no caller churn).
- `frontend/features/analytics/components/ProductTrendPanel.tsx` — comment-only fix (stale reference to the old wire param name).
- `QuickActions.tsx` — untouched, stays on `usePrimaryStoreId()` (mutation/action context, per doc comment on the hook).

## Analytics page — no code change
Investigated `frontend/app/(dashboard)/analytics/page.tsx` in depth per the brief's instruction to
find where it fetches expiry-summary-compare/weekly-kpi. **It doesn't call either.** Grepped both
endpoints tenant-wide: they're only ever hit from the dashboard feature (`dashboard.ts`/
`useDashboard.ts`, now fixed). The Analytics page's own hooks (`useExpirySummary`,
`useWriteOffAnalytics(Compare)`, `useZoneAnalytics`, `useCategoryAnalytics`, `useLosses(Compare)`,
`useLossesTrend`) all go through `features/analytics/api/analytics.ts`, which hits a disjoint set
of endpoints (`/api/analytics/expiry-summary` [no `/compare`], `/write-offs`, `/losses`, `/by-zone`,
`/by-category`, `/losses/trend`, `/losses/by-product`, `/by-category/products`) — none of which are
part of TASK-608's widened set.

**Net effect: the Analytics page's own metrics still only reflect the first selected store** —
same limitation as before, not fixed by this task, because the backend endpoints it actually calls
weren't widened. Fixing it for real needs those ~8 analytics endpoints widened the same way
`/api/stock/summary` etc. were, which is beyond TASK-608's scope. Flagging as a follow-up rather
than guessing at a change with no matching backend support (would either no-op or break requests).

`ProductTrendPanel.tsx` does hit one of the 5 widened endpoints (`/api/stock`, via `stockApi.getAll`)
but per the brief stays single-store (mechanical rename only) — it's a drill-down for one product at
one store, not a report aggregate.

`frontend/features/locations/api/locations.ts` — checked, `getStock()` sends no store filter at all; no change needed.

## Verification
- `npx tsc --noEmit` — clean.
- Ran `frontend-dev` (3001) + `backend-dev` (5000) via `.claude/launch.json`; backend already had
  TASK-608's `storeIds` widening live. Logged in, opened Dashboard, selected 2 stores in the header
  selector. Confirmed via `read_network_requests`: all 5 endpoints fired with `storeIds=<id1>&storeIds=<id2>`
  and returned 200. Page totals changed to the combined aggregate (e.g. Expired 409→413, Safe 202→203,
  new zone rows from the second store appeared) — not a silent revert to one store.
- Analytics page loads clean, unaffected (still shows single-store numbers as before — expected, see above).
- No new console errors introduced (pre-existing 401/RSC-prefetch noise unrelated to this change).
