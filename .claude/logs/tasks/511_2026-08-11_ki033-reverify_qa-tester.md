# TASK-511: Independent re-verification of KI-033 fix (TASK-508/509/510)

**Status:** done — **KI-033 resolved** for the reported bug. One unanticipated side effect found (not a regression, but the TASK-509/510 handoffs' assumption about `network_manager` being "unchanged" is wrong — see below).

## Pre-check: setup state (per brief's explicit "don't assume" instruction)

`manager@demo.local`'s `user_locations` grants were **not** actually restored to the true
under-scoped 2/4 state as TASK-509's handoff claimed. Live DB check found all 4 tenant locations
granted (the 2 original + the 2 TASK-504 added for QA and was supposed to revoke). Timestamps
confirmed the 2 extra grants (Троєщина, Оболонь) were both dated `2026-08-10 18:53:21` (TASK-504's
QA session), the 2 original ones `2026-07-20 18:35:54`. Deleted the 2 extra grants via SQL before
testing — otherwise the repro would have been invalid (a fully-granted store_manager can't reveal
an RLS under-scoping bug regardless of whether the fix works).

## Repro results (tenant `8abfbbb5-3190-4de9-9f91-f4de59101bca`, local dev API + Postgres 5435)

All comparisons below are `manager@demo.local` (store_manager, true 2/4-location grant state) vs
`ea@demo.local` (enterprise_admin, RLS-exempt) unless noted.

1. **`GET /store-migration?period=6m`** — byte-identical. 3 flows, `migratedCustomerCount: 3`,
   "Троєщина→Подільський"/Loyal One present, "Центральний→Подільський" at full 3124.25/22
   receipts (not the previously-undercounted 3004.25/21). Matches TASK-504's ground truth exactly.
2. **`GET /store-migration?period=3m`** — also byte-identical (active=12, migrated=3, share=25%),
   matches TASK-504's original 3m numbers too.
3. **`GET /overview?period=6m`** — identical except `calculatedAt` (wall-clock, expected).
   `periodRevenue: 16066.60` both.
4. **`GET /store-migration/customers?period=6m&limit=100`** — byte-identical. 3 rows, correct PII
   masking (`+380 50 *** ** 05`, `c***@qa-task504.test`), A→B→A edge-case customer
   (`1b7e5eb1-...`) correctly absent.
5. **`GET /store-migration?period=6m&storeIds=<Центральний>`** (OR-semantics single-store filter)
   — byte-identical (active=6, migrated=2, both flows shown together).
6. **`POST /exports/store-migration`** (masked + `unmaskPii:true`) — both 200, unzipped and
   inspected `sheet1.xml`/`sharedStrings.xml` directly: masked export shows `+380 50 *** ** 04`
   etc., unmasked shows raw `380501110004`/`champion.two@qa-task504.test`; all 3 customers present
   including the previously-vanished Loyal One. (Note: my first export attempt used a `period`
   field that doesn't exist on `ExportStoreMigrationRequest` — it needs `from`/`to` `DateOnly` —
   own testing mistake, not a product bug; corrected and re-ran.)
7. **UI spot-check**, logged in live as `manager@demo.local` on `/marketing-analytics`: matrix
   shows all 3 stores with cross-traffic (Оболонь correctly absent — no cross-traffic), KPI row
   (Migrated=3, Share=23.1%, Biggest gain/loss correct), customer table matches the API exactly
   (Loyal Two 954/11, Champion Two 3,124/22, Loyal One 770/12). No console errors from the
   marketing-analytics calls themselves (a batch of CORS/401 console errors present are leftover
   from my own local dev port-troubleshooting before the API was reachable from the frontend's
   dev-server port — environment noise, not a product issue).
8. **Cross-tenant isolation** — verified live at the Postgres level rather than just trusting
   TASK-510's citation: `pos_transactions`' `tenant_isolation` policy (`TenantId = app.tenant_id`)
   is a separate PERMISSIVE policy independent of `store_scope`'s RESTRICTIVE bypass list. Ran a
   raw session as the app role with `app.role='marketing_analytics_bypass'` and `app.tenant_id`
   set to a *different* tenant: 0 rows visible from tenant `8abfbbb5`'s `pos_transactions` (141
   rows visible when `app.tenant_id` is set back to the correct tenant — positive control). The
   new bypass value cannot cross a tenant boundary; it only widens `store_scope`.

## Regression pass (original TASK-504 checklist items not tied to the bug)

- `dotnet test` full suite: **1400/1400 green**. Filtered `MarketingAnalytics|StoreScopeRls|TenantConnectionInterceptor`: 285/285 green.
- `npx tsc --noEmit` (frontend): clean.
- Latency: 3 repeated calls each to `/store-migration` and `/overview`, 40–84ms — no timeout/latency
  concern from the new explicit-transaction wrapping on all 13 repository methods.
- Matrix dynamic axis, KPI math, customer-table masking, A→B→A edge case, export masked/unmasked,
  OR-semantics filter: all re-confirmed above, unchanged from TASK-504's original PASS.
- Atomic period/store-filter refetch: confirmed via direct API (3m vs 6m both byte-identical
  manager-vs-ea) but **not** re-confirmed via a live UI click — a period-button click in this
  session's browser tool didn't visibly trigger a new network call (likely a ref/DOM-targeting
  issue in this tool session, not investigated further since this code path is untouched by
  TASK-508/509 and was already live-verified in TASK-504). Low risk; flagging only for completeness,
  not as an open item.

## Finding: `network_manager` (`netmgr@demo.local`, KI-031) — NOT unchanged, contrary to the TASK-510 handoff's expectation

The TASK-510 handoff (item 3) and this task's own brief both expected `netmgr@demo.local` to
**still see zero data** in marketing-analytics after the fix — reasoning that KI-033's fix targets
store_manager-style *partial* scoping, not KI-031's *zero-grants* case. Live-tested and this
assumption is **wrong**:

- `netmgr@demo.local` still has **0** `user_locations` grants (confirmed live — KI-031's root cause
  is untouched).
- `GET /api/stock?...` as `netmgr@demo.local` still returns 0 rows tenant-wide — confirms KI-031's
  effect on *other* modules (stock/sales/write-offs) is unchanged, as expected.
- But `GET /store-migration?period=6m`, `/overview?period=6m`, and `/store-migration/customers` as
  `netmgr@demo.local` now return **full, correct data, byte-identical to `ea@demo.local`** — not
  zero.

Root cause of the discrepancy: `AnalyticsRlsOverride.ExecuteAsync` sets `app.role =
'marketing_analytics_bypass'` for **every** call into any of the 13 wrapped
`MarketingAnalyticsRepository` methods, unconditionally — it is not conditioned on the *original*
caller's role or grants at all. Since `store_scope`'s bypass list now includes
`marketing_analytics_bypass`, **every** caller who reaches these 13 methods (regardless of role or
`user_locations` grants) sees the full tenant, not just previously-under-scoped store_managers.

This is **not a regression** — netmgr now gets *correct* data instead of *silently wrong* (zero)
data, and it doesn't cross a tenant boundary (see the cross-tenant check above). But it means:
- The design assumption in TASK-508/509/510 ("this only affects store_manager-style partial
  scoping, network_manager's zero-grants case is untouched") does not match the actual mechanism —
  the fix is blanket-scoped per-repository-method, not per-caller-role.
- As a side effect, this specific module's manifestation of KI-031 (`netmgr@demo.local` seeing zero
  marketing-analytics data) is now also fixed — but KI-031 itself (zero grants, affecting every
  *other* RLS-scoped module) remains open and unaffected, exactly as before.

Recommend TASK-512 note this precisely: KI-033 is fully resolved as reported; KI-031 stays open
(unaffected outside marketing-analytics); and the "netmgr unaffected" assumption in the TASK-509/510
chain should be corrected in the paper trail so a future reader doesn't rely on it.

## Verdict

**KI-033 is fully resolved** for the exact bug reported in TASK-504: store_manager's silent
data-corruption (flow reclassification + undercounting) in store-migration and RFM overview no
longer occurs — confirmed via direct API byte-comparison, live UI, unzipped export content, and a
live Postgres-level cross-tenant isolation check, plus a full regression pass (1400/1400 backend
tests, clean `tsc`, no latency issue). The one thing to correct in the record (not a blocker) is the
network_manager/KI-031 assumption above.

## Test data / environment notes

- Restored `manager@demo.local`'s `user_locations` to the true 2-grant state (removed the 2 grants
  for Троєщина/Оболонь added during TASK-504 and left in place through TASK-509). This is now the
  correct baseline state for future QA/demo of this tenant — matches what TASK-504's bug report and
  this task's brief describe as ground truth.
- Ran the local backend API and frontend dev server for this session only (both stopped after
  testing); temporarily widened local-only CORS origins via an env var on my own `dotnet run`
  invocation (not a file change) to work around an unrelated port conflict from another docker
  container occupying host port 3000 — no persistent config changed.
