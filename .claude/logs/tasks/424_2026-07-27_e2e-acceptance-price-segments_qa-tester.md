# TASK-424: E2E acceptance/regression — Фаза 2 (price segments + frequency/reactivation)

**Agent:** qa-tester
**Date:** 2026-07-27
**Status:** done — **verdict: SHIP WITH ONE FOLLOW-UP.** No blocker, no crash, no data-integrity
failure anywhere in the scenario. 1 real MEDIUM-severity gap found (on-screen PII never masked in
any of the 3 new tables) that none of TASK-419/420/421/422 caught — a genuine cross-agent stitch
point, not covered by fixing it myself per this task's testing-only scope.

## Scope

Independent E2E pass over TASK-419 (schema)/420 (backend)/421 (frontend)/422 (security) — Фаза 2
price segments + frequency/reactivation. Read all 4 task logs first, then the actual controllers/
services/DTOs/components directly. Tested against the real dev stack: `dotnet run` backend on
:5000, `npm run dev` frontend on :3000, live Postgres `crmproductsystems-postgres-1:5435`, real
seeded tenant "Свіжий Кут" (13 customers, 127 POS transactions, all customer-linked) — real HTTP
calls (curl, with fresh JWTs) for precise data/pagination/threshold verification, real browser
interaction (Next.js dev server, store_manager session) for the on-screen stitch points an API
test can't see.

## Finding — on-screen customer PII (phone) is never masked in any of the 3 new tables

**Severity: medium.** Not exploitable under today's role configuration (see why below), but a real,
live-verified gap in a control the codebase explicitly relies on elsewhere.

**Repro:** open `/marketing-analytics/price-segments`, any of the 3 modes, open any audience/segment
table. The phone column always shows the full raw number (e.g. `380501110012`). The "Показати
повний номер телефону" checkbox above the table visually implies it controls this column — toggling
it on/off live in the browser (confirmed both ways) produces **zero change** to the on-screen
digits; it only tags the subsequent `POST .../exports/*` call's `unmaskPii` flag. Confirmed the same
raw-phone-always behavior via direct `GET .../audiences/{a}` calls too (not a client-side rendering
quirk — the JSON payload itself carries the unmasked phone unconditionally).

**Root cause:** `PriceAudienceTable.tsx:113`, `AllTimeCustomerTable.tsx:102`,
`FrequencyAudienceTable.tsx:137` all render `{r.phone ?? "—"}` directly, with no masking logic
anywhere in any of the three — and the backend's `GetAudienceTableAsync`/`GetAllTimeCustomerTableAsync`/
`GetFrequencyAudienceTableAsync` return the customer's real `Phone` unconditionally (masking exists
only inside the 3 `Build*Excel` export builders). This is a new exposure *surface*: Фаза 1's RFM
dashboard (checked for comparison) has no per-customer table at all — its only PII surface was
always the export, which is why the export-only masking pattern was never questioned before. Фаза 2
is the first place in this codebase with a live, paginated, on-screen customer-identity table, and
nobody building or reviewing it asked the new question "does the SCREEN need masking too, not just
the file" — TASK-421 built the toggle to pair with the export button (correct per its own scope),
TASK-422's checklist item #3 was scoped to "PII in exports" (correct per Фаза 1's precedent, which
never had this surface), so neither agent's brief pointed them at the actual gap.

**Why not critical today:** `CAN_VIEW_ANALYTICS` (view floor: enterprise_admin/network_manager/
store_manager) and `MarketingAnalyticsAuthorization.CanExportPii`'s base-role floor
(`AtLeastStoreManagerRoles`) are currently the *same* role set — nobody can view this page today
without also qualifying for unmasked export, so no role gets *less* than what they already see raw
on-screen. **Why it matters anyway:** this codebase's own `RequireModule`+`OrCapability` pattern
(ADR-020, exercised by this exact feature per TASK-422's note "a granted capability holder below
store_manager can now actually reach the export endpoints") is explicitly designed to let a tenant
grant `marketing_analytics.view` to a sub-store_manager role *without* also granting
`marketing_analytics.export_pii`. The moment any tenant does that (a supported admin action, not a
misconfiguration), that role sees every customer's full phone number on-screen with zero gate — the
export mask becomes theater, since the same data was already fully visible one click earlier.

**Not fixed** (testing only, per brief). Recommend routing to backend-developer (mask `Phone` in the
3 GET-table service methods the same way the export builders already do, gate the raw value behind
`CanExportPii`) + a quick security-reviewer re-check of just this path.

## Scenario coverage (everything below passed)

**Comparison mode, 30/60/90-day switch:** confirmed 3 genuinely distinct numbers every time —
period=30: analyzed=1, current-buyers=9, previous-buyers=3; period=60: 2/11/2; period=90: 0/11/1
(all-zero cohort at 90d is itself a clean edge case, no crash). Matches TASK-421's own live numbers
exactly, independently reproduced in this session.

**4 audiences (RealGrowth/PriceGrowth/Declining/Stable), real data on each:** RealGrowth (period=60,
CannotLose One: Tier5→Tier7 + items/receipt up = "real" growth), Stable (period=30, Attention One,
Tier4→Tier4), Declining (see threshold section below, needed a custom period to get a natural
signal in this dataset). **Server-side pagination genuinely proven** (not client slicing): used
Frequency's "Growing" audience (11 real rows, same repository/pagination code as all 3 tables per
TASK-422's own trace) with `pageSize=3` — page 1 and page 2 returned completely disjoint customers,
`sortBy=ltv` descending vs ascending produced correctly reversed, re-sorted (not just reversed-slice)
results; re-confirmed on All-time's 13-customer table (`pageSize=5`, 3 pages, alphabetically
disjoint). **Export PII masking verified at the byte level**, not assumed: unzipped the actual
`.xlsx`, masked export's `sharedStrings.xml` has `+380 50 *** ** 12`, unmasked has raw
`380501110012` — both from the same live audience.

**All-time mode:** KPIs (13 customers, 119₴ avg check, 125 purchases, 14 847₴ turnover, distribution
summing to 13) confirmed via API and live in the browser. Clicked the "83–96 ₴" distribution column
live in the browser — table AND recommendation both atomically filtered to the same 3 customers in
that tier, matching the API response exactly.

**Frequency mode, all 4 audiences:** Sleeping (live browser + API) — filter labels correctly flip to
"…попередній період" wording, no decline-threshold field, `Тип. чек` renders `—` (never `0`) both
on-screen and in the export XML. Confirmed the *previous-period re-orientation* is real, not just
labeled: `minSpend=1` still matched a Sleeping customer whose current-period spend is 0 (would have
been wrongly excluded if the filter used current spend); `minSpend=999999` correctly excluded them.
Declining — live browser confirms the threshold field appears and labels stay "поточний". Found a
real customer (CannotLose One: 13→9 receipts month-over-month, -30.77%) straddling the default 30%
threshold and proved the boundary is live, not cosmetic: threshold=35 → excluded (0 rows),
threshold=25 → included, threshold=30 (default) → included. `>=`, not `>`, confirmed at the boundary.

**Balance checks, multiple independent datasets:** sum of 4 price audiences = analyzedCount (checked
at 3 periods, including the 0/0/0/0 edge case). Sum of Sleeping+Declining = atRiskCount (checked at
period=90 default: 1+0=1; custom period: 0+1=1).

**Edge cases:** filtered every one of the 3 overview endpoints to a real 1-customer store
(`f52b6d99…`, 3 real transactions) — all returned clean `200`s; All-time's percentile boundaries
degenerated to `28–28 ₴` repeats (mathematically correct for n=1, not an error); Frequency correctly
classified the lone customer as Growing (`previous=0`). Extreme filters (`minSpend=999999999`)
produced a clean empty-table `200` with `totalPages:0` and a recommendation that gracefully renders
"0 клієнтів (0%)" instead of dividing by zero — confirmed both via API and live in the browser
("Немає клієнтів у цій аудиторії за обраний період і заклади.", no crash, export controls correctly
hidden).

**Settings (`PriceSegmentSettingsController`):** baseline `GET` before any save returns proposed
defaults (30%, null, `updatedAt:null`). `PUT` validation: `-5` → 400 "must be between 0 and 100",
`150` → 400 (same message), `MinReceiptsForBoundaries:-1` → 400 "cannot be negative"; boundary
values `0` and `100` (inclusive) → `200`, correctly not rejected. Role gate: storekeeper 403 on both
the main feature and Settings; **store_manager also correctly 403 on Settings** (confirms the
stricter `AtLeastEnterpriseAdmin`-only gate wasn't accidentally loosened to match the feature's
looser view floor). Proved the persisted default is actually read (not a hardcoded fallback): set
tenant default to 50% → the same CannotLose One 30.77%-decline row disappeared from the Declining
table with **no** explicit `declineThresholdPercent` in the request.

**Adversarial re-check of TASK-422's own claim:** sent `sortBy=name; DROP TABLE customers; --` to
`all-time/customers` live — silently fell back to the safe default (`sortBy` echoed as `"check"`),
`200 OK`, data intact, `customers` table unaffected (13 rows before and after). Independent
black-box confirmation of the allowlist, not just trusting the prior code review.

**Build/test, full suite after all 4 Фаза-2 agents:**
- `dotnet test` (full suite): **1180/1180 green**, 0 failed — matches TASK-420's own reported
  count exactly, no regressions from this session's live data manipulation either.
- `npx tsc --noEmit`: clean, 0 errors.
- `npm run build`: exit 0, `/marketing-analytics/price-segments` **16.2 kB / 252 kB First Load
  JS** — identical to TASK-421's own reported size. The repeating `ENVIRONMENT_FALLBACK` traces
  during static generation are the same pre-existing, unrelated noise every prior agent already
  flagged (build still exit 0).
- No console errors or backend errors at any point in the live browser/API session.

## Cleanup performed

- Deleted the one `price_segment_settings` row created during Settings testing — verified `0` rows
  for this table across the **entire** database afterward (not just this tenant).
- No customers/transactions/tenants were created, modified, or deleted — every scenario check
  (including the "small tenant"/"empty audience" edge cases) used real pre-existing seeded data via
  read-only GETs and store/threshold filters, never new fixtures, so there was nothing else to roll
  back. Verified "Свіжий Кут"'s customer (13) and POS-transaction (127) counts are byte-for-byte
  unchanged from this session's own opening baseline check.
- Stopped both dev-preview servers (backend :5000, frontend :3000) cleanly at the end.

## Overall verdict

**Ship.** Every documented Фаза 2 behavior (comparison/all-time/frequency modes, 3-way denominator
distinction, real server pagination/sorting on all 3 tables, PII-masked exports, Sleeping's
previous-period re-orientation, Declining's live threshold, settings validation/persistence, role
gates, small-sample/empty-audience robustness, SQL-injection resistance) held up under live,
sometimes adversarial, re-verification — independent of and consistent with all 4 prior agents'
own reported results, with `dotnet test`/`tsc`/`npm run build` all green and no accumulated
regressions. The one real gap — on-screen phone never masked in the 3 new tables, only in exports —
is not a blocker under the current role configuration but is a genuine security-relevant product
gap worth a small, scoped backend follow-up before this feature is exposed to any role below
store_manager via a capability grant.
