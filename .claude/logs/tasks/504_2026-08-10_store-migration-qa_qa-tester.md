# TASK-504: Store migration QA (RFM dashboard cross-store analysis)

**Status:** done (1 bug found and reported, not fixed — see handoff)
**Agent:** qa-tester

## Test data used

Local dev Postgres (docker `crmproductsystems-postgres-1`, port 5435), tenant "Свіжий Кут"
(`8abfbbb5-3190-4de9-9f91-f4de59101bca`), which already had 2 stores (Подільський, Центральний)
and 13 active RFM-SEED customers but **zero cross-store purchase history** (confirmed before
seeding — matches what both handoffs flagged).

Added via direct SQL (no code changes, no migration):
- 2 new `locations`: "Свіжий Кут Троєщина (QA TASK-504)" (`f3269957-c1d0-4e61-b9fa-22c6a450fc6a`,
  participates in migrations) and "Свіжий Кут Оболонь (QA TASK-504, no migration)"
  (`f611ad5d-13d0-4609-a8a4-a43a6aae7850`, deliberately kept with zero transactions — used to
  verify the matrix/axis excludes stores with no cross-traffic).
- 11 `pos_transactions` rows (`QA504-1`..`QA504-11`) giving 5 existing RFM-SEED customers
  (Champion One/Two/Three, Loyal One/Two) extra cross-store purchases.
- 1 new customer "QA504 Edge Case A-B-A" (`1b7e5eb1-f37e-417a-a80d-16a38d12aa28`) + 3 clean
  transactions (Store1 → Store2 → Store1) as an isolated, unambiguous test of the "visited a
  2nd store but ended up back at the 1st — not migrated" rule.
- Email set on 2 customers (Champion One, Champion Two) to exercise masked-email rendering
  (no customer in this tenant had an email before).
- `user_locations` grant for `manager@demo.local` (store_manager) covering the 2 new stores —
  required to work around a pre-existing RLS gap, see Bug #1 below.

Note: the pre-existing RFM-SEED bulk transactions (all dated 2026-07-23..07-26, all at Store1)
ended up chronologically *after* several of my hand-picked "migration" dates, which changed 2 of
my 5 intended flows (customers ended up back at Store1 as their "last" store instead of my
intended 2nd store). Recomputed expected values from the actual resulting data via raw SQL
before testing the API/UI, rather than fighting the existing seed data — see full numbers below.

Final ground truth (period 2026-02-10..2026-08-10, verified via direct SQL against
`pos_transactions`, confirmed byte-identical to the `enterprise_admin`-token API response):
- Flows: Подільський→Центральний (1 cust, 953.90), Троєщина→Подільський (1 cust, 769.70),
  Центральний→Подільський (1 cust, 3124.25). MigratedCustomerCount=3, ActiveCustomerCount=13,
  share=23.08%. NetFlowByStore: Подільський +1, Центральний 0, Троєщина −1 → bestGain=Подільський,
  worstLoss=Троєщина.

## Checklist results

| # | Item | Result |
|---|---|---|
| 1 | Matrix renders real non-empty cells; axis = only stores in `flows`, not full tenant list | **PASS** — verified live in UI: 4-store tenant, only the 3 stores with cross-traffic appear in the matrix axis; "Оболонь" (0 migrations) correctly absent. Cell hover tooltip (`title` attr) shows `"N customers · X revenue"`. |
| 2 | KPI row math (migrated count, % active, best-gain/worst-loss) | **PASS** — hand-verified against raw `GET /store-migration` JSON: migrated=3, share=3/13=23.08%, `NetFlowByStore` = Gained−Lost per store, correct. Also caught a good edge case live: filtering to a single store where all nets happen to be 0 correctly renders "—" for both best-gain/worst-loss (the `net<=0`/`net>=0` guards in `bestNetFlow()` work as intended, not just decorative). |
| 3 | Customer table: populated rows, PII masked, correct from/to+dates, truncation note | **PASS** — 3 rows rendered, phone masked `+380 50 *** ** 05` format, email masked `c***@qa-task504.test`, `—` for customers with no email. Truncation note correctly absent (3 rows < 100 limit). Minor nitpick (not filed as a bug): `rows.length >= limit` at `StoreMigrationCustomerTable.tsx:148` will show the "showing first N" note even when the migrated count is *exactly* the limit (100) and there's nothing truncated — cosmetic, only triggers at an exact 100-row boundary. |
| 4 | Store filter OR-semantics (one store selected → both directions) | **PASS** — verified via API and live UI click-through: selecting only "Центральний" showed both the Подільський→Центральний and Центральний→Подільський flows/customers together (2 migrated, not 1), matrix axis shrank to just the 2 touching stores, RFM overview panel above it updated in sync too. |
| 5 | "First→last" A→B→A not-migrated edge case | **PASS** — dedicated clean customer with Store1(07-03)→Store2(07-15)→Store1(07-27) confirmed absent from both `flows` and the customer list via direct SQL and the API. |
| 6 | Export: `.xlsx` downloads, 9 columns, PII masked by default, unmask for permitted role | **PASS** — downloaded and unzipped both a masked and `unmaskPii:true` export via curl as `manager@demo.local` (store_manager). Masked: `+380 50 *** ** 05` / `c***@qa-task504.test`. Unmasked: raw `380501110005` / `champion.two@qa-task504.test`. All 9 expected headers present in order. Also confirmed via live UI: checked "Show full phone number and email" → clicked Export → POST fired with 200. |
| 7 | Export gating for a role below the capability floor | **Not directly comparable to the brief's framing** — `CanViewAnalyticsRoles` (page access) and `AtLeastStoreManagerRoles` (PII export) are the *same* role set (`Provider/EnterpriseAdmin/NetworkManager/StoreManager`) in this codebase, so there is no role that can view the page via bare role but not export PII — merchandiser (below store_manager) gets `403` on every marketing-analytics endpoint and the frontend correctly redirects away from `/marketing-analytics` before rendering anything (verified live). The "view but not export" case only exists via a granular `marketing_analytics.view`-without-`export_pii` capability override (ADR-020), not present in seed data — not exercised, but this is pre-existing, reused authorization plumbing (`MarketingAnalyticsAuthorization.CanExportPii`), not new code from this feature. |
| 8 | Single-store tenant guard | **Not verified** — no single-store tenant with an active module + logged-in-able user exists in the dev DB (the 4 single-location tenants found are stray concurrency-test fixtures with no users and `modules=[]`). Same as frontend handoff's flag. Code review: `StoreMigrationSection.tsx:77-78` (`stores.length > 1` gate, endpoints not even called when false) is simple and low-risk by inspection. |
| 9 | Period/store filter changes re-fetch all 3 queries together, no stale flash | **PASS** — verified via network tab: switching period (6m→3m) and store filter both fire `overview` + `store-migration` + `store-migration/customers` as one atomic batch each time; numbers on screen updated consistently together (e.g. 3m: active=12, migrated=3, share=25%). |
| 10 | Regression: `dotnet test --filter MarketingAnalytics`, `npx tsc --noEmit` | **PASS** — 250/250 backend tests green, tsc clean. Seeded data lives under a real tenant, untouched by the integration tests (which create/teardown their own randomly-named tenant). |

## Bug found — HIGH severity, pre-existing root cause, first surfaced as wrong math here

**Store-scope RLS silently produces incomplete/wrong store-migration data for any caller who
isn't `provider`/`provider_admin`/`worker`/`enterprise_admin`.**

`pos_transactions` has a RESTRICTIVE `store_scope` RLS policy (Stage 3 rollout,
`20260719193545_AddLocationStoreScopeRlsPolicies.cs`) that only lets a caller see rows whose
`LocationId` is in their own `user_locations` grants (unless their role is in the bypass list).
The new `GetStoreMigrationFlowsAsync`/`GetStoreMigrationCustomersAsync`/
`GetActivePeriodCustomerCountAsync` queries run through the caller's own RLS session like any
other query — so a `store_manager` scoped to only *some* of the tenant's stores (the normal,
expected shape for this role per KI-031) gets a silently wrong answer, not just a narrower one:

- Reproduced with `manager@demo.local` (store_manager, granted only the tenant's original 2
  stores, not the 2 new test stores): the "Троєщина→Подільський" flow **disappeared entirely**
  (not filtered — the customer's true earliest transaction was at the unscoped store, so their
  visible first/last store collapsed to the same store, reclassifying a real migration as "not
  migrated"), and the visible "Центральний→Подільський" flow's revenue/receipt-count were
  silently **undercounted** (3004.25/21 receipts vs the true 3124.25/22 — missing exactly the one
  transaction at the unscoped store, no partial-data indication anywhere).
- Compared against `ea@demo.local` (enterprise_admin, RLS-exempt) on the identical request: got
  the full, correct 3-flow answer that matches raw SQL ground truth exactly.
- `netmgr@demo.local` (network_manager) gets **zero data across the entire module**, not just
  store-migration — this part is already tracked as **KI-031** (network_manager has 0
  `user_locations` grants in seed data; confirmed still reproducing).
- The store_manager-specific silent-undercounting behavior is **not** covered by KI-031 — KI-031
  is about a demo account having zero grants; this is about what happens for a *normally*
  provisioned store_manager (scoped to their real subset of stores), which is the realistic
  production shape of this role, and which the frontend explicitly treats as a first-class user
  of this feature's most sensitive capability (`canExportMarketingAnalyticsPii` = store_manager+).

Worked around for the rest of this QA pass by granting `manager@demo.local` `user_locations`
rows for the 2 new test stores (mirrors KI-031's own resolution pattern) — after that grant, the
store_manager view matched enterprise_admin exactly, confirming the aggregation/math logic itself
(repository CTEs, `NetFlowByStore` derivation, share-percent) is correct; the bug is purely about
whose transactions the query is allowed to see.

Also worth noting: the pre-existing RFM overview endpoint (`GET /marketing-analytics/overview`,
shipped since TASK-404..418) has the same underlying issue (store_manager's `periodRevenue` was
understated by exactly the 2 new-store transactions) — so this is architecture debt the whole
module inherited, not something introduced fresh by TASK-501/502/503. Store-migration is just the
first place it manifests as an outright wrong classification (migrated→not-migrated) rather than
"just" a smaller total.

Full repro + numbers in the handoff note below.

## Not filed as bugs (checked, working as designed)

- `network_manager` seeing zero data is the already-tracked KI-031, not new.
- Merchandiser 403 on the whole controller is by design (`CanViewAnalyticsRoles` floor).
- `StoreMigrationCustomerTable.tsx:148` truncation-note-at-exact-limit is a cosmetic nitpick, not
  filed as a formal bug (see checklist item 3).
